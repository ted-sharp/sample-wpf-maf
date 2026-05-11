# wpf-maf-sample

WPF (.NET 10) + **Microsoft Agent Framework (MAF)** で、ローカル LLM (LM Studio / Gemma) に **GUI を操作させる**サンプル。チャットで「背景を赤にして」「ボタンを 2 つ追加して」と頼むと、LLM が Tool 呼び出しを介して実際にウィンドウの中身を書き換える。

slnx ソリューションに 2 つの WPF プロジェクトが入っており、片方は **マイク入力 (リアルタイム音声認識)** に置き換えた構成になっている。

| プロジェクト | 入力 | 説明 |
|---|---|---|
| `WpfMafSample`    | キーボード | テキストチャット → エージェント → GUI 操作 Tool |
| `WpfMafSampleStt` | マイク (PTT) | Sherpa-onnx (Moonshine ja) + Silero VAD で日本語音声 → エージェント |

## 必要なもの

- **Windows 10 / 11** (WPF × x64)
- **.NET 10 SDK** (`global.json` で `10.0.203` を指定)
- **LM Studio** とそこにロードされた Tool Use 対応 LLM（既定: `google/gemma-4-e2b`）
  - OpenAI 互換サーバを `http://localhost:1234/v1` で起動しておく
- **Task** ([taskfile.dev](https://taskfile.dev/)) — `scoop install task` または `winget install Task.Task`
- 音声版を使う場合のみ: マイクと、`task download-models` で取得する STT モデル（合計 ~135MB）

## クイックスタート

```sh
# 1. 初回セットアップ (STT モデル取得 + 全プロジェクトビルド)
task init

# 2. LM Studio を起動し、Gemma 系の Tool Use 対応モデルをロードしておく
#    (モデル ID は appsettings.json の Llm.Model と完全一致させる)

# 3a. テキスト版を実行
task run

# 3b. 音声版を実行
task run-stt
```

主要なタスク：

| コマンド | 役割 |
|---|---|
| `task init`                          | モデル取得 + 全ビルド (`download-models` → `build`) |
| `task build`                         | `dotnet build WpfMafSample.slnx` |
| `task run`                           | テキスト版 (`WpfMafSample`) を実行 |
| `task run-stt`                       | 音声版 (`WpfMafSampleStt`) を実行 |
| `task download-models`               | Moonshine base ja + Silero VAD を `models/` に取得 |
| `task download-moonshine VARIANT=tiny` | 軽量モデル (~70MB) に切り替えてダウンロード |
| `task clean-models`                  | `models/` を全削除 |

`task --list` で全タスクが表示される。

## 使い方

### テキスト版

左ペインのチャット欄に文章を入力して **Enter** か「送信」ボタン（`Shift+Enter` で改行）。例:

- 「背景色を青にして」
- 「(120, 80) にラベル `はい` を、(200, 80) に `いいえ` ボタンを置いて」
- 「いま画面に何があるか教えて」 → `get_canvas_state` Tool を呼んで JSON で答える
- 「全部消して」

右ペインのキャンバスが LLM の Tool 呼び出しに応じて書き換わる。

### 音声版 (Push-To-Talk)

左ペイン下部の **マイクボタンを押しっぱなしにして話す → 離す** と、

1. 押している間: 末尾数秒を Moonshine に流して partial 認識結果が薄字で出る
2. 離した瞬間: 録音全体を Silero VAD でセグメント分割 → Moonshine で最終認識
3. 確定テキストを MAF エージェントに送信、応答と Tool 呼び出しが反映される

最大 25 秒（`Stt.MaxRecordingSeconds`）で自動確定する。LM Studio に接続できなくても STT モデルが揃っていれば認識結果は表示される。

## 設定

各プロジェクトの `appsettings.json` を編集する。実行ファイル隣に `appsettings.local.json` を置けば上書きできる（`.gitignore` 対象）。

```jsonc
{
  "Llm": {
    "Endpoint": "http://localhost:1234/v1",
    "ApiKey": "",                       // LAN ローカル前提なので空でも可
    "Model": "google/gemma-4-e2b",      // LM Studio のロード済 ID と完全一致
    "Temperature": 0.2,
    "TimeoutSeconds": 120
  },
  "Agent": {
    "Name": "GuiAgent",
    "Instructions": "あなたは WPF アプリの GUI を操作するアシスタントです。..."
  }
}
```

音声版 (`WpfMafSampleStt/appsettings.json`) には追加で `Stt` / `Audio` セクションがある。VAD オフ (`UseVad: false`)、デバッグ WAV 保存 (`SaveDebugWav: true` で `<bin>/debug_recordings/` に出力)、partial の更新間隔 (`PartialIntervalMs`) などをチューニングできる。詳細は `SPEC.md` 第 E 章。

## プロジェクト構成

```
wpf-maf-sample/
├── README.md
├── SPEC.md                              # 詳細仕様 (このサンプルの設計根拠)
├── CLAUDE.md                            # Claude Code 向けガイド
├── Taskfile.yml                         # task コマンド定義
├── WpfMafSample.slnx                    # slnx 形式ソリューション
├── global.json                          # .NET 10 SDK バージョン固定
├── src/
│   ├── WpfMafSample/                    # テキスト版
│   │   ├── MainWindow.xaml(.cs)
│   │   ├── AgentFactory.cs              # IChatClient (LM Studio) 生成
│   │   ├── Tools/GuiTools.cs            # LLM から呼ぶ GUI 操作 Tool 群
│   │   └── appsettings.json
│   └── WpfMafSampleStt/                 # 音声版 (上記 + Speech/)
│       ├── Speech/
│       │   ├── MicrophoneCapture.cs     # NAudio で 16kHz mono PCM 録音
│       │   ├── MoonshineRecognizer.cs   # Sherpa-onnx OfflineRecognizer ラッパ
│       │   ├── VadProcessor.cs          # Silero VAD セグメント抽出
│       │   └── SpeechInputService.cs    # PTT + 擬似ストリーミング統括
│       └── ...
└── models/                              # task で取得 (gitignore)
    ├── sherpa-onnx-moonshine-base-ja-quantized-2026-02-27/
    └── silero_vad.onnx
```

## アーキテクチャ概要

- **MVVM は採用しない**。`MainWindow.xaml.cs` から UI を直接触る純粋なコードビハインド構成
- LLM 接続は `OpenAIClient` の `Endpoint` を LM Studio に上書きして `IChatClient.AsIChatClient()` 経由で `ChatClientAgent` を作る
- Tool は `GuiTools` のメソッドを `AIFunctionFactory.Create(...)` で `AITool` 化して `ChatOptions.Tools` に渡す
- Tool 実行はすべて `Dispatcher` 経由で UI スレッドへマーシャル（`GuiTools.Invoke<T>`）
- 音声版は **PTT + 末尾切り出し partial + VAD セグメント final** という構成で Moonshine (非ストリーミング) を擬似的にストリーミング化している

設計判断の根拠と細かい注意点（partial / final で recognizer を分離する理由、多重停止呼び出しのガード方法など）は `SPEC.md` と `CLAUDE.md` に記載。

## ライセンス

MIT License. 詳細は [LICENSE](LICENSE)。

サードパーティ:
- [Microsoft Agent Framework](https://github.com/microsoft/agent-framework) — MIT
- [Sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) — Apache-2.0
- Moonshine (Useful Sensors) / Silero VAD — それぞれの配布元のライセンスに従う
- [NAudio](https://github.com/naudio/NAudio) — MIT
