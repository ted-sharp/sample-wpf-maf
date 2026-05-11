# WPF × Microsoft Agent Framework サンプル 仕様書

## 1. 概要

WPF アプリケーション上で **Microsoft Agent Framework (MAF)** を用いて LLM エージェントを動作させ、
チャット入力を通じて **GUI 操作を Tool 経由で実行できる** サンプルアプリを作成する。

LLM のバックエンドには **LM Studio**（ローカル）の OpenAI 互換エンドポイントを利用し、
モデルは Google 製の **Gemma**（Tool Use 対応版）を使用する。

---

## 2. ゴール / 非ゴール

### ゴール

- WPF (.NET 8 以降, `net8.0-windows`) で動作する単一ウィンドウのデスクトップアプリ
- ユーザーがチャット欄にメッセージを送ると、LLM が会話または Tool 呼び出しで応答する
- LLM が **GUI の状態を取得・変更できる**（背景色変更、テキスト表示、ボタン追加 など）
- LM Studio (OpenAI 互換) を `appsettings.json` で切り替え可能にする
- **コードビハインド形式**で実装する（MVVM ライブラリや `ViewModel` クラスを使わない）

### 非ゴール

- 認証・認可（LAN 内ローカル前提のためなし）
- 永続化（履歴は in-memory のみ。再起動でリセットして良い）
- マルチエージェント / オーケストレーション
- インストーラ / 配布パッケージ

---

## 3. 技術スタック

| 区分 | 採用技術 |
|---|---|
| ランタイム | .NET 10 (`net10.0-windows`) |
| ソリューション | **slnx 形式** (`*.slnx` / XML ベースの新ソリューションファイル) |
| UI | WPF (コードビハインド) |
| エージェント | Microsoft Agent Framework (`Microsoft.Agents.AI` 系) |
| LLM クライアント | `Microsoft.Extensions.AI` + OpenAI 互換クライアント |
| LLM サーバ | LM Studio (ローカル、OpenAI 互換) |
| モデル | **`google/gemma-4-e2b`**（Google 製, Tool Use 対応） |
| 設定 | `Microsoft.Extensions.Configuration.Json` (`appsettings.json`) |
| ログ | `Microsoft.Extensions.Logging`（コンソール / Debug 出力で十分） |

> **メモ**: Microsoft Agent Framework は `IChatClient` を介して任意の OpenAI 互換エンドポイントへ接続できる。LM Studio の `http://<host>:1234/v1` をそのまま指定する。

---

## 4. アーキテクチャ

```
+----------------------------------------------+
|                MainWindow.xaml               |
|   - チャット履歴表示                          |
|   - 入力欄 / 送信ボタン                       |
|   - 操作対象キャンバス (LLM が操作する場所)   |
+--------------------+-------------------------+
                     |  (コードビハインド)
                     v
        +--------------------------+
        |   MainWindow.xaml.cs     |
        |  - AIAgent を保持        |
        |  - UI 操作 Tool を登録   |
        |  - 送信 → Agent 呼び出し |
        +-----------+--------------+
                    |
                    v
        +--------------------------+
        |   Microsoft Agent FW     |
        |  AIAgent / AgentThread   |
        +-----------+--------------+
                    |
                    v
        +--------------------------+
        |  IChatClient (OpenAI)    |
        |    → LM Studio (Gemma)   |
        +--------------------------+
```

- **MVVM は採用しない**。`MainWindow.xaml.cs` から UI 要素を直接触る。
- Tool は `MainWindow` のフィールドにラムダ/メソッドとして定義し、UI スレッドへのマーシャリングは `Dispatcher` を使って行う。
- LLM 呼び出しは非同期 (`async`) で、UI スレッドをブロックしない。

---

## 5. 機能要件

### 5.1 チャット UI
- 上部: 会話履歴（ユーザー発言 / アシスタント発言を時系列表示）
- 下部: 入力 `TextBox` ＋ 「送信」`Button`
- `Enter` で送信、`Shift+Enter` で改行
- 送信中は入力欄をディセーブル化、ステータスバーに「考え中…」表示

### 5.2 LLM が呼べる Tool（最低限の初期セット）

GUI 側を「うまく操作できる」ことが目的なので、見て楽しい / 効果が分かりやすい Tool を用意する。

