using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using WpfMafSampleStt.Speech;
using WpfMafSampleStt.Tools;

namespace WpfMafSampleStt;

public partial class MainWindow : Window
{
    private readonly AIAgent _agent;
    private AgentSession? _session;
    private SpeechInputService? _speech;
    private bool _agentBusy;

    private readonly DispatcherTimer _countdownTimer;
    private readonly Stopwatch _recordingElapsed = new();
    private int _maxRecordingSeconds;
    private bool _stopping;

    public MainWindow()
    {
        this.InitializeComponent();

        var settings = App.Settings;
        this.ModelText.Text = $"LLM: {settings.Llm.Model} | STT: Moonshine ja";
        this._maxRecordingSeconds = Math.Max(5, settings.Stt.MaxRecordingSeconds);

        this._countdownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        this._countdownTimer.Tick += this.CountdownTimer_Tick;

        var tools = new GuiTools(this, this.TargetCanvas);
        var aiTools = new List<AITool>
        {
            AIFunctionFactory.Create(tools.SetWindowTitle),
            AIFunctionFactory.Create(tools.SetBackgroundColor),
            AIFunctionFactory.Create(tools.AddTextBlock),
            AIFunctionFactory.Create(tools.AddButton),
            AIFunctionFactory.Create(tools.ClearCanvas),
            AIFunctionFactory.Create(tools.GetCanvasState),
            AIFunctionFactory.Create(tools.ShowMessageBox)
        };

        var chatClient = AgentFactory.CreateChatClient(settings.Llm);
        this._agent = new ChatClientAgent(
            chatClient,
            new ChatClientAgentOptions
            {
                Name = settings.Agent.Name,
                ChatOptions = new ChatOptions
                {
                    Instructions = settings.Agent.Instructions,
                    Temperature = settings.Llm.Temperature,
                    Tools = aiTools
                }
            });

        Loaded += this.MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var settings = App.Settings;
        try
        {
            // partial と final で recognizer を分離: 同一インスタンスを連続 Decode すると
            // Sherpa-onnx の内部状態が劣化して幻覚が出ることが観測されたため。
            var partialRec = new MoonshineRecognizer(settings.Stt);
            var finalRec = new MoonshineRecognizer(settings.Stt);

            VadProcessor? partialVad = null;
            VadProcessor? finalVad = null;
            string vadStatus;
            if (!settings.Stt.UseVad)
            {
                vadStatus = "VAD: OFF (UseVad=false)";
            }
            else
            {
                try
                {
                    // partial と final で VAD インスタンスも分離 (recognizer と同じ理由)
                    partialVad = new VadProcessor(settings.Stt);
                    finalVad = new VadProcessor(settings.Stt);
                    vadStatus = $"VAD: ON (model={settings.Stt.VadModel})";
                }
                catch (System.IO.FileNotFoundException ex)
                {
                    vadStatus = $"VAD: OFF (モデル未配置 {ex.FileName})";
                    this.AppendErrorMessage($"VAD モデル未配置: {ex.FileName}\n`task download-vad` で取得できます。VAD なしで起動します。");
                }
                catch (Exception ex)
                {
                    vadStatus = $"VAD: OFF (初期化失敗: {ex.Message})";
                    this.AppendErrorMessage($"VAD 初期化に失敗しました: {ex.Message}");
                }
            }

            this._speech = new SpeechInputService(settings.Stt, settings.Audio, partialRec, finalRec, partialVad, finalVad);
            this._speech.PartialRecognized += this.OnPartialRecognized;
            this._speech.FinalRecognized += this.OnFinalRecognized;
            this._speech.MaxDurationReached += this.OnMaxDurationReached;
            this._speech.Error += text => this.Dispatcher.Invoke(() => this.AppendErrorMessage(text));

            this.PttButton.IsEnabled = true;
            this.StatusText.Text = $"準備完了 — {vadStatus}";
            this.AppendSystemMessage($"STT モデルを読み込みました。{vadStatus}\nマイクボタンを押しながら話してください。");
        }
        catch (System.IO.FileNotFoundException ex)
        {
            this.StatusText.Text = "STT モデル未配置";
            this.AppendErrorMessage($"モデルファイルが見つかりません: {ex.FileName}\n" +
                               "ターミナルで `task init` を実行してから再度起動してください。");
        }
        catch (Exception ex)
        {
            this.StatusText.Text = "STT 初期化失敗";
            this.AppendErrorMessage($"STT 初期化に失敗しました: {ex.Message}");
        }
    }

