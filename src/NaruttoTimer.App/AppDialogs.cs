using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using NaruttoTimer.Capture;
using NaruttoTimer.Data;
using NaruttoTimer.Overlay;
using NaruttoTimer.Rules;

namespace NaruttoTimer.App;

/// <summary>
/// 区域标注弹窗（对齐源项目 LabelRectActivity + LabelEnergyAIActivity）：
/// 左右能量条同高同宽、竖直联动；支持数字精确微调与即时测试。
/// </summary>
public sealed class LabelConfigDialog : Window
{
    private static readonly Color LeftBarColor = Color.FromRgb(0x2E, 0xCC, 0x71);
    private static readonly Color RightBarColor = Color.FromRgb(0xE0, 0x3E, 0x3E);

    private readonly CapturedFrame? _frame;
    private readonly Func<LabelImageConfig, string>? _tester;
    private readonly LabelLayoutEditor _editor;
    private readonly Image _image = new() { Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Top };
    private readonly Canvas _canvas = new() { Background = Brushes.Transparent };
    private readonly TextBlock _status = new() { Foreground = Brushes.LightGray, Margin = new Thickness(10, 4, 10, 0), TextWrapping = TextWrapping.Wrap };

    private readonly TextBox _barWidth = new(), _barHeight = new(), _barLeftX = new(), _barRightX = new(), _barTop = new();

    private LabelHandle _dragHandle = LabelHandle.None;
    private Point _dragLast;
    private double _scale = 1;
    private double _offsetX;
    private double _offsetY;