| Tool 名 | 引数 | 概要 |
|---|---|---|
| `set_window_title` | `string title` | ウィンドウタイトルを書き換える |
| `set_background_color` | `string colorName` | 操作キャンバスの背景色を変更（"red", "#FF8800" など） |
| `add_text_block` | `string text`, `double x`, `double y` | キャンバスにテキストを配置 |
| `add_button` | `string label`, `double x`, `double y` | キャンバスにボタンを追加（クリックでログ出力） |
| `clear_canvas` | (なし) | キャンバス内の要素を全削除 |
| `get_canvas_state` | (なし) | 現在キャンバスにある要素の一覧を JSON で返す |
| `show_message_box` | `string message` | `MessageBox.Show` を呼ぶ |

> Tool は MAF の `AIFunctionFactory.Create(...)` 相当で `MainWindow` のメソッドから自動生成し、`ChatClientAgent` の `Tools` に渡す。

### 5.3 エラー処理
- LM Studio が落ちている / 接続不能の場合、チャット欄にエラーメッセージを赤字表示
- Tool 呼び出し中の例外は catch し、LLM へ「失敗しました」と返却（処理継続）

---

## 6. 設定 (`appsettings.json`)

`appsettings.json` を実行ファイルと同じディレクトリに配置し、起動時に読み込む。

```json
{
  "Llm": {
    "Endpoint": "http://localhost:1234/v1",
    "ApiKey": "",
    "Model": "google/gemma-4-e2b",
    "Temperature": 0.2,
    "TimeoutSeconds": 120
  },
  "Agent": {
    "Name": "GuiAgent",
    "Instructions": "あなたは WPF アプリの GUI を操作するアシスタントです。ユーザーの指示に従い、必要に応じて提供された Tool を呼び出して GUI を変更してください。日本語で応答します。"
  }
}
```

- `ApiKey` は LAN 内ローカル前提のため空でも可（OpenAI クライアントがダミー値を要求するため、空のときはコード側で `"lm-studio"` などのダミーを入れる）。
- 既定モデルは **`google/gemma-4-e2b`**。LM Studio 側でロードしている ID と完全一致させる必要がある（大文字小文字・スラッシュ位置に注意）。
- 上記値はあくまで既定例。ユーザーが自由に変更できる。

`appsettings.local.json` (gitignore 対象) で上書き可能にしておくと便利だが、初版では `appsettings.json` のみで良い。

---

## 7. プロジェクト構成（予定）

```
wpf-maf-sample/
├── SPEC.md
├── LICENSE
├── .editorconfig
├── .gitignore
├── .gitattributes
├── WpfMafSample.slnx            # slnx 形式 (XML ベースのソリューション)
└── src/
    └── WpfMafSample/
        ├── WpfMafSample.csproj  # TargetFramework: net10.0-windows
        ├── App.xaml
        ├── App.xaml.cs          # 起動時に appsettings.json 読込
        ├── MainWindow.xaml      # チャット + 操作キャンバス
        ├── MainWindow.xaml.cs   # コードビハインドで全部やる
        ├── AppSettings.cs       # 設定 POCO
        ├── AgentFactory.cs      # IChatClient + AIAgent 構築
        ├── Tools/
        │   └── GuiTools.cs      # GUI 操作 Tool 群（MainWindow から呼ばれる）
        └── appsettings.json
```

### slnx について

`*.slnx` は Visual Studio / dotnet CLI が対応する **XML ベースの新ソリューションファイル**形式。`*.sln` の冗長な GUID 列を排し、人間にも読み書きしやすい。最低限の例：

```xml
<Solution>
  <Project Path="src/WpfMafSample/WpfMafSample.csproj" />
</Solution>
```

> `dotnet new sln --format slnx` または `dotnet sln migrate` で生成可能。

> `GuiTools.cs` は `MainWindow` の参照を受け取り、`Dispatcher.Invoke` で UI 操作する。
> Tool の関数自体は `AIFunctionFactory.Create` で MAF に登録する。

---

## 8. 開発ステップ（実装順）

1. **csproj / slnx 作成**：`net10.0-windows`, `UseWPF=true`, slnx 形式のソリューション、必要な NuGet 追加
   - `Microsoft.Agents.AI`（または現行の MAF パッケージ）
   - `Microsoft.Extensions.AI.OpenAI`
   - `Microsoft.Extensions.Configuration.Json`
   - `Microsoft.Extensions.Configuration.Binder`
