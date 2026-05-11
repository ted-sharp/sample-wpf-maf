using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using SherpaOnnx;

namespace WpfMafSampleStt.Speech;

/// <summary>
/// Silero VAD で音声から発話セグメント (speech segment) を抽出する。
/// 無音区間を捨てることで Moonshine の幻覚を防ぐ。
///
/// SpeechSegment.Start (録音全体の中でのサンプル位置) と pre/post-roll を使って
/// 元の samples 配列から「少し前」「少し後」を含めて切り出すことで頭欠け・語尾切れを防ぐ。
/// </summary>
internal sealed class VadProcessor : IDisposable
{
    private readonly VoiceActivityDetector _vad;
    private readonly int _sampleRate;
    private readonly int _windowSize;
    private readonly int _preRollSamples;
    private readonly int _postRollSamples;

    public VadProcessor(SttSettings stt)
    {
        var modelPath = ResolvePath(stt.VadModel);
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException($"VAD モデルが見つかりません: {modelPath}", modelPath);
        }

        var config = new VadModelConfig();
        config.SileroVad.Model = modelPath;
        config.SileroVad.Threshold = stt.VadThreshold;
        config.SileroVad.MinSilenceDuration = stt.VadMinSilenceDuration;
        config.SileroVad.MinSpeechDuration = stt.VadMinSpeechDuration;
        config.SileroVad.MaxSpeechDuration = 20.0f;
        config.SampleRate = stt.SampleRate;
        config.NumThreads = 1;
        config.Provider = "cpu";
        config.Debug = 0;

        _vad = new VoiceActivityDetector(config, bufferSizeInSeconds: 60);
        _sampleRate = stt.SampleRate;
        _windowSize = config.SileroVad.WindowSize > 0 ? config.SileroVad.WindowSize : 512;
        _preRollSamples = (int)(stt.SampleRate * Math.Max(0, stt.VadPreRollSeconds));
        _postRollSamples = (int)(stt.SampleRate * Math.Max(0, stt.VadPostRollSeconds));
    }

    /// <summary>
    /// 録音全体を VAD に流し、検出された speech segment を pre-roll / post-roll 付きで返す。
    /// 戻り値の各 float[] は元 samples 配列の [Start - preRoll, End + postRoll] の範囲を切り出したもの。
    /// </summary>
    public List<float[]> ExtractSpeechSegments(float[] samples)
    {
        _vad.Reset();
        var rawSegments = new List<(int Start, int Length)>();

        int offset = 0;
        while (offset + _windowSize <= samples.Length)
        {
            var window = new float[_windowSize];
            Array.Copy(samples, offset, window, 0, _windowSize);
            _vad.AcceptWaveform(window);
            DrainRaw(rawSegments);
            offset += _windowSize;
        }
        _vad.Flush();
        DrainRaw(rawSegments);

        Debug.WriteLine($"[vad] raw segments={rawSegments.Count} from {samples.Length} samples ({samples.Length / (double)_sampleRate:F2}s)");

        // pre/post-roll 付きで切り出し直す。範囲が前後にかぶる場合はクランプ。
        var result = new List<float[]>(rawSegments.Count);
        for (int i = 0; i < rawSegments.Count; i++)
        {
            var (rawStart, rawLen) = rawSegments[i];
            int start = Math.Max(0, rawStart - _preRollSamples);
            int end = Math.Min(samples.Length, rawStart + rawLen + _postRollSamples);
            int len = end - start;
            if (len <= 0)
            {
                continue;
            }
            var slice = new float[len];
            Array.Copy(samples, start, slice, 0, len);
            result.Add(slice);
            Debug.WriteLine($"[vad]   seg#{i} raw=[{rawStart}..{rawStart + rawLen}] ({rawLen / (double)_sampleRate:F2}s) " +
                            $"-> extended=[{start}..{end}] ({len / (double)_sampleRate:F2}s)");
        }
        return result;
    }

    private void DrainRaw(List<(int Start, int Length)> sink)
    {
        // 公式サンプルに合わせて IsSpeechDetected も確認するが、IsEmpty で取り出せれば segment は確定済み
        while (!_vad.IsEmpty())
        {
            var segment = _vad.Front();
            if (segment.Samples is not null && segment.Samples.Length > 0)
            {
                sink.Add((segment.Start, segment.Samples.Length));
            }
            _vad.Pop();
        }
    }

    private static string ResolvePath(string relativeOrAbsolute)
    {
        if (Path.IsPathRooted(relativeOrAbsolute)) return relativeOrAbsolute;

        var baseDir = AppContext.BaseDirectory;
        var c1 = Path.GetFullPath(Path.Combine(baseDir, relativeOrAbsolute));
        if (File.Exists(c1)) return c1;

        // 開発時: bin/Debug/net10.0-windows/ から見て 5 階層上がリポジトリルート
        var dev = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", relativeOrAbsolute));
        if (File.Exists(dev)) return dev;

        return c1;
    }

    public void Dispose() => _vad.Dispose();
}
