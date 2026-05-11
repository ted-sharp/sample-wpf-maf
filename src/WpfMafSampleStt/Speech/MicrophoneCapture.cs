using System;
using System.Collections.Generic;
using NAudio.Wave;

namespace WpfMafSampleStt.Speech;

/// <summary>
/// マイクから 16-bit PCM mono を取得し、float (-1.0~1.0) のバッファに蓄積する単発キャプチャ。
/// Start() → Stop() の間に録音されたサンプルを GetSamples() で取り出す。
/// </summary>
internal sealed class MicrophoneCapture : IDisposable
{
    private readonly int _sampleRate;
    private readonly int _deviceNumber;
    private WaveInEvent? _wave;
    private readonly List<float> _samples = new();
    private readonly object _gate = new();

    public bool IsRecording { get; private set; }

    public MicrophoneCapture(int sampleRate, int deviceNumber)
    {
        this._sampleRate = sampleRate;
        this._deviceNumber = deviceNumber;
    }

    public void Start()
    {
        if (this.IsRecording)
        {
            return;
        }

        lock (this._gate)
        {
            this._samples.Clear();
        }

        this._wave = new WaveInEvent
        {
            DeviceNumber = this._deviceNumber,
            WaveFormat = new WaveFormat(this._sampleRate, 16, 1),
            BufferMilliseconds = 50
        };
        this._wave.DataAvailable += this.OnDataAvailable;
        this._wave.RecordingStopped += this.OnRecordingStopped;
        this._wave.StartRecording();
        this.IsRecording = true;
    }

    public float[] Stop()
    {
        if (!this.IsRecording || this._wave is null)
        {
            return Array.Empty<float>();
        }

        this._wave.StopRecording();
        this._wave.DataAvailable -= this.OnDataAvailable;
        this._wave.RecordingStopped -= this.OnRecordingStopped;
        this._wave.Dispose();
        this._wave = null;
        this.IsRecording = false;

        lock (this._gate)
        {
            return this._samples.ToArray();
        }
    }

    /// <summary>
    /// 録音を継続したまま、現在までに蓄積されたサンプルのスナップショットを返す。
    /// partial 認識のために使う。
    /// </summary>
    public float[] GetSnapshot()
    {
        lock (this._gate)
        {
            return this._samples.ToArray();
        }
    }

    /// <summary>サンプル配列をコピーせず、長さだけを取得する。</summary>
    public int GetSnapshotLength()
    {
        lock (this._gate)
        {
            return this._samples.Count;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var bytes = e.Buffer;
        var count = e.BytesRecorded;
        lock (this._gate)
        {
            for (int i = 0; i + 1 < count; i += 2)
            {
                short s = (short)(bytes[i] | (bytes[i + 1] << 8));
                this._samples.Add(s / 32768f);
            }
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        // 失敗時のクリーンアップ用。例外は表面化させない（呼び出し元の Stop で吸収）
    }

    public void Dispose()
    {
        if (this._wave is not null)
        {
            try { this._wave.StopRecording(); } catch { }
            this._wave.Dispose();
            this._wave = null;
        }
        this.IsRecording = false;
    }

    public static int DefaultDeviceCount => WaveInEvent.DeviceCount;
}
