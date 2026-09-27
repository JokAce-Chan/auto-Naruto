using System.Runtime.InteropServices;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using NaruttoTimer.Data;

namespace NaruttoTimer.Overlay;

public sealed class ArenaLineChangedEventArgs(string lineId, double y) : EventArgs
{
    public string LineId { get; } = lineId;
    public double Y { get; } = y;
}

/// <summary>
/// 绑定雷电 RenderWindow 的透明范围覆盖层。非编辑模式完全鼠标穿透，编辑模式允许拖动横线。
/// </summary>
public sealed class ArenaOverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpShowWindow = 0x0040;

    private readonly Grid _root = new() { ClipToBounds = true };
    private readonly Image _referenceImage = new()
    {
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
        IsHitTestVisible = false,
    };
    private readonly Canvas _linesCanvas = new()
    {
        Background = Brushes.Transparent,
        ClipToBounds = true,
    };
    private readonly List<(ArenaRangeLine Model, Line Visual)> _lineVisuals = [];

    private ArenaOverlaySettings _settings = new();
    private IntPtr _handle;
    private string? _imagePath;
    private Line? _draggedLine;
    private ArenaRangeLine? _draggedModel;
    private bool _editMode;

    public ArenaOverlayWindow()
    {
        Title = "决斗场范围显示";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        SnapsToDevicePixels = true;
        Width = 1;
        Height = 1;
        Left = -10000;
        Top = -10000;

        _root.Children.Add(_referenceImage);
        _root.Children.Add(_linesCanvas);
        Content = _root;

        SourceInitialized += (_, _) =>
        {
            _handle = new WindowInteropHelper(this).Handle;
            ApplyInputMode();
        };
        SizeChanged += (_, _) => DrawLines();
        _linesCanvas.MouseLeftButtonDown += LinesCanvas_MouseLeftButtonDown;
        _linesCanvas.MouseMove += LinesCanvas_MouseMove;
        _linesCanvas.MouseLeftButtonUp += LinesCanvas_MouseLeftButtonUp;
    }

    public event EventHandler<ArenaLineChangedEventArgs>? LineChanged;
    public event EventHandler? LineEditCompleted;

    public void ShowOverlay()
    {
        RunOnUi(() =>
        {
            if (!IsVisible) base.Show();
            if (_handle != IntPtr.Zero)
            {
                SetWindowPos(_handle, HwndTopmost, 0, 0, 0, 0,
                    SwpNoActivate | SwpNoSize | SwpNoMove);
            }
        });
    }

    public void HideOverlay()
    {
        RunOnUi(() =>
        {
            if (IsVisible) base.Hide();
        });
    }

    public void SetSettings(ArenaOverlaySettings settings)
    {
        RunOnUi(() =>
        {
            _settings = settings.Normalize();
            ApplyVisuals();
        });
    }

    public void SetBounds(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        RunOnUi(() =>
        {
            if (_handle != IntPtr.Zero)
            {
                SetWindowPos(_handle, HwndTopmost, x, y, width, height,
                    SwpNoActivate | SwpShowWindow);
                return;
            }

            Left = x;
            Top = y;
            Width = width;
            Height = height;
        });
    }

    public void SetEditMode(bool editMode)
    {
        RunOnUi(() =>
        {
            _editMode = editMode;
            _linesCanvas.IsHitTestVisible = editMode;
            _linesCanvas.Cursor = editMode ? Cursors.SizeNS : Cursors.Arrow;
            ApplyInputMode();
            DrawLines();
        });
    }

    public void SetReferenceImage(string? imagePath)
    {
        RunOnUi(() =>
        {
            if (string.Equals(_imagePath, imagePath, StringComparison.OrdinalIgnoreCase)) return;
            _imagePath = imagePath;
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                _referenceImage.Source = null;
                return;
            }

            try
            {
                string path = imagePath!;
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                _referenceImage.Source = bitmap;
            }
            catch
            {
                _referenceImage.Source = null;
            }
        });
    }

    public void UpdateSettings()
    {
        RunOnUi(ApplyVisuals);
    }

    private void ApplyVisuals()
    {
        bool showImage = string.Equals(_settings.ContentMode, "image", StringComparison.OrdinalIgnoreCase);
        _referenceImage.Visibility = showImage ? Visibility.Visible : Visibility.Collapsed;
        _referenceImage.Opacity = Math.Clamp(_settings.ImageOpacityPercent, 0, 100) / 100.0;
        _linesCanvas.Visibility = showImage ? Visibility.Collapsed : Visibility.Visible;
        DrawLines();
    }

    private void DrawLines()
    {
        _lineVisuals.Clear();
        _linesCanvas.Children.Clear();
        if (_draggedLine != null)
        {
            _draggedLine = null;
            _draggedModel = null;
        }

        double width = Math.Max(1, _linesCanvas.ActualWidth > 0 ? _linesCanvas.ActualWidth : Width);
        double height = Math.Max(1, _linesCanvas.ActualHeight > 0 ? _linesCanvas.ActualHeight : Height);
        foreach (ArenaRangeLine line in _settings.Lines)
        {
            if (!line.Enabled) continue;
            var visual = new Line
            {
                X1 = 0,
                X2 = width,
                Y1 = Math.Clamp(line.Y, 0, 1) * height,
                Y2 = Math.Clamp(line.Y, 0, 1) * height,
                Stroke = ParseBrush(line.Color),
                StrokeThickness = Math.Clamp(line.Thickness, 1, 50),
                Opacity = Math.Clamp(line.OpacityPercent, 0, 100) / 100.0,
                IsHitTestVisible = false,
            };
            _linesCanvas.Children.Add(visual);
            _lineVisuals.Add((line, visual));
        }
    }

    private void LinesCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_editMode) return;
        Point point = e.GetPosition(_linesCanvas);
        var hit = _lineVisuals
            .Select(item => (item.Model, item.Visual, Distance: Math.Abs(item.Visual.Y1 - point.Y)))
            .Where(item => item.Distance <= 14)
            .OrderBy(item => item.Distance)
            .FirstOrDefault();
        if (hit.Model == null) return;

        _draggedModel = hit.Model;
        _draggedLine = hit.Visual;
        _linesCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void LinesCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_editMode || _draggedLine == null || _draggedModel == null) return;
        double height = Math.Max(1, _linesCanvas.ActualHeight);
        double y = Math.Clamp(e.GetPosition(_linesCanvas).Y, 0, height);
        _draggedModel.Y = Math.Clamp(y / height, 0, 1);
        _draggedLine.Y1 = y;
        _draggedLine.Y2 = y;
        LineChanged?.Invoke(this, new ArenaLineChangedEventArgs(_draggedModel.Id, _draggedModel.Y));
        e.Handled = true;
    }

    private void LinesCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedLine == null) return;
        _draggedLine = null;
        _draggedModel = null;
        if (_linesCanvas.IsMouseCaptured) _linesCanvas.ReleaseMouseCapture();
        LineEditCompleted?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void ApplyInputMode()
    {
        if (_handle == IntPtr.Zero) return;
        long style = GetWindowLongPtr(_handle, GwlExStyle).ToInt64();
        style |= WsExToolWindow;
        if (_editMode)
        {
            style &= ~WsExTransparent;
            style &= ~WsExNoActivate;
        }
        else
        {
            style |= WsExTransparent;
            style |= WsExNoActivate;
        }
        SetWindowLongPtr(_handle, GwlExStyle, new IntPtr(style));
    }

    private static SolidColorBrush ParseBrush(string? color)
    {
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color ?? "#FFE65A"));
        }
        catch
        {
            return new SolidColorBrush(Color.FromRgb(0xFF, 0xE6, 0x5A));
        }
    }

    private void RunOnUi(Action action)
    {
        if (Dispatcher.CheckAccess()) action();
        else Dispatcher.Invoke(action);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr newValue);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr handle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