2. **`appsettings.json` 読み込み**：`App.xaml.cs` で `IConfiguration` → `AppSettings`
3. **`AgentFactory`**：`OpenAIClient` (BaseAddress 上書き) → `IChatClient` → `ChatClientAgent`
4. **MainWindow の UI**：左ペインにチャット、右ペインに操作 `Canvas`
5. **Tool 実装**：`GuiTools` を作り、`AIFunctionFactory.Create` で `ChatClientAgent` に登録
6. **送信フロー**：ユーザー入力 → `AgentThread.RunAsync` → 応答を履歴に追加 → Tool 呼び出しは MAF が自動処理
7. **動作確認**：LM Studio に Gemma をロードして起動。チャットから「背景を赤にして」「ボタンを追加して」等を試す
8. **エラー処理 / 微調整**：接続エラー、Tool 例外、改行/Enter 制御 など

---

## 9. オープン項目（実装中に決める）

- MAF の現行 NuGet パッケージ名 / バージョン（プレビュー版の名称が変動するため、`csproj` 作成時点で最新を確認）
- Gemma が Tool Use を安定して返すかは LM Studio 側のテンプレート設定次第。期待通り動かない場合は `Instructions` を強化する
- 履歴の最大保持件数（初版では制限なしで OK）

---

# 第二プロジェクト: `WpfMafSampleStt`（リアルタイム音声入力サンプル）

## A. 概要

第一プロジェクト (`WpfMafSample`) と **同じ slnx ソリューションに追加**する、リアルタイム STT 対応版。
キーボード入力の代わりに**マイクから日本語で話したコマンド**を Microsoft Agent Framework のエージェントへ送信し、
Tool 呼び出しで GUI を操作する。

### ゴール
- マイク入力をリアルタイムにストリーミング書き起こし（ストリーミング ASR）
- 文末／無音検出（VAD or エンドポイント検出）で 1 つの発話を確定 → エージェントへ送信
- 認識中の途中結果（partial）と確定結果（final）を UI に表示
- LLM / Tool / GUI 部分は第一プロジェクトと同等

### 非ゴール
- 多言語切替（日本語固定）
- TTS（応答の読み上げ）
- 任意のキーワードでウェイクワード起動（常時マイク ON or PTT ボタン方式）

---

## B. 技術スタック（追加分）

