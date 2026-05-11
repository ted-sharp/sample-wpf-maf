# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## リポジトリ概要

WPF (.NET 10, `net10.0-windows`) 上で **Microsoft Agent Framework (MAF)** を動かし、LLM が Tool 経由で GUI を操作するサンプル。LLM バックエンドは **LM Studio (OpenAI 互換, `http://localhost:1234/v1`)** + Gemma を前提とする。詳細仕様は `SPEC.md` を参照。

slnx ソリューション (`WpfMafSample.slnx`) に 2 つの WPF プロジェクトが共存：

| プロジェクト | 役割 |
|---|---|
| `src/WpfMafSample/`    | テキストチャット版。最小構成 |
| `src/WpfMafSampleStt/` | 上記 + マイク入力 (Sherpa-onnx / Moonshine ja + Silero VAD) |

両プロジェクトは独立性を保つため `AgentFactory.cs` と `Tools/GuiTools.cs` を**コピー共有**している（共通ライブラリ化していない）。片方を変更したらもう片方も同期する必要がある。

## ビルド / 実行

タスクランナーは **Task (taskfile.dev)** を採用。`scoop install task` / `winget install Task.Task` で導入。

```sh
task init               # 初回セットアップ: STT モデル取得 + 全プロジェクトビルド
task build              # dotnet build WpfMafSample.slnx
task run                # テキスト版 (WpfMafSample)
task run-stt            # 音声版 (WpfMafSampleStt) - 要 download-models
task download-models    # Moonshine base ja + silero_vad.onnx を models/ に取得
task download-moonshine VARIANT=tiny   # 軽量モデルに切替
task clean-models       # models/ を削除
```

`models/` は gitignore 対象。`WpfMafSampleStt` のモデルパス解決 (`MoonshineRecognizer.ResolvePath`) は `AppContext.BaseDirectory` → 5 階層上のリポジトリルートの順で探すので、開発時 (`dotnet run`) もリリース時 (`bin/.../net10.0-windows/`) も同じ `appsettings.json` で動く。

dotnet 単体で動かしたい場合：

```sh
dotnet build WpfMafSample.slnx
dotnet run --project src/WpfMafSample/WpfMafSample.csproj
dotnet run --project src/WpfMafSampleStt/WpfMafSampleStt.csproj
```

LM Studio は別途起動しておく。モデル ID は `appsettings.json` の `Llm.Model` と完全一致させる必要がある（既定 `google/gemma-4-e2b`）。

## アーキテクチャ要点

### コードビハインドオンリー方針

**MVVM / ViewModel は採用しない**。`MainWindow.xaml.cs` から UI を直接触る。Tool は `MainWindow` のフィールドに保持する `GuiTools` インスタンスのメソッドを `AIFunctionFactory.Create(...)` で MAF に登録する。`GuiTools` 内のすべての操作は `Dispatcher.Invoke` で UI スレッドへマーシャルする（`GuiTools.Invoke<T>`）。

### MAF 接続

`AgentFactory.CreateChatClient` は **`OpenAIClient` の `Endpoint` を LM Studio に上書き** + `ApiKey` が空なら `"lm-studio"` ダミー値を入れる → `IChatClient` → `ChatClientAgent` の流れ。`AgentSession` は初回送信時に `CreateSessionAsync` で作って再利用する。

### STT 版の擬似ストリーミング

Moonshine は本来 **non-streaming** の短尺モデル (~30 秒)。本実装は PTT (Push-To-Talk) を前提に、

1. `MicrophoneCapture` (NAudio `WaveInEvent`, 16kHz / mono / Float32) で累積バッファに録音
2. 録音中は `PartialIntervalMs` 間隔で **末尾 `PartialMaxSeconds` 秒だけ**を `OfflineRecognizer` に投げて partial 表示
3. リリース時に全サンプルを VAD でセグメント化 → セグメントごとに `OfflineRecognizer.Decode` → 結合
4. VAD 無効時は `FinalChunkSeconds` 単位 + `FinalChunkOverlapSeconds` のオーバーラップでチャンク分割

ことで擬似ストリーミングを実現している。`MaxRecordingSeconds` (既定 25 秒) で自動確定。

### Moonshine の落とし穴 (絶対に守る)

- **partial 用と final 用で `MoonshineRecognizer` インスタンスを必ず分ける** (`VadProcessor` も同様)。同一インスタンスで Decode を連続呼びすると Sherpa-onnx の内部状態が劣化して幻覚 (「あっ」「うん」等の偽認識) が出る。`MainWindow.xaml.cs` の `MainWindow_Loaded` で 2 つずつ生成しているのはこのため
- partial 認識中は `Interlocked` (`_decoding`) で 1 回ぶんの推論しか走らせない
- `StopPttAndRecognizeAsync` は `Interlocked.CompareExchange(_stopping)` で多重呼び出し (`PreviewMouseUp` + `LostMouseCapture` + `MaxDurationReached`) を吸収する。これを外すと NRE が出る
- VAD セグメントが短すぎる (`PartialMinSegmentSeconds` 未満) と幻覚源になるのでスキップ
- final が空 / 短すぎたら直前の partial を fallback に採用 (`_lastPartial`)

### 設定 (`appsettings.json`)

- 実行ファイル隣の `appsettings.json` を `App.OnStartup` で読み込み、`appsettings.local.json` (gitignore 対象) で上書き可
- `AppSettings` は `Llm` / `Agent` (両プロジェクト共通) + STT 版のみ `Stt` / `Audio` を持つ
- `Stt.SaveDebugWav = true` で録音 PCM が `<bin>/debug_recordings/rec-*.wav` に保存される。Moonshine が何を聞いていたか確認するためのデバッグ機能

## コーディング規約 (`.editorconfig`)

- **4-space インデント、CRLF、ファイル末尾改行必須**
- `this.` 修飾は**必須** (field / property / method / event すべて severity:error)
- BCL 型名を使う (`String.IsNullOrEmpty`、`Int32` ではなく `int` ではあるが `String` を優先): `dotnet_style_predefined_type_for_member_access = false`
- `var` は基本的にどこでも使う
- private field は `_camelCase`、private static field は `s_camelCase`
- StyleCop の SA1200 (using は namespace の外), SA1308/1309/1311 (アンダースコア prefix), SA1502 (1 行) は意図的に無効化済み

## 参考: Tool 一覧 (`GuiTools.cs`)

LLM から呼べる Tool は両プロジェクト共通。追加時は `MainWindow.xaml.cs` の `aiTools` リストにも `AIFunctionFactory.Create` で登録すること。

| Tool | 概要 |
|---|---|
| `SetWindowTitle(title)`              | ウィンドウタイトル変更 |
| `SetBackgroundColor(colorName)`      | キャンバス背景色 (色名 or `#RRGGBB`) |
| `AddTextBlock(text, x, y)`           | テキスト配置 |
| `AddButton(label, x, y)`             | ボタン配置 (クリックは `Debug.WriteLine` のみ) |
| `ClearCanvas()`                      | 全削除 + 背景白 |
| `GetCanvasState()`                   | 現在のキャンバス状態を JSON で返す |
| `ShowMessageBox(message)`            | MessageBox を表示 |

色解釈は `ColorConverter.ConvertFromString` → `typeof(Brushes).GetProperty(name, IgnoreCase)` の二段階フォールバック (`GuiTools.ParseBrush`)。
