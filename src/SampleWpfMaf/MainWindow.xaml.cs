using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using SampleWpfMaf.Core;
using SampleWpfMaf.Core.Tools;

namespace SampleWpfMaf;

public partial class MainWindow : Window
{
    private readonly AIAgent _agent;
    private AgentSession? _session;
    private bool _busy;

    public MainWindow()
    {
        this.InitializeComponent();

        var settings = App.Settings;
        this.ModelText.Text = $"{settings.Llm.Model} @ {settings.Llm.Endpoint}";

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

        this.AppendSystemMessage("LM Studio に接続します。送信してエージェントを試してください。");
        this.InputBox.Focus();
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        await this.SendAsync();
    }

    private async void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            await this.SendAsync();
        }
    }

    private async Task SendAsync()
    {
        if (this._busy)
        {
            return;
        }
        var text = this.InputBox.Text?.Trim();
        if (String.IsNullOrEmpty(text))
        {
            return;
        }

        this._busy = true;
        this.SendButton.IsEnabled = false;
        this.InputBox.IsEnabled = false;
        this.StatusText.Text = "考え中…";

        this.AppendUserMessage(text);
        this.InputBox.Clear();

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
            this._busy = false;
            this.SendButton.IsEnabled = true;
            this.InputBox.IsEnabled = true;
            this.InputBox.Focus();
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
            MaxWidth = 460,
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
}