| 区分 | 採用技術 |
|---|---|
| STT エンジン | **Sherpa-onnx**（`org.k2fsa.sherpa.onnx` NuGet パッケージ） |
| 音響モデル | **Moonshine 日本語版（base, quantized, 2026-02-27 リリース）** |
| マイクキャプチャ | NAudio (`NAudio.Wasapi` / `WaveInEvent`) で 16kHz mono / Float32 取得 |
| タスクランナー | **[Task (taskfile.dev)](https://taskfile.dev/)** — `task init` でモデル取得 |
| アーカイブ展開 | `Taskfile.yml` 内で `curl` + `tar -xjf`（Windows 10/11 に標準同梱の bsdtar が `.tar.bz2` を扱える） |

### モデル詳細（2026-05 時点の最新版）

| モデル | サイズ | ダウンロード URL |
|---|---|---|
| `sherpa-onnx-moonshine-base-ja-quantized-2026-02-27`（**既定**） | encoder 30M + decoder 105M | <https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-moonshine-base-ja-quantized-2026-02-27.tar.bz2> |
| `sherpa-onnx-moonshine-tiny-ja-quantized-2026-02-27`（軽量代替） | encoder 13M + decoder 56M | <https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-moonshine-tiny-ja-quantized-2026-02-27.tar.bz2> |

展開後のファイル構成（両モデル共通）:

```
sherpa-onnx-moonshine-base-ja-quantized-2026-02-27/
├── encoder_model.ort
├── decoder_model_merged.ort
├── tokens.txt
├── LICENSE
└── test_wavs/
```

> **メモ**: `.ort` は ONNX Runtime Optimized 形式。Sherpa-onnx の `OfflineMoonshineModelConfig` がそのままこの 2 ファイル構成を受け付ける。

> **メモ**: Sherpa-onnx の C# API は `OnlineRecognizer` (ストリーミング) を提供する。Moonshine は本来 *non-streaming* 系だが、Sherpa-onnx は **VAD + 短時間バッファのチャンク認識** によって擬似ストリーミングを実現できる。
> 実装時に `OnlineRecognizer` 系でモデルが直接動かない場合は、`VoiceActivityDetector` + `OfflineRecognizer` の組み合わせに切り替える（チャンクごとに非ストリーミング推論）。

---

## C. アーキテクチャ

```
+--------------------------------------------------------+
|                 MainWindow.xaml                        |
|  - PTT/常時ON 切替ボタン                                |
|  - 認識中テキスト (partial) / 確定済み履歴             |
|  - 操作キャンバス (LLM が変更)                          |
+----------------------+---------------------------------+
                       |
                       v
        +--------------------------------+
        |  MainWindow.xaml.cs            |
        |  - SpeechInputService 起動     |
        |  - 確定発話 → AIAgent          |
        |  - GUI Tool 登録 (共通)        |
        +---------------+----------------+
                        |
        +---------------+----------------+
        |                                |
        v                                v
+-----------------+         +---------------------------+
| NAudio          |  PCM    | Sherpa-onnx Recognizer    |
| (WaveInEvent)   +-------->+  + VAD                    |
+-----------------+         |  → partial/final テキスト |
                            +-------------+-------------+
                                          |
                              (final 確定時)
                                          v
                            +---------------------------+
                            |  Microsoft Agent FW       |
                            |  ChatClientAgent          |
                            |  → LM Studio (Gemma)      |
                            +---------------------------+
```

- 認識の状態管理は `SpeechInputService` に閉じ込め、UI 側からは
  - イベント `PartialRecognized(string)` / `FinalRecognized(string)`
  - メソッド `Start()` / `Stop()`
  だけを露出する
- UI スレッドへのマーシャルは引き続き `Dispatcher.Invoke`

---

## D. 機能要件

### D.1 入力モード
- **PTT (Push-To-Talk)**: ボタン押下中だけマイクを開く（初版の既定）
- **常時 ON**: トグルでマイク開放、VAD で発話を切り出す
- どちらでも「無音 X ms 続いたら確定」で 1 発話を確定

### D.2 UI
- 左ペイン上部に `🎤` トグル / PTT ボタン、状態テキスト (`待機中` / `聴取中` / `処理中`)
- 中段に **partial 表示** (薄字、随時更新)
- 下段にチャット履歴（第一プロジェクトと同じ吹き出し表示）
- 右ペインは第一プロジェクトと同じ操作キャンバス

### D.3 エージェント連携
- `final` 確定時にテキストを `agent.RunAsync(text, session)` へ送る
- 認識結果が空・1 文字以下なら破棄
- 応答中の **重複認識を抑止**（応答返却までマイクをミュートする、または認識結果をキューに溜める）

### D.4 エラー処理
- マイクデバイス無し → ステータスバーに赤字表示、PTT 無効化
- モデル未配置 → 起動時に検出して「`task init` を実行してください」と表示し、エージェント部分のみで起動

---

## E. 設定 (`appsettings.json` 追加項目)

```jsonc
{
  "Llm":   { /* 第一プロジェクトと同じ */ },
  "Agent": { /* 第一プロジェクトと同じ */ },
  "Stt": {
    "ModelDir": "models/sherpa-onnx-moonshine-base-ja-quantized-2026-02-27",
    "Tokens":   "tokens.txt",
    "Encoder":  "encoder_model.ort",
    "Decoder":  "decoder_model_merged.ort",
    "SampleRate": 16000,
    "NumThreads": 1,
    "Provider": "cpu",
    "SilenceTimeoutMs": 800,
    "MinUtteranceLengthChars": 2
  },
  "Audio": {
    "InputDeviceIndex": -1,
    "Mode": "Ptt"
  }
}
```

> モデルの具体ファイル名は Sherpa-onnx の Moonshine リリース構成に合わせて Taskfile 側で最終決定する。
> `InputDeviceIndex: -1` は既定デバイス。

---

## F. プロジェクト構成

```
wpf-maf-sample/
├── WpfMafSample.slnx              # 既存（両プロジェクトを保持）
├── Taskfile.yml                   # ★追加: task init などのタスク定義
├── src/
│   ├── WpfMafSample/              # 既存
│   └── WpfMafSampleStt/           # ★追加
│       ├── WpfMafSampleStt.csproj
│       ├── App.xaml / App.xaml.cs
│       ├── MainWindow.xaml
│       ├── MainWindow.xaml.cs
│       ├── AppSettings.cs         # Llm/Agent/Stt/Audio
│       ├── AgentFactory.cs        # ← 第一プロジェクトと同等（コピー or 共有化）
│       ├── Speech/
│       │   ├── SpeechInputService.cs   # NAudio + Sherpa-onnx を束ねる
│       │   ├── MicrophoneCapture.cs    # WaveInEvent ラッパ
│       │   └── MoonshineRecognizer.cs  # Sherpa-onnx Recognizer ラッパ
│       ├── Tools/GuiTools.cs      # 第一プロジェクトと同等
│       └── appsettings.json
└── models/                        # ★追加: task init が展開する (gitignore 対象)
    └── sherpa-onnx-moonshine-base-ja-quantized-2026-02-27/
        ├── encoder_model.ort
        ├── decoder_model_merged.ort
        ├── tokens.txt
        ├── LICENSE
        └── test_wavs/
```

> **共有化について**: 初版では `AgentFactory.cs` / `GuiTools.cs` は両プロジェクトに**コピー配置**で進める（依存を増やさず単独サンプル性を保つ）。後で共通ライブラリ化したくなったら `src/Common/` に切り出す。

---

## G. `Taskfile.yml`（モデル取得）

[Task (taskfile.dev)](https://taskfile.dev/) を採用する。Go 製の単一バイナリで `winget install Task.Task` または `scoop install task` で導入可。

最低限定義するタスク：

| タスク | 役割 |
|---|---|
| `task init`         | `task fetch-models` → `task build` を一括実行 |
| `task fetch-models` | Moonshine 日本語モデルをダウンロード & `models/` に展開（既に存在すればスキップ） |
| `task clean-models` | `models/` 配下を削除 |
| `task build`        | `dotnet build WpfMafSample.slnx` |
| `task run-stt`      | `dotnet run --project src/WpfMafSampleStt` |
| `task run`          | `dotnet run --project src/WpfMafSample` |

`fetch-models` の実装方針：
1. アーカイブを `models/` 直下に取得
   - URL: <https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-moonshine-base-ja-quantized-2026-02-27.tar.bz2>
2. `curl -fL --create-dirs -o models/<archive>` で取得
3. `tar -xjf models/<archive> -C models/`（Windows 10/11 標準の bsdtar が `.tar.bz2` を扱える）で展開
4. 展開済みフォルダ（`models/sherpa-onnx-moonshine-base-ja-quantized-2026-02-27/encoder_model.ort` 等）の存在をチェックして既にあればスキップ（冪等）
5. 展開後、ダウンロードしたアーカイブは削除しても良い（任意）

軽量版に切り替えたい場合は `task fetch-models VARIANT=tiny` のように引数化しておくと便利。

---

## H. 開発ステップ（第二プロジェクト）

1. **Taskfile.yml 作成**：`task init` でモデル取得・展開、`build`/`run-stt` を提供
2. **`task init` のドライラン**：手元でダウンロード〜展開まで通すか、最低でも URL 構造を確定
3. **csproj 作成**：`net10.0-windows`, WPF, `org.k2fsa.sherpa.onnx`, `NAudio.Wasapi`（または `NAudio`）, `Microsoft.Agents.AI` 系
4. **`appsettings.json`** に `Stt` / `Audio` セクション追加
5. **`MicrophoneCapture`**：`WaveInEvent` で 16kHz mono Float32 を読み、コールバックでバッファ提供
6. **`MoonshineRecognizer`**：Sherpa-onnx C# API でモデルロード、partial/final イベント発火
7. **`SpeechInputService`**：上記 2 つを束ね、PTT/常時 ON モードを実装
8. **MainWindow**：ボタン・partial 表示・履歴を実装、`final` を `agent.RunAsync` へ
9. **共通部コピー**：`AgentFactory.cs` / `Tools/GuiTools.cs` を第一プロジェクトから移植
10. **動作確認**：LM Studio + マイクで「背景を赤にして」など発話 → 反映を確認

---

## I. オープン項目（第二プロジェクト固有）

- Sherpa-onnx C# API の Moonshine 用 API シグネチャ確認（`OfflineRecognizerConfig` の `ModelConfig.Moonshine.{Preprocessor, Encoder, UncachedDecoder, CachedDecoder}` か、新しい `decoder_model_merged.ort` 1 ファイル方式に統一されているか — 1.10.x 系で API 差分あり）
- VAD モデルの選定: `silero-vad` (`silero_vad.onnx`) を併用する想定。`task fetch-models` に同梱するか別タスクにするか
- マイクのサンプリングレート変換が必要な場合の処理（多くのデバイスは 44.1kHz / 48kHz 出力。`WaveInEvent` で 16kHz 直指定可能か、リサンプリングが要るか）
- 認識中に LLM 応答が来る非同期競合の扱い（簡易にはマイクをミュート、慎重派はキューイング）

