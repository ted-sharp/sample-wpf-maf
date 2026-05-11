using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace WpfMafSampleStt.Speech;

/// <summary>
/// マイク録音 + Moonshine 認識を束ねる (PTT 専用)。
/// 録音中は累積バッファを定期的に再認識して partial 結果を流し、
/// リリース時に全サンプルで最終認識する (擬似ストリーミング)。
///
/// Moonshine は短尺音声 (~30 秒) 用モデルなので、長すぎる入力は
/// 末尾 MaxRecordingSeconds に切り詰めて投入する。
/// </summary>
internal sealed class SpeechInputService : IDisposable
{
    private readonly MicrophoneCapture _mic;
    private readonly MoonshineRecognizer _partialRecognizer;
    private readonly MoonshineRecognizer _finalRecognizer;
    private readonly VadProcessor? _partialVad;
    private readonly VadProcessor? _finalVad;
    private readonly int _sampleRate;
    private readonly int _minChars;
    private readonly int _partialIntervalMs;
    private readonly int _maxSamples;
    private readonly int _maxRecordingSeconds;
    private readonly int _partialMaxSamples;
    private readonly int _finalChunkSamples;
    private readonly int _finalOverlapSamples;
    private readonly bool _saveDebugWav;
    private readonly int _partialMinSegmentSamples;
    private string _lastPartial = String.Empty;

    /// <summary>
    /// 最終認識の「生」結果。UI 側でデバッグ表示できる。
    /// </summary>
    public string LastFinalRaw { get; private set; } = String.Empty;

    /// <summary>最終認識で VAD が抽出したセグメント数。VAD 無効時は -1。</summary>
    public int LastFinalVadSegments { get; private set; } = -1;

    private CancellationTokenSource? _partialCts;
    private Task? _partialLoop;
    private int _decoding;
    private int _stopping;

    public event Action<string>? PartialRecognized;
    public event Action<string>? FinalRecognized;
    public event Action<string>? Error;

    /// <summary>
    /// 録音時間が MaxRecordingSeconds に達して自動停止に入ったときに発火する。
    /// UI 側でこれを受けて PTT ボタンの見た目を解除し、StopPttAndRecognizeAsync を呼ぶ。
    /// </summary>
    public event Action? MaxDurationReached;

    public bool IsRecording => this._mic.IsRecording;

    public SpeechInputService(SttSettings stt, AudioSettings audio, MoonshineRecognizer partialRecognizer, MoonshineRecognizer finalRecognizer, VadProcessor? partialVad, VadProcessor? finalVad)
    {
        this._mic = new MicrophoneCapture(stt.SampleRate, audio.InputDeviceIndex);
        this._partialRecognizer = partialRecognizer;
        this._finalRecognizer = finalRecognizer;
        this._partialVad = partialVad;
        this._finalVad = finalVad;
        this._sampleRate = stt.SampleRate;
        this._minChars = stt.MinUtteranceLengthChars;
        this._partialIntervalMs = stt.PartialIntervalMs;
        this._maxRecordingSeconds = Math.Max(5, stt.MaxRecordingSeconds);
        this._maxSamples = this._sampleRate * this._maxRecordingSeconds;
        this._partialMaxSamples = stt.PartialMaxSeconds > 0
            ? this._sampleRate * stt.PartialMaxSeconds
            : Int32.MaxValue;
        this._finalChunkSamples = this._sampleRate * Math.Max(4, stt.FinalChunkSeconds);
        this._finalOverlapSamples = this._sampleRate * Math.Max(0, stt.FinalChunkOverlapSeconds);
        this._saveDebugWav = stt.SaveDebugWav;
        this._partialMinSegmentSamples = (int)(this._sampleRate * Math.Max(0, stt.PartialMinSegmentSeconds));
    }

    public void StartPtt()
    {
        try
        {
            this._mic.Start();
        }
        catch (Exception ex)
        {
            Error?.Invoke($"録音開始に失敗: {ex.Message}");
            return;
        }

        this._lastPartial = String.Empty;
        this._partialCts = new CancellationTokenSource();
        this._partialLoop = Task.Run(() => this.RunPartialLoopAsync(this._partialCts.Token));
    }