    public LabelConfigDialog(LabelImageConfig config, CapturedFrame? frame, Func<LabelImageConfig, string>? tester)
    {
        _frame = frame;
        _tester = tester;
        _editor = new LabelLayoutEditor(config);

        Title = "区域标注 · 左右能量条（归一化坐标 0~1）";
        Width = 1040;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x18, 0x1B, 0x20));

        if (frame != null)
        {
            var bmp = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null);
            bmp.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.Pixels, frame.Width * 4, 0);
            _image.Source = bmp;
        }

        _canvas.MouseLeftButtonDown += OnCanvasMouseDown;
        _canvas.MouseMove += OnCanvasMouseMove;
        _canvas.MouseLeftButtonUp += OnCanvasMouseUp;
        _image.SizeChanged += (_, _) => Render();

        var preview = new Grid();
        preview.Children.Add(_image);
        preview.Children.Add(_canvas);
        if (frame == null)
        {
            preview.Children.Add(new TextBlock
            {
                Text = "暂无帧：请先 [连接] 建立视频流后重新打开本弹窗，才能拖拽标注（可先手动输入数值）",
                Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        var hint = new TextBlock
        {
            Text = "拖动矩形内部移动；拖动边线／四角缩放（能量条左右同宽、同高、同顶联动）。绿色=左条，红色=右条。",
            Foreground = Brushes.Gray,
            Margin = new Thickness(10, 8, 10, 0),
            TextWrapping = TextWrapping.Wrap,
        };

        var btnApply = MakeButton("应用输入", ApplyInputs);
        var btnRefresh = MakeButton("从画面回读", () => { SyncTextFromConfig(); Render(); });
        var btnDefault = MakeButton("恢复默认", RestoreDefault);
        var btnTest = MakeButton("测试识别", TestRecognize);
        var btnSave = MakeButton("保存", Save);
        var btnCancel = MakeButton("取消", () => DialogResult = false);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(10) };
        buttons.Children.Add(btnApply);
        buttons.Children.Add(btnRefresh);
        buttons.Children.Add(btnDefault);
        buttons.Children.Add(btnTest);
        buttons.Children.Add(btnSave);
        buttons.Children.Add(btnCancel);

        var fields = new Grid { Margin = new Thickness(10, 4, 10, 4) };
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddField(fields, 0, "能量条公用 (宽,高,顶)", _barWidth, _barHeight, _barTop,
            new[] { ("w", "宽"), ("h", "高"), ("top", "顶") });
        AddField(fields, 1, "两侧左边线", _barLeftX, _barRightX,
            new[] { ("left.x1", "左条左边"), ("right.x1", "右条左边") });

        var bottom = new StackPanel();
        bottom.Children.Add(fields);
        bottom.Children.Add(_status);
        bottom.Children.Add(buttons);

        var panel = new DockPanel();
        DockPanel.SetDock(hint, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        panel.Children.Add(hint);
        panel.Children.Add(bottom);
        panel.Children.Add(preview);
        Content = panel;

        _editor.HitTolerance = 14;
        SyncTextFromConfig();
        Render();
    }

    public LabelImageConfig? Result { get; private set; }

    private static void AddField(Grid grid, int row, string title, TextBox a, TextBox b, TextBox c, (string key, string label)[] labels)
        => AddFieldCore(grid, row, title, new[] { a, b, c }, labels);

    private static void AddField(Grid grid, int row, string title, TextBox a, TextBox b, (string key, string label)[] labels)
        => AddFieldCore(grid, row, title, new[] { a, b }, labels);

    private static void AddFieldCore(Grid grid, int row, string title, TextBox[] boxes, (string key, string label)[] labels)
    {
        var titleBlock = new TextBlock
        {
            Text = title,
            Foreground = Brushes.LightGray,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 10, 4),
            FontSize = 12,
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        for (int i = 0; i < boxes.Length; i++)
        {
            boxes[i].Width = 90;
            boxes[i].Margin = new Thickness(0, 0, 8, 0);
            var inner = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            inner.Children.Add(new TextBlock { Text = labels[i].label, Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 0, 4, 0) });
            inner.Children.Add(boxes[i]);
            panel.Children.Add(inner);
        }
        Grid.SetRow(titleBlock, row);
        Grid.SetColumn(titleBlock, 0);
        Grid.SetRow(panel, row);
        Grid.SetColumn(panel, 1);
        grid.Children.Add(titleBlock);
        grid.Children.Add(panel);
    }

    private void SyncTextFromConfig()
    {
        var left = _editor.LeftBar;
        var right = _editor.RightBar;
        _barWidth.Text = Fmt(left.Width);
        _barHeight.Text = Fmt(left.Height);
        _barTop.Text = Fmt(left.LeftTop.Y);
        _barLeftX.Text = Fmt(left.LeftTop.X);
        _barRightX.Text = Fmt(right.LeftTop.X);
    }

    private static string Fmt(double value) => value.ToString("0.#####", CultureInfo.InvariantCulture);

    private void ApplyInputs()
    {
        try
        {
            double width = Parse(_barWidth);
            double height = Parse(_barHeight);
            double top = Parse(_barTop);
            double leftX = Parse(_barLeftX);
            double rightX = Parse(_barRightX);
            SetBar(_editor.LeftBar, leftX, top, width, height);
            SetBar(_editor.RightBar, rightX, top, width, height);

            var error = _editor.Config.Validate();
            _status.Text = error ?? "已应用输入（拖动或再微调后点 [保存]）";
            Render();
        }
        catch (Exception ex)
        {
            _status.Text = "输入无效：" + ex.Message;
        }
    }

    private static void SetBar(LabelRect rect, double x, double y, double width, double height)
    {
        rect.LeftTop.X = x;
        rect.LeftTop.Y = y;
        rect.RightBottom.X = x + width;
        rect.RightBottom.Y = y + height;
    }

    private static double Parse(TextBox box)
    {
        if (!double.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            throw new FormatException($"“{box.Text}” 不是数字");
        return value;
    }

    private void RestoreDefault()
    {
        var def = LabelImageConfig.CreateDefault();
        CopyRect(def.EnergyBar.Left, _editor.LeftBar);
        CopyRect(def.EnergyBar.Right, _editor.RightBar);
        SyncTextFromConfig();
        Render();
        _status.Text = "已恢复源项目默认标注值";
    }

    private static void CopyRect(LabelRect source, LabelRect target)
    {
        target.LeftTop.X = source.LeftTop.X;
        target.LeftTop.Y = source.LeftTop.Y;
        target.RightBottom.X = source.RightBottom.X;
        target.RightBottom.Y = source.RightBottom.Y;
    }

    private void TestRecognize()
    {
        if (_frame == null)
        {
            _status.Text = "暂无帧，无法测试（请先连接视频流）";
            return;
        }
        if (_tester == null)
        {
            _status.Text = "测试接口不可用";
            return;
        }
        try
        {
            _status.Text = _tester(_editor.Config.Clone());
        }
        catch (Exception ex)
        {
            _status.Text = "测试失败：" + ex.Message;
        }
    }

    private void Save()
    {
        ApplyInputs();
        var error = _editor.Config.Validate();
        if (error != null)
        {
            MessageBox.Show(this, error, "区域标注", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Result = _editor.Config.Clone();
        DialogResult = true;
    }

    // ── 拖拽 ──

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_frame == null) return;
        var p = e.GetPosition(_canvas);
        ToFramePixel(p, out double fx, out double fy);
        _dragHandle = _editor.HitTest(fx, fy, _frame.Width, _frame.Height);
        if (_dragHandle == LabelHandle.None) return;
        _dragLast = p;
        _canvas.CaptureMouse();
        e.Handled = true;
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragHandle == LabelHandle.None || _frame == null) return;
        var p = e.GetPosition(_canvas);
        double dx = (p.X - _dragLast.X) / _scale;
        double dy = (p.Y - _dragLast.Y) / _scale;
        _dragLast = p;
        _editor.Drag(_dragHandle, dx, dy, _frame.Width, _frame.Height);
        Render();
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragHandle == LabelHandle.None) return;
        _dragHandle = LabelHandle.None;
        _canvas.ReleaseMouseCapture();
        SyncTextFromConfig();
    }

    private void ToFramePixel(Point canvasPoint, out double fx, out double fy)
    {
        fx = (canvasPoint.X - _offsetX) / _scale;
        fy = (canvasPoint.Y - _offsetY) / _scale;
    }

    private void Render()
    {
        if (_frame == null) return;
        double aw = _image.ActualWidth, ah = _image.ActualHeight;
        if (aw <= 0 || ah <= 0) return;
        _scale = Math.Min(aw / _frame.Width, ah / _frame.Height);
        var origin = _image.TranslatePoint(new Point(0, 0), _canvas);
        _offsetX = origin.X + (aw - _frame.Width * _scale) / 2;
        _offsetY = origin.Y + (ah - _frame.Height * _scale) / 2;

        _canvas.Children.Clear();
        DrawRect(_editor.LeftBar, LeftBarColor, "左");
        DrawRect(_editor.RightBar, RightBarColor, "右");
    }

    private void DrawRect(LabelRect rect, Color color, string label)
    {
        if (rect == null || _frame == null) return;
        double x = _offsetX + rect.LeftTop.X * _frame.Width * _scale;
        double y = _offsetY + rect.LeftTop.Y * _frame.Height * _scale;
        double w = Math.Max(1, rect.Width * _frame.Width * _scale);
        double h = Math.Max(1, rect.Height * _frame.Height * _scale);

        var border = new System.Windows.Shapes.Rectangle
        {
            Width = w,
            Height = h,
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 2 },
            Fill = Brushes.Transparent,
        };
        Canvas.SetLeft(border, x);
        Canvas.SetTop(border, y);
        _canvas.Children.Add(border);

        var text = new TextBlock { Text = label, Foreground = new SolidColorBrush(color), FontSize = 13, FontWeight = FontWeights.Bold };
        Canvas.SetLeft(text, x);
        Canvas.SetTop(text, Math.Max(0, y - 18));
        _canvas.Children.Add(text);
    }

    private static Button MakeButton(string text, Action onClick)
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

