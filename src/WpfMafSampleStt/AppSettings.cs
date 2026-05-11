namespace WpfMafSampleStt;

public sealed class AppSettings
{
    public LlmSettings Llm { get; set; } = new();
    public AgentSettings Agent { get; set; } = new();
    public SttSettings Stt { get; set; } = new();
    public AudioSettings Audio { get; set; } = new();
}

public sealed class LlmSettings
{
    public string Endpoint { get; set; } = "http://localhost:1234/v1";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "google/gemma-4-e2b";
    public float Temperature { get; set; } = 0.2f;
    public int TimeoutSeconds { get; set; } = 120;
}

public sealed class AgentSettings
{
    public string Name { get; set; } = "GuiAgent";
    public string Instructions { get; set; } = "";
}

public sealed class SttSettings
{
    public string ModelDir { get; set; } = "models/sherpa-onnx-moonshine-base-ja-quantized-2026-02-27";
    public string Tokens { get; set; } = "tokens.txt";
    public string Encoder { get; set; } = "encoder_model.ort";
    public string Decoder { get; set; } = "decoder_model_merged.ort";
    public int SampleRate { get; set; } = 16000;
    public int NumThreads { get; set; } = 1;
    public string Provider { get; set; } = "cpu";
    public int SilenceTimeoutMs { get; set; } = 800;
    public int MinUtteranceLengthChars { get; set; } = 2;

    /// <summary>
    /// 録音中の partial 認識を行う間隔 (ms)。前回認識が終わるまで次は投げないので、
    /// 実機の認識所要時間より長めにすると安定する。0 にすると partial を無効化。
    /// </summary>
    public int PartialIntervalMs { get; set; } = 400;

    /// <summary>
    /// partial 認識で Moonshine に投入する音声長の上限 (秒)。
    /// 累積バッファ全体ではなく末尾のこの秒数のみを使うことで、長時間録音でも partial の負荷が一定になる。
    /// 0 にすると制限なし (累積バッファ全体)。
    /// </summary>
    public int PartialMaxSeconds { get; set; } = 6;

    /// <summary>
    /// 録音時間の上限 (秒)。これを超えたら自動で確定処理に入る。
    /// 最終認識は FinalChunkSeconds 単位のチャンク分割で行うので、ここは比較的長くてよい。
    /// </summary>
    public int MaxRecordingSeconds { get; set; } = 25;

    /// <summary>
    /// 最終認識を分割する際の 1 チャンクの長さ (秒)。Moonshine の sweet spot に合わせて 12 秒推奨。
    /// 録音長がこれ以下ならチャンク分割せず 1 回で認識。
    /// </summary>
    public int FinalChunkSeconds { get; set; } = 12;

    /// <summary>
    /// 最終認識のチャンク同士のオーバーラップ (秒)。境界での単語切り落としを防ぐ。
    /// </summary>
    public int FinalChunkOverlapSeconds { get; set; } = 2;

    /// <summary>
    /// 録音した PCM を WAV としてアプリ実行ディレクトリの debug_recordings/ に保存する。
    /// Moonshine が何を聞いていたのかを確認するためのデバッグ機能。
    /// </summary>
    public bool SaveDebugWav { get; set; } = false;

    /// <summary>
    /// Silero VAD で無音区間を除去してから Moonshine に投入するかどうか。
    /// 無音時の幻覚を防ぎ、計算量も削減できる。
    /// </summary>
    public bool UseVad { get; set; } = true;

    /// <summary>VAD モデル (silero_vad.onnx) のパス。ModelDir からの相対 or 絶対。</summary>
    public string VadModel { get; set; } = "models/silero_vad.onnx";

    /// <summary>VAD の発話判定しきい値 (0.0〜1.0)。低いほど発話とみなしやすい。</summary>
    public float VadThreshold { get; set; } = 0.5f;

    /// <summary>VAD: この秒数以上の無音で発話セグメントを区切る。</summary>
    public float VadMinSilenceDuration { get; set; } = 0.4f;

    /// <summary>VAD: この秒数未満の発話は捨てる。</summary>
    public float VadMinSpeechDuration { get; set; } = 0.25f;

    /// <summary>
    /// VAD が検出したセグメントの開始位置から、この秒数だけ「前」を含めて切り出す。
    /// Silero VAD は判定確定に少し時間がかかるため、これで頭欠けを防ぐ。
    /// </summary>
    public float VadPreRollSeconds { get; set; } = 0.3f;

    /// <summary>
    /// VAD が検出したセグメントの終了位置から、この秒数だけ「後」を含めて切り出す。
    /// 語尾切れを防ぐ。
    /// </summary>
    public float VadPostRollSeconds { get; set; } = 0.2f;

    /// <summary>
    /// partial 認識でこの秒数未満のセグメントは捨てる。
    /// 短い断片を Moonshine に投げると幻覚 ("あっ" "うん" 等) が出るのを防ぐ。
    /// </summary>
    public float PartialMinSegmentSeconds { get; set; } = 0.6f;
}

public sealed class AudioSettings
{
    public int InputDeviceIndex { get; set; } = -1;
    public string Mode { get; set; } = "Ptt";
}