    public async Task StopPttAndRecognizeAsync()
    {
        // 二重呼び出し (PreviewMouseUp + LostMouseCapture + MaxDurationReached 競合) を抑止
        if (Interlocked.CompareExchange(ref this._stopping, 1, 0) != 0)
        {
            return;
        }

        try
        {
            // ローカル変数にキャプチャしてから null 化することで競合時の NRE を回避
            var cts = Interlocked.Exchange(ref this._partialCts, null);
            var loop = Interlocked.Exchange(ref this._partialLoop, null);
            if (cts is not null)
            {
                cts.Cancel();
                if (loop is not null)
                {
                    try { await loop; } catch { }
                }
                cts.Dispose();
            }

            float[] samples;
            try
            {
                samples = this._mic.Stop();
            }
            catch (Exception ex)
            {
                Error?.Invoke($"録音停止に失敗: {ex.Message}");
                return;
            }

            if (samples.Length == 0)
            {
                FinalRecognized?.Invoke(String.Empty);
                return;
            }

            while (Volatile.Read(ref this._decoding) == 1)
            {
                await Task.Delay(20);
            }

            // 末尾 MaxRecordingSeconds 分のみ Moonshine に渡す (これ自体が安全ガード)
            var clipped = ClipTail(samples, this._maxSamples);

            if (this._saveDebugWav)
            {
                try { this.SaveWavFile(clipped); } catch (Exception wex) { Debug.WriteLine($"[stt] wav 保存失敗: {wex.Message}"); }
            }

            var sw = Stopwatch.StartNew();
            string text;
            try
            {
                text = await Task.Run(() => this.DecodeFinal(clipped));
            }
            catch (Exception ex)
            {
                Error?.Invoke($"認識に失敗: {ex.Message}");
                return;
            }

            sw.Stop();
            text = (text ?? String.Empty).Trim();
            this.LastFinalRaw = text;
            Debug.WriteLine($"[stt] FINAL samples={clipped.Length} ({clipped.Length / (double)this._sampleRate:F1}s) elapsed={sw.ElapsedMilliseconds}ms raw=\"{text}\" lastPartial=\"{this._lastPartial}\"");

            // 最終認識が空 / 短すぎなら、partial で見えていた最後の結果を fallback として採用
            if (text.Length < this._minChars)
            {
                var fallback = this._lastPartial.Trim();
                if (fallback.Length >= this._minChars)
                {
                    text = fallback;
                    Debug.WriteLine($"[stt] FINAL fallback to lastPartial=\"{text}\"");
                }
            }
            FinalRecognized?.Invoke(text.Length >= this._minChars ? text : String.Empty);
        }
        finally
        {
            Volatile.Write(ref this._stopping, 0);
        }
    }

