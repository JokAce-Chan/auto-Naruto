using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using NaruttoTimer.Rules;

namespace NaruttoTimer.Overlay;

/// <summary>
/// 半透明无边框置顶框：显示 "0.00  0.00"（左绿右红，可自选颜色）。
/// 字号随框高、间距随框宽；未锁定时可拖动/缩放；锁定后不可拖动/缩放。
/// </summary>
public sealed class OverlayWindow : Window, IOverlayService
{
    private readonly TextBlock _leftText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _rightText = new() { VerticalAlignment = VerticalAlignment.Center };
    private bool _locked;
    private (byte R, byte G, byte B) _leftColor = (31, 157, 85);
    private (byte R, byte G, byte B) _rightColor = (224, 62, 62);
    private int _backgroundPercent = 55;
    private int _textPercent = 100;

    public OverlayWindow()
    {
        Title = "替身计时器";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize; // 缩放由右下角手柄自绘实现
        AllowsTransparency = true;
        Background = BackgroundBrush();
        Opacity = 1.0;
        Topmost = true;
        ShowInTaskbar = false;
        Width = 220;
        Height = 64;
        MinWidth = 60;
        MinHeight = 32;
        Left = Math.Max(0, (SystemParameters.WorkArea.Width - Width) / 2);
        Top = 40;

        var root = new Grid();
        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        stack.Children.Add(_leftText);
        stack.Children.Add(_rightText);
        root.Children.Add(stack);

        var thumb = new Thumb
        {
            Width = 16,
            Height = 16,
            Cursor = Cursors.SizeNWSE,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Opacity = 0.35,
            Background = Brushes.Gray,
        };
        thumb.DragDelta += (_, e) =>
        {
            if (_locked) return;
            Width = Math.Max(MinWidth, Width + e.HorizontalChange);
            Height = Math.Max(MinHeight, Height + e.VerticalChange);
        };
        root.Children.Add(thumb);
        Content = root;

        MouseLeftButtonDown += (_, e) =>
        {
            if (!_locked && e.LeftButton == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { /* 拖动中释放可忽略 */ }
            }
        };

        SizeChanged += (_, _) => ApplyLayout();
        ApplyLayout();
        Update(CountdownSnapshot.Zero);
    }

    public bool IsLocked => _locked;

    private void ApplyLayout()
    {
        double fontSize = OverlayFormat.FontSizeForHeight(ActualHeight > 0 ? ActualHeight : Height);
        double spacing = OverlayFormat.SpacingForWidth(ActualWidth > 0 ? ActualWidth : Width);

        var font = new FontFamily("Segoe UI");
        _leftText.FontSize = fontSize;
        _rightText.FontSize = fontSize;
        _leftText.FontFamily = font;
        _rightText.FontFamily = font;
        _leftText.Foreground = TextBrush(_leftColor);
        _rightText.Foreground = TextBrush(_rightColor);
        _leftText.Margin = new Thickness(0, 0, spacing / 2, 0);
        _rightText.Margin = new Thickness(spacing / 2, 0, 0, 0);
    }

    private SolidColorBrush TextBrush((byte R, byte G, byte B) c)
    {
        int alpha = Math.Clamp(_textPercent, 0, 100) * 255 / 100;
        return new(Color.FromArgb((byte)alpha, c.R, c.G, c.B));
    }

    private SolidColorBrush BackgroundBrush()
    {
        int alpha = Math.Clamp(_backgroundPercent, 0, 100) * 255 / 100;
        return new(Color.FromArgb((byte)alpha, 0xFF, 0xFF, 0xFF));
    }

    // ── IOverlayService ──

    public new void Show()
    {
        RunOnUi(() =>
        {
            if (!IsVisible) base.Show();
            Activate();
        });
    }

    public new void Hide()
    {
        RunOnUi(() =>
        {
            if (IsVisible) base.Hide();
        });
    }

    public void SetLocked(bool locked)
    {
        RunOnUi(() => _locked = locked);
    }

    public void SetOpacity(int percent)
    {
        int validated = OverlayFormat.ValidateOpacityPercent(percent);
        RunOnUi(() =>
        {
            _backgroundPercent = validated;
            Background = BackgroundBrush();
        });
    }

    public void SetTextOpacity(int percent)
    {
        int validated = Math.Clamp(percent, 0, 100);
        RunOnUi(() =>
        {
            _textPercent = validated;
            ApplyLayout();
        });
    }

    public void SetColors(string leftHex, string rightHex)
    {
        var l = OverlayFormat.ParseHexColor(leftHex);
        var r = OverlayFormat.ParseHexColor(rightHex);
        RunOnUi(() =>
        {
            _leftColor = l;
            _rightColor = r;
            ApplyLayout();
        });
    }

    public void Update(CountdownSnapshot snapshot)
    {
        RunOnUi(() =>
        {
            _leftText.Text = OverlayFormat.FormatSeconds(snapshot.LeftSeconds);
            _rightText.Text = OverlayFormat.FormatSeconds(snapshot.RightSeconds);
        });
    }

    private void RunOnUi(Action action)
    {
        if (Dispatcher.CheckAccess()) action();
        else Dispatcher.Invoke(action);
    }
}
