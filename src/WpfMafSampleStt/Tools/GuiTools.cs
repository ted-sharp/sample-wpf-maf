using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WpfMafSampleStt.Tools;

/// <summary>
/// LLM から呼び出される GUI 操作 Tool 群。
/// 各メソッドは UI スレッドへの Dispatcher 経由で実行される。
/// </summary>
internal sealed class GuiTools
{
    private readonly Window _window;
    private readonly Canvas _canvas;

    public GuiTools(Window window, Canvas canvas)
    {
        this._window = window;
        this._canvas = canvas;
    }

    [Description("メインウィンドウのタイトルバーに表示される文字列を変更します。")]
    public string SetWindowTitle(
        [Description("新しいウィンドウタイトル")] string title)
    {
        return this.Invoke(() =>
        {
            this._window.Title = title;
            return $"ウィンドウタイトルを '{title}' に変更しました。";
        });
    }

    [Description("操作キャンバスの背景色を変更します。色名 (red, blue など) と 16 進数 (#FF8800) のどちらも受け付けます。")]
    public string SetBackgroundColor(
        [Description("色名または 16 進数表記 (#RRGGBB)")] string colorName)
    {
        return this.Invoke(() =>
        {
            var brush = ParseBrush(colorName);
            if (brush is null)
            {
                return $"色 '{colorName}' を解釈できませんでした。";
            }
            this._canvas.Background = brush;
            return $"背景色を '{colorName}' に変更しました。";
        });
    }

    [Description("操作キャンバス上の指定座標にテキストブロックを追加します。座標はキャンバス左上を (0, 0) とします。")]
    public string AddTextBlock(
        [Description("表示する文字列")] string text,
        [Description("X 座標 (px)")] double x,
        [Description("Y 座標 (px)")] double y)
    {
        return this.Invoke(() =>
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = 16,
                Foreground = Brushes.Black
            };
            Canvas.SetLeft(tb, x);
            Canvas.SetTop(tb, y);
            this._canvas.Children.Add(tb);
            return $"テキスト '{text}' を ({x}, {y}) に追加しました。";
        });
    }

    [Description("操作キャンバス上の指定座標にボタンを追加します。クリックされてもアプリ側のログに記録するだけです。")]
    public string AddButton(
        [Description("ボタン表面に表示するラベル")] string label,
        [Description("X 座標 (px)")] double x,
        [Description("Y 座標 (px)")] double y)
    {
        return this.Invoke(() =>
        {
            var btn = new Button
            {
                Content = label,
                Padding = new Thickness(8, 4, 8, 4),
                MinWidth = 60
            };
            btn.Click += (_, _) =>
            {
                System.Diagnostics.Debug.WriteLine($"[GuiTools] Button '{label}' clicked.");
            };
            Canvas.SetLeft(btn, x);
            Canvas.SetTop(btn, y);
            this._canvas.Children.Add(btn);
            return $"ボタン '{label}' を ({x}, {y}) に追加しました。";
        });
    }

    [Description("操作キャンバス上のすべての要素を削除し、背景色を白に戻します。")]
    public string ClearCanvas()
    {
        return this.Invoke(() =>
        {
            var count = this._canvas.Children.Count;
            this._canvas.Children.Clear();
            this._canvas.Background = Brushes.White;
            return $"{count} 個の要素を削除しました。";
        });
    }

    [Description("操作キャンバス上にある要素の一覧を JSON で返します。位置・種類・表示文字列を含みます。")]
    public string GetCanvasState()
    {
        return this.Invoke(() =>
        {
            var items = new List<object>();
            foreach (UIElement element in this._canvas.Children)
            {
                var info = new Dictionary<string, object?>
                {
                    ["type"] = element.GetType().Name,
                    ["x"] = Canvas.GetLeft(element),
                    ["y"] = Canvas.GetTop(element)
                };
                switch (element)
                {
                    case TextBlock tb:
                        info["text"] = tb.Text;
                        break;
                    case Button btn:
                        info["label"] = btn.Content?.ToString();
                        break;
                }
                items.Add(info);
            }

            var state = new
            {
                background = BrushToString(this._canvas.Background),
                width = this._canvas.ActualWidth,
                height = this._canvas.ActualHeight,
                items
            };
            return JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = false });
        });
    }

    [Description("メッセージダイアログを表示します。ユーザーが OK を押すまで処理は戻ります。")]
    public string ShowMessageBox(
        [Description("ダイアログに表示するメッセージ")] string message)
    {
        return this.Invoke(() =>
        {
            MessageBox.Show(this._window, message, this._window.Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return "メッセージダイアログを閉じました。";
        });
    }

    private T Invoke<T>(Func<T> action)
    {
        if (this._window.Dispatcher.CheckAccess())
        {
            return action();
        }
        return this._window.Dispatcher.Invoke(action);
    }

    private static Brush? ParseBrush(string color)
    {
        if (String.IsNullOrWhiteSpace(color))
        {
            return null;
        }
        try
        {
            var converted = ColorConverter.ConvertFromString(color);
            if (converted is Color c)
            {
                return new SolidColorBrush(c);
            }
        }
        catch
        {
            // ignore - try as brush name
        }
        try
        {
            var prop = typeof(Brushes).GetProperty(color, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.IgnoreCase);
            if (prop?.GetValue(null) is Brush b)
            {
                return b;
            }
        }
        catch
        {
            // ignore
        }
        return null;
    }

    private static string BrushToString(Brush? brush)
    {
        if (brush is SolidColorBrush scb)
        {
            var c = scb.Color;
            return String.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}{3:X2}", c.A, c.R, c.G, c.B);
        }
        return brush?.ToString() ?? "null";
    }
}
