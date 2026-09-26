using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NaruttoTimer.App;

/// <summary>对话框与预览区共用的少量 UI 资源（按钮样式、能量条配色）。</summary>
internal static class DialogUi
{
    /// <summary>左右能量条框体配色（主界面预览与「区域标注」弹窗保持一致）。</summary>
    internal static readonly Color LeftBarColor = Color.FromRgb(0x2E, 0xCC, 0x71);
    internal static readonly Color RightBarColor = Color.FromRgb(0xE0, 0x3E, 0x3E);

    /// <summary>对话框深色按钮（三个弹窗共用同一外观）。</summary>
    internal static Button MakeButton(string text, Action onClick)
    {
        var btn = new Button
        {
            Content = text,
            Padding = new Thickness(14, 5, 14, 5),
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x24, 0x29, 0x33)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x34, 0x3B, 0x47)),
            Cursor = Cursors.Hand,
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }
}