    /// <summary>
    /// 最終認識: VAD があれば発話セグメントごとに認識して結合、無ければ従来のチャンク分割。
    /// </summary>
    private string DecodeFinal(float[] samples)
    {
        if (this._finalVad is not null)
        {
            var segments = this._finalVad.ExtractSpeechSegments(samples);
            this.LastFinalVadSegments = segments.Count;
            if (segments.Count == 0)
            {
                return String.Empty;
            }

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                // Moonshine の sweet spot を超える長いセグメントは末尾を切る
                if (seg.Length > this._finalChunkSamples)
                {
                    seg = ClipTail(seg, this._finalChunkSamples);
                }
                var text = (this._finalRecognizer.Decode(seg) ?? String.Empty).Trim();
                Debug.WriteLine($"[stt] FINAL seg#{i} samples={seg.Length} text=\"{text}\"");
                if (text.Length > 0)
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(text);
                }
            }
            return sb.ToString();
        }

        // VAD 無効時: チャンク分割 (旧ロジック)
        return this.DecodeChunked(samples);
    }

    /// <summary>VAD 無効時用のチャンク分割認識。無音 chunk はスキップ。</summary>
    private string DecodeChunked(float[] samples)
    {
        this.LastFinalVadSegments = -1;
        const float SilenceMaxAmp = 0.02f;

        if (samples.Length <= this._finalChunkSamples)
        {
            return this._finalRecognizer.Decode(samples);
        }

        var stride = Math.Max(1, this._finalChunkSamples - this._finalOverlapSamples);
        var sb = new System.Text.StringBuilder();
        int offset = 0;
        int chunkIndex = 0;
        while (offset < samples.Length)
        {
            int len = Math.Min(this._finalChunkSamples, samples.Length - offset);
            var chunk = new float[len];
            Array.Copy(samples, offset, chunk, 0, len);

            float maxAmp = 0;
            for (int i = 0; i < chunk.Length; i++)
            {
                var a = Math.Abs(chunk[i]);
                if (a > maxAmp) maxAmp = a;
            }

            if (maxAmp >= SilenceMaxAmp)
            {
                var part = this._finalRecognizer.Decode(chunk);
                Debug.WriteLine($"[stt] FINAL chunk #{chunkIndex} offset={offset} len={len} maxAmp={maxAmp:F3} text=\"{part}\"");
                if (!String.IsNullOrWhiteSpace(part))
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(part.Trim());
                }
            }
            else
            {
                Debug.WriteLine($"[stt] FINAL chunk #{chunkIndex} offset={offset} len={len} maxAmp={maxAmp:F3} SILENCE skip");
            }

            chunkIndex++;
            if (offset + len >= samples.Length) break;
            offset += stride;
        }
        return sb.ToString();
    }

    private async Task RunPartialLoopAsync(CancellationToken ct)
    {
        var notifiedMax = false;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(this._partialIntervalMs > 0 ? this._partialIntervalMs : 400, ct);

                // 最大録音時間に達していたら、まず UI に通知して自動停止を促す
                var rawLength = this._mic.GetSnapshotLength();
                if (!notifiedMax && rawLength >= this._maxSamples)
                {
                    notifiedMax = true;
                    MaxDurationReached?.Invoke();
                    // この後は通常通り partial を続けるが、UI 側で StopPttAndRecognizeAsync が呼ばれて止まる
                }

                if (this._partialIntervalMs <= 0)
                {
                    continue;
                }

                if (Interlocked.CompareExchange(ref this._decoding, 1, 0) != 0)
                {
                    continue;
                }

                try
                {
                    var snapshot = this._mic.GetSnapshot();
                    if (snapshot.Length == 0)
                    {
                        continue;
                    }
                    // partial は短いローリングウィンドウで負荷を一定化する
                    var clipped = ClipTail(snapshot, this._partialMaxSamples);
                    var psw = Stopwatch.StartNew();
                    string text;

                    if (this._partialVad is not null)
                    {
                        // VAD で発話セグメントだけを抽出し Moonshine に渡す (無音幻覚を防ぐ)
                        var segs = this._partialVad.ExtractSpeechSegments(clipped);
                        if (segs.Count == 0)
                        {
                            psw.Stop();
                            Debug.WriteLine($"[stt] partial samples={clipped.Length} ({clipped.Length / (double)this._sampleRate:F1}s) elapsed={psw.ElapsedMilliseconds}ms VAD=silence skip");
                            continue; // 無音 → partial 表示を更新しない (前回値保持)
                        }
                        // 直近の発話だけを採用 (頭欠けで短くなる古いセグメントを避ける)
                        var lastSeg = segs[segs.Count - 1];
                        if (lastSeg.Length < this._partialMinSegmentSamples)
                        {
                            psw.Stop();
                            Debug.WriteLine($"[stt] partial samples={clipped.Length} segLen={lastSeg.Length / (double)this._sampleRate:F2}s elapsed={psw.ElapsedMilliseconds}ms TOO_SHORT skip");
                            continue; // 短すぎるセグメントは幻覚源 → スキップ
                        }
                        text = (this._partialRecognizer.Decode(lastSeg) ?? String.Empty).Trim();
                    }
                    else
                    {
                        // VAD なし: 従来通り rolling window 全体を Moonshine へ
                        text = (this._partialRecognizer.Decode(clipped) ?? String.Empty).Trim();
                    }

                    psw.Stop();
                    Debug.WriteLine($"[stt] partial samples={clipped.Length} ({clipped.Length / (double)this._sampleRate:F1}s) elapsed={psw.ElapsedMilliseconds}ms text=\"{text}\"");
                    if (!ct.IsCancellationRequested && text.Length > 0)
                    {
                        // 前回と同じテキストなら UI 更新もイベント発火も省略 (ちらつき防止)
                        if (text != this._lastPartial)
                        {
                            this._lastPartial = text;
                            PartialRecognized?.Invoke(text);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Error?.Invoke($"partial 認識エラー: {ex.Message}");
                }
                finally
                {
                    Volatile.Write(ref this._decoding, 0);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static float[] ClipTail(float[] samples, int maxSamples)
    {
        if (samples.Length <= maxSamples) return samples;
        var dst = new float[maxSamples];
        Array.Copy(samples, samples.Length - maxSamples, dst, 0, maxSamples);
        return dst;
    }

    private void SaveWavFile(float[] samples)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "debug_recordings");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"rec-{DateTime.Now:yyyyMMdd-HHmmss}.wav");

        // 振幅統計をログ
        float max = 0, sumAbs = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            var a = Math.Abs(samples[i]);
            if (a > max) max = a;
            sumAbs += a;
        }
        var avg = samples.Length > 0 ? sumAbs / samples.Length : 0;
        Debug.WriteLine($"[stt] wav saved path={path} samples={samples.Length} maxAmp={max:F4} avgAbs={avg:F4}");

        using var fs = new FileStream(path, FileMode.Create);
        using var bw = new BinaryWriter(fs);
        int byteRate = this._sampleRate * 2;
        int dataLen = samples.Length * 2;
        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + dataLen);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)1);
        bw.Write(this._sampleRate);
        bw.Write(byteRate);
        bw.Write((short)2);
        bw.Write((short)16);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(dataLen);
        for (int i = 0; i < samples.Length; i++)
        {
            var v = Math.Clamp(samples[i], -1f, 1f);
            bw.Write((short)(v * 32767));
        }
    }

    public void Dispose()
    {
        this._partialCts?.Cancel();
        this._mic.Dispose();
        this._partialRecognizer.Dispose();
        this._finalRecognizer.Dispose();
        this._partialVad?.Dispose();
        this._finalVad?.Dispose();
    }
}
