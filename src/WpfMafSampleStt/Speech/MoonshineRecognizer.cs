using System;
using System.IO;
using SherpaOnnx;

namespace WpfMafSampleStt.Speech;

/// <summary>
/// Sherpa-onnx の OfflineRecognizer を Moonshine (merged decoder) 構成でラップ。
/// </summary>
internal sealed class MoonshineRecognizer : IDisposable
{
    private readonly OfflineRecognizer _recognizer;
    private readonly int _sampleRate;

    public MoonshineRecognizer(SttSettings settings)
    {
        var dir = ResolvePath(settings.ModelDir);
        var encoderPath = Path.Combine(dir, settings.Encoder);
        var decoderPath = Path.Combine(dir, settings.Decoder);
        var tokensPath = Path.Combine(dir, settings.Tokens);

        if (!File.Exists(encoderPath)) throw new FileNotFoundException(encoderPath);
        if (!File.Exists(decoderPath)) throw new FileNotFoundException(decoderPath);
        if (!File.Exists(tokensPath)) throw new FileNotFoundException(tokensPath);

        var config = new OfflineRecognizerConfig();
        config.ModelConfig.Moonshine.Encoder = encoderPath;
        config.ModelConfig.Moonshine.MergedDecoder = decoderPath;
        config.ModelConfig.Tokens = tokensPath;
        config.ModelConfig.NumThreads = settings.NumThreads;
        config.ModelConfig.Provider = settings.Provider;
        config.ModelConfig.Debug = 0;

        this._recognizer = new OfflineRecognizer(config);
        this._sampleRate = settings.SampleRate;
    }

    public string Decode(float[] samples)
    {
        if (samples is null || samples.Length == 0)
        {
            return String.Empty;
        }
        using var stream = this._recognizer.CreateStream();
        stream.AcceptWaveform(this._sampleRate, samples);
        this._recognizer.Decode(stream);
        return stream.Result.Text ?? String.Empty;
    }

    private static string ResolvePath(string relativeOrAbsolute)
    {
        if (Path.IsPathRooted(relativeOrAbsolute))
        {
            return relativeOrAbsolute;
        }
        // 実行ディレクトリ → リポジトリルート (実行ディレクトリの祖先) の順で探す
        var baseDir = AppContext.BaseDirectory;
        var candidate1 = Path.GetFullPath(Path.Combine(baseDir, relativeOrAbsolute));
        if (Directory.Exists(candidate1)) return candidate1;

        // 開発時: bin/Debug/net10.0-windows/ から見て4階層上が wpf-maf-sample/
        var dev = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", relativeOrAbsolute));
        if (Directory.Exists(dev)) return dev;

        return candidate1;
    }

    public void Dispose()
    {
        this._recognizer.Dispose();
    }
}