/// <summary>数据保存对话框：事件列表 + 导出。</summary>
public sealed class DataDialog : Window
{
    private readonly DataStore _store;
    private readonly ListView _list = new() { Margin = new Thickness(8) };

    public DataDialog(DataStore store)
    {
        _store = store;
        Title = "数据保存 · 触发事件";
        Width = 720;
        Height = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x18, 0x1B, 0x20));

        _list.View = new GridView
        {
            Columns =
            {
                new GridViewColumn { Header = "时间", Width = 170, DisplayMemberBinding = new System.Windows.Data.Binding("Timestamp") { StringFormat = "yyyy-MM-dd HH:mm:ss.fff" } },
                new GridViewColumn { Header = "侧", Width = 60, DisplayMemberBinding = new System.Windows.Data.Binding("Side") },
                new GridViewColumn { Header = "旧值", Width = 60, DisplayMemberBinding = new System.Windows.Data.Binding("OldValue") },
                new GridViewColumn { Header = "新值", Width = 60, DisplayMemberBinding = new System.Windows.Data.Binding("NewValue") },
                new GridViewColumn { Header = "动作", Width = 200, DisplayMemberBinding = new System.Windows.Data.Binding("Action") },
            }
        };

        var btnRefresh = MakeButton("刷新", Refresh);
        var btnCsv = MakeButton("导出 CSV", ExportCsv);
        var btnJson = MakeButton("导出 JSON", ExportJson);
        var btnClose = MakeButton("关闭", Close);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(btnRefresh);
        buttons.Children.Add(btnCsv);
        buttons.Children.Add(btnJson);
        buttons.Children.Add(btnClose);

        var panel = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        panel.Children.Add(_list);
        Content = panel;
        Refresh();
    }

    private void Refresh() => _list.ItemsSource = _store.GetTriggers();

    private void ExportCsv()
    {
        var dlg = new SaveFileDialog { Filter = "CSV 文件|*.csv", FileName = $"triggers-{DateTime.Now:yyyyMMdd}.csv" };
        if (dlg.ShowDialog(this) == true)
        {
            _store.ExportCsv(dlg.FileName);
            MessageBox.Show(this, $"已导出：{dlg.FileName}", "数据保存", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ExportJson()
    {
        var dlg = new SaveFileDialog { Filter = "JSON 文件|*.json", FileName = $"triggers-{DateTime.Now:yyyyMMdd}.json" };
        if (dlg.ShowDialog(this) == true)
        {
            _store.ExportJson(dlg.FileName);
            MessageBox.Show(this, $"已导出：{dlg.FileName}", "数据保存", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private static Button MakeButton(string text, Action onClick)
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

/// <summary>设置对话框：设备、路径、识别参数、置顶框颜色。</summary>
public sealed class SettingsDialog : Window
{
    private readonly AppSettings _settings;
    private readonly TextBox _device = new(), _countdown = new(), _stable = new(), _interval = new();
    private readonly TextBox _confidence = new(), _nms = new();
    private readonly TextBox _adb = new(), _scrcpy = new(), _dataDir = new();
    private readonly TextBox _leftColor = new(), _rightColor = new();

    public SettingsDialog(AppSettings settings)
    {
        _settings = settings;
        Title = "设置";
        Width = 560;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x18, 0x1B, 0x20));

        _device.Text = settings.DeviceSerial;
        _countdown.Text = settings.CountdownSeconds.ToString("0.##", CultureInfo.InvariantCulture);
        _stable.Text = settings.StableFrames.ToString(CultureInfo.InvariantCulture);
        _interval.Text = settings.InferenceIntervalMs.ToString(CultureInfo.InvariantCulture);
        _confidence.Text = settings.ConfidenceThreshold.ToString("0.##", CultureInfo.InvariantCulture);
        _nms.Text = settings.NmsThreshold.ToString("0.##", CultureInfo.InvariantCulture);
        _adb.Text = settings.AdbPath;
        _scrcpy.Text = settings.ScrcpyPath;
        _dataDir.Text = settings.DataDirectory;
        _leftColor.Text = settings.OverlayLeftColor;
        _rightColor.Text = settings.OverlayRightColor;

        var form = new StackPanel { Margin = new Thickness(12) };
        AddRow(form, "设备序列号", _device);
        AddRow(form, "替换身倒计时（秒，默认 14.5）", _countdown);
        AddRow(form, "稳定判定帧数（默认 3）", _stable);
        AddRow(form, "推理间隔（毫秒，默认 125；调大可降低 CPU 占用）", _interval);
        AddRow(form, "AI 置信度阈值（默认 0.7）", _confidence);
        AddRow(form, "NMS IoU 阈值（默认 0.25）", _nms);
        AddRow(form, "adb 路径（留空自动查找）", _adb);
        AddRow(form, "scrcpy/FFmpeg 目录（留空自动查找）", _scrcpy);
        AddRow(form, "数据目录（默认 data）", _dataDir);
        AddRow(form, "置顶框左数字颜色 #RRGGBB", _leftColor);
        AddRow(form, "置顶框右数字颜色 #RRGGBB", _rightColor);

        var btnSave = MakeButton("保存", Save);
        var btnCancel = MakeButton("取消", () => DialogResult = false);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12) };
        buttons.Children.Add(btnSave);
        buttons.Children.Add(btnCancel);

        var panel = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        panel.Children.Add(new ScrollViewer { Content = form });
        Content = panel;
    }

    private static void AddRow(StackPanel parent, string label, TextBox box)
    {
        parent.Children.Add(new TextBlock { Text = label, Foreground = Brushes.LightGray, Margin = new Thickness(0, 8, 0, 2) });
        box.Margin = new Thickness(0, 0, 0, 4);
        parent.Children.Add(box);
    }

    private void Save()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_device.Text)) throw new ArgumentException("设备序列号不能为空");
            double countdown = ParseDouble(_countdown, "替换身倒计时");
            int stable = ParseInt(_stable, "稳定判定帧数");
            int interval = ParseInt(_interval, "推理间隔");
            double confidence = ParseDouble(_confidence, "AI 置信度阈值");
            double nms = ParseDouble(_nms, "NMS 阈值");
            if (countdown <= 0) throw new ArgumentException("替换身倒计时必须大于 0");
            if (stable < 1) throw new ArgumentException("稳定判定帧数至少为 1");
            if (interval < 16) throw new ArgumentException("推理间隔至少 16 毫秒");
            if (confidence <= 0 || confidence > 1) throw new ArgumentException("置信度阈值需在 0~1 之间");
            if (nms <= 0 || nms > 1) throw new ArgumentException("NMS 阈值需在 0~1 之间");
            OverlayFormat.ParseHexColor(_leftColor.Text);
            OverlayFormat.ParseHexColor(_rightColor.Text);

            _settings.DeviceSerial = _device.Text.Trim();
            _settings.CountdownSeconds = countdown;
            _settings.StableFrames = stable;
            _settings.InferenceIntervalMs = interval;
            _settings.ConfidenceThreshold = confidence;
            _settings.NmsThreshold = nms;
            _settings.AdbPath = _adb.Text.Trim();
            _settings.ScrcpyPath = _scrcpy.Text.Trim();
            _settings.DataDirectory = _dataDir.Text.Trim();
            _settings.OverlayLeftColor = _leftColor.Text.Trim();
            _settings.OverlayRightColor = _rightColor.Text.Trim();
            _settings.Normalize();
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static double ParseDouble(TextBox box, string name)
    {
        if (!double.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            throw new ArgumentException($"{name} 不是有效数字");
        return value;
    }

    private static int ParseInt(TextBox box, string name)
    {
        if (!int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            throw new ArgumentException($"{name} 不是有效整数");
        return value;
    }

    private static Button MakeButton(string text, Action onClick)
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