    private void PttButton_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (this._speech is null || this._agentBusy) return;
        this.PartialText.Text = "";
        this._recordingElapsed.Restart();
        this._countdownTimer.Start();
        this.UpdateCountdown();
        this._speech.StartPtt();
        // ボタン外でリリースされても確実に MouseUp が飛んでくるようにキャプチャ
        this.PttButton.CaptureMouse();
    }

    private async void PttButton_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (this.PttButton.IsMouseCaptured)
        {
            this.PttButton.ReleaseMouseCapture();
        }
        await this.StopRecordingAsync();
    }

    private async void PttButton_LostMouseCapture(object sender, MouseEventArgs e)
    {
        // 何らかの理由でキャプチャを失った場合の保険 (Alt+Tab など)
        await this.StopRecordingAsync();
    }

    private async Task StopRecordingAsync()
    {
        if (this._speech is null || !this._speech.IsRecording) return;
        if (this._stopping) return;
        this._stopping = true;
        try
        {
            this.StopCountdown();
            this.PttButton.Content = "🎤 押している間だけ録音";
            this.StatusText.Text = "認識中…";
            await this._speech.StopPttAndRecognizeAsync();
            if (!this._agentBusy)
            {
                this.StatusText.Text = "準備完了";
            }
        }
        finally
        {
            this._stopping = false;
        }
    }

    private void CountdownTimer_Tick(object? sender, EventArgs e) => this.UpdateCountdown();

    private void UpdateCountdown()
    {
        var elapsed = this._recordingElapsed.Elapsed.TotalSeconds;
        var remaining = Math.Max(0, this._maxRecordingSeconds - elapsed);
        var remainingSecCeil = (int)Math.Ceiling(remaining);

        var foreground = remaining <= 3 ? Brushes.Crimson
                       : remaining <= 8 ? Brushes.DarkOrange
                                        : Brushes.Black;

        this.PttButton.Foreground = foreground;
        this.PttButton.Content = $"🔴 録音中… 残り {remainingSecCeil} 秒";
        this.StatusText.Text = $"録音中 — 残り {remainingSecCeil} 秒 / 最大 {this._maxRecordingSeconds} 秒";
    }

    private void StopCountdown()
    {
        this._countdownTimer.Stop();
        this._recordingElapsed.Stop();
        this.PttButton.Foreground = Brushes.Black;
    }

    private void OnPartialRecognized(string text)
    {
        this.Dispatcher.BeginInvoke(new Action(() =>
        {
            this.PartialText.Text = String.IsNullOrEmpty(text) ? "" : $"… {text}";
        }));
    }

    private async void OnMaxDurationReached()
    {
        // Moonshine の処理可能長を超えたので、自動で確定処理に入る
        await this.Dispatcher.InvokeAsync(async () =>
        {
            if (this._speech is null || !this._speech.IsRecording) return;
            this.StopCountdown();
            this.PttButton.Content = "🎤 押している間だけ録音";
            this.StatusText.Text = "最大録音時間に到達 → 自動で認識中…";
            await this._speech.StopPttAndRecognizeAsync();
        });
    }

    private async void OnFinalRecognized(string text)
    {
        await this.Dispatcher.InvokeAsync(async () =>
        {
            this.PartialText.Text = "";
            var segs = this._speech?.LastFinalVadSegments ?? -1;
            var vadInfo = segs >= 0 ? $" [VAD: {segs} セグメント]" : " [VAD: 無効]";
            if (String.IsNullOrWhiteSpace(text))
            {
                var raw = this._speech?.LastFinalRaw ?? "";
                this.AppendErrorMessage($"認識結果が空でした (生 final=\"{raw}\"){vadInfo}。debug_recordings/ の WAV を確認してください。");
                return;
            }
            this.AppendUserMessage($"🎤 {text}{vadInfo}");
            await this.SendToAgentAsync(text);
        });
    }

    private async Task SendToAgentAsync(string text)
    {
        if (this._agentBusy) return;
        this._agentBusy = true;
        this.PttButton.IsEnabled = false;
        this.StatusText.Text = "アシスタント応答中…";

        try
        {
            this._session ??= await this._agent.CreateSessionAsync();
            var response = await this._agent.RunAsync(text, this._session);
            var reply = response?.Text ?? "(応答が空でした)";
            this.AppendAssistantMessage(reply);
            this.StatusText.Text = "準備完了";
        }
        catch (Exception ex)
        {
            this.AppendErrorMessage($"エラー: {ex.Message}");
            this.StatusText.Text = "エラー";
        }
        finally
        {
            this._agentBusy = false;
            this.PttButton.IsEnabled = true;
        }
    }

    private void AppendUserMessage(string text) => this.AppendBubble("あなた", text, Brushes.SteelBlue, HorizontalAlignment.Right);
    private void AppendAssistantMessage(string text) => this.AppendBubble("アシスタント", text, Brushes.SeaGreen, HorizontalAlignment.Left);
    private void AppendSystemMessage(string text) => this.AppendBubble("システム", text, Brushes.DimGray, HorizontalAlignment.Left);
    private void AppendErrorMessage(string text) => this.AppendBubble("エラー", text, Brushes.Crimson, HorizontalAlignment.Left);

    private void AppendBubble(string speaker, string text, Brush color, HorizontalAlignment alignment)
    {
        var border = new Border
        {
            BorderBrush = color,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 4, 0, 4),
            MaxWidth = 480,
            HorizontalAlignment = alignment,
            Background = Brushes.White
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = speaker,
            FontWeight = FontWeights.Bold,
            Foreground = color,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 2)
        });
        stack.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap
        });
        border.Child = stack;
        this.HistoryList.Items.Add(border);

        this.Dispatcher.BeginInvoke(new Action(() => this.HistoryScroll.ScrollToBottom()),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    protected override void OnClosed(EventArgs e)
    {
        this._speech?.Dispose();
        base.OnClosed(e);
    }
}
