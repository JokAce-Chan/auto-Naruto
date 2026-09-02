using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using NaruttoTimer.Calibration;
using NaruttoTimer.Capture;
using NaruttoTimer.Data;
using NaruttoTimer.Overlay;
using NaruttoTimer.Recognition;
using NaruttoTimer.Rules;

namespace NaruttoTimer.App;

/// <summary>取色校准对话框：冻结帧点取像素 → 归类亮/暗 → 生成区间 → 保存。</summary>
public sealed class CalibrationDialog : Window
{
    private readonly CapturedFrame _frame;
    private readonly CalibrationStore _store;
    private readonly AppSettings _settings;
    private readonly List<(int R, int G, int B)> _brightSamples = new();
    private readonly List<(int R, int G, int B)> _darkSamples = new();
    private readonly Image _image = new() { Stretch = Stretch.Uniform, Margin = new Thickness(8) };
    private readonly TextBlock _rgbText = new() { Foreground = Brushes.LightGray, Margin = new Thickness(8, 4, 8, 0) };
    private readonly TextBlock _rangeText = new() { Foreground = Brushes.LightGray, Margin = new Thickness(8, 4, 8, 0), TextWrapping = TextWrapping.Wrap };
    private (int R, int G, int B)? _lastSample;

    public CalibrationDialog(CapturedFrame frame, CalibrationStore store, AppSettings settings)
    {
        _frame = frame;
        _store = store;
        _settings = settings;
        Title = "取色校准 · 点击菱形像素采样";
        Width = 920;
        Height = 660;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x18, 0x1B, 0x20));

        var bmp = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null);
        bmp.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.Pixels, frame.Width * 4, 0);
        _image.Source = bmp;
        _image.MouseLeftButtonDown += OnImageClick;

        var hint = new TextBlock { Text = "提示：点击像素后点 [设为亮] 或 [设为暗]；多点几次效果更好。", Foreground = Brushes.Gray, Margin = new Thickness(8, 6, 8, 0) };

        var btnBright = MakeButton("设为亮", () => AddSample(_brightSamples, "亮"));
        var btnDark = MakeButton("设为暗", () => AddSample(_darkSamples, "暗"));
        var btnClear = MakeButton("清空采样", () => { _brightSamples.Clear(); _darkSamples.Clear(); UpdateRanges(); });
        var btnSave = MakeButton("保存", () => Save());
        var btnCancel = MakeButton("取消", () => DialogResult = false);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(btnBright);
        buttons.Children.Add(btnDark);
        buttons.Children.Add(btnClear);
        buttons.Children.Add(btnSave);
        buttons.Children.Add(btnCancel);

        var panel = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        var info = new StackPanel { Orientation = Orientation.Vertical };
        info.Children.Add(hint);
        info.Children.Add(_rgbText);
        info.Children.Add(_rangeText);
        DockPanel.SetDock(info, Dock.Bottom);
        panel.Children.Add(info);
        panel.Children.Add(_image);
        Content = panel;

        var cal = _store.Load();
        _rangeText.Text = $"当前保存值：亮 {cal.Bright}；暗 {cal.Dark}";
    }

    private void OnImageClick(object sender, MouseButtonEventArgs e)
    {
        double aw = _image.ActualWidth, ah = _image.ActualHeight;
        if (aw <= 0 || ah <= 0 || _frame.Width <= 0 || _frame.Height <= 0) return;
        double scale = Math.Min(aw / _frame.Width, ah / _frame.Height);
        var pos = e.GetPosition(_image);
        int x = (int)((pos.X - (aw - _frame.Width * scale) / 2) / scale);
        int y = (int)((pos.Y - (ah - _frame.Height * scale) / 2) / scale);
        if (x < 0 || x >= _frame.Width || y < 0 || y >= _frame.Height) return;
        _frame.GetPixel(x, y, out byte b, out byte g, out byte r, out _);
        _lastSample = (r, g, b);
        _rgbText.Text = $"像素({x},{y})  RGB({r},{g},{b})";
    }

    private void AddSample(List<(int R, int G, int B)> list, string name)
    {
        if (_lastSample == null)
        {
            MessageBox.Show(this, "请先在预览图上点击一个像素", "取色校准", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        list.Add(_lastSample.Value);
        _rgbText.Text += $"  → 已加入「{name}」";
        UpdateRanges();
    }

    private void UpdateRanges()
    {
        string bright = _brightSamples.Count > 0 ? ColorRangeBuilder.Build(_brightSamples).ToString() : "（未采样）";
        string dark = _darkSamples.Count > 0 ? ColorRangeBuilder.Build(_darkSamples).ToString() : "（未采样）";
        _rangeText.Text = $"当前生成：亮 [{bright}]；暗 [{dark}]";
    }

    private void Save()
    {
        if (_brightSamples.Count == 0 || _darkSamples.Count == 0)
        {
            MessageBox.Show(this, "亮、暗各需至少采样一个像素", "取色校准", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var result = new CalibrationResult(ColorRangeBuilder.Build(_brightSamples), ColorRangeBuilder.Build(_darkSamples));
        _store.Save(result);
        _settings.BrightRange = result.Bright;
        _settings.DarkRange = result.Dark;
        DialogResult = true;
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

/// <summary>区域配置对话框：左右 ROI 坐标微调 + 测试识别 + 保存。</summary>
public sealed class RoiDialog : Window
{
    private readonly AppSettings _settings;
    private readonly CapturedFrame? _frame;
    private readonly Recognizer? _recognizer;
    private readonly TextBlock _result = new() { Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };

    private readonly TextBox _lx = new(), _ly = new(), _lw = new(), _lh = new();
    private readonly TextBox _rx = new(), _ry = new(), _rw = new(), _rh = new();

    public RoiDialog(AppSettings settings, CapturedFrame? frame, Recognizer? recognizer)
    {
        _settings = settings;
        _frame = frame;
        _recognizer = recognizer;
        Title = "区域配置 · 左右菱形区域坐标（相对当前预览帧）";
        Width = 620;
        Height = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x18, 0x1B, 0x20));

        var hint = new TextBlock
        {
            Text = "坐标格式：x,y,宽,高。可先 [测试识别] 验证，再 [保存]。6 格自动向屏幕中心延伸。",
            Foreground = Brushes.Gray,
            Margin = new Thickness(8, 6, 8, 0),
            TextWrapping = TextWrapping.Wrap,
        };

        _lx.Text = settings.LeftRoi?.X.ToString() ?? "";
        _ly.Text = settings.LeftRoi?.Y.ToString() ?? "";
        _lw.Text = settings.LeftRoi?.Width.ToString() ?? "";
        _lh.Text = settings.LeftRoi?.Height.ToString() ?? "";
        _rx.Text = settings.RightRoi?.X.ToString() ?? "";
        _ry.Text = settings.RightRoi?.Y.ToString() ?? "";
        _rw.Text = settings.RightRoi?.Width.ToString() ?? "";
        _rh.Text = settings.RightRoi?.Height.ToString() ?? "";

        var grid = new Grid { Margin = new Thickness(8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        int row = 0;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddRoiRow(grid, row++, "区域A（左）", _lx, _ly, _lw, _lh);
        AddRoiRow(grid, row++, "区域B（右）", _rx, _ry, _rw, _rh);

        var btnTest = MakeButton("测试识别", TestRecognize);
        var btnSave = MakeButton("保存", Save);
        var btnClear = MakeButton("恢复默认", () =>
        {
            var d = AppSettings.DefaultLeftRoi; var r = AppSettings.DefaultRightRoi;
            _lx.Text = d.X.ToString(); _ly.Text = d.Y.ToString(); _lw.Text = d.Width.ToString(); _lh.Text = d.Height.ToString();
            _rx.Text = r.X.ToString(); _ry.Text = r.Y.ToString(); _rw.Text = r.Width.ToString(); _rh.Text = r.Height.ToString();
            _result.Text = "已填入默认坐标，可 [测试识别] 验证";
        });
        var btnCancel = MakeButton("取消", () => DialogResult = false);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(8) };
        buttons.Children.Add(btnTest);
        buttons.Children.Add(btnSave);
        buttons.Children.Add(btnClear);
        buttons.Children.Add(btnCancel);

        var panel = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        var body = new StackPanel();
        body.Children.Add(hint);
        body.Children.Add(grid);
        body.Children.Add(_result);
        panel.Children.Add(body);
        Content = panel;
    }

    private void AddRoiRow(Grid grid, int row, string label, TextBox x, TextBox y, TextBox w, TextBox h)
    {
        var lbl = new TextBlock { Text = label, Foreground = Brushes.LightGray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4) };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        foreach (var (tb, hint) in new[] { (x, "x"), (y, "y"), (w, "宽"), (h, "高") })
        {
            tb.Width = 70;
            tb.Margin = new Thickness(0, 0, 6, 0);
            var inner = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            inner.Children.Add(new TextBlock { Text = hint, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 4, 0) });
            inner.Children.Add(tb);
            panel.Children.Add(inner);
        }
        Grid.SetRow(lbl, row);
        Grid.SetColumn(lbl, 0);
        Grid.SetRow(panel, row);
        Grid.SetColumn(panel, 1);
        grid.Children.Add(lbl);
        grid.Children.Add(panel);
    }

    private void TestRecognize()
    {
        var left = ParseRoi(_lx, _ly, _lw, _lh);
        var right = ParseRoi(_rx, _ry, _rw, _rh);
        if (_frame == null) { _result.Text = "暂无帧，请先连接并预览"; return; }
        if (_recognizer == null) { _result.Text = "识别器不可用"; return; }
        var opts = new RecognizerOptions(
            _settings.BrightRange, _settings.DarkRange, left, right,
            OrangeRange: RecognizerOptions.DefaultOrangeRange,
            DebounceFrames: Math.Max(1, _settings.DebounceFrames),
            LoadingSeconds: Math.Max(0.05, _settings.LoadingSeconds),
            GridJudgeSeconds: Math.Max(0.1, _settings.GridJudgeSeconds));
        var rec = new Recognizer(opts);
        var output = rec.Recognize(_frame);
        _result.Text =
            $"左：格数 {output.Left.GridCount} · 值 {output.Left.Value} · 加载 {output.Left.InLoading}    " +
            $"右：格数 {output.Right.GridCount} · 值 {output.Right.Value} · 加载 {output.Right.InLoading}";
    }

    private static RoiConfig? ParseRoi(TextBox x, TextBox y, TextBox w, TextBox h)
    {
        if (int.TryParse(x.Text, out int xi) && int.TryParse(y.Text, out int yi)
            && int.TryParse(w.Text, out int wi) && int.TryParse(h.Text, out int hi)
            && wi > 0 && hi > 0)
        {
            return new RoiConfig(xi, yi, wi, hi);
        }
        return null;
    }

    private void Save()
    {
        _settings.LeftRoi = ParseRoi(_lx, _ly, _lw, _lh);
        _settings.RightRoi = ParseRoi(_rx, _ry, _rw, _rh);
        DialogResult = true;
    }

    private static Button MakeButton(string text, Action onClick)
    {
        var btn = new Button { Content = text, Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x24, 0x29, 0x33)), Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x34, 0x3B, 0x47)), Cursor = Cursors.Hand };
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
                new GridViewColumn { Header = "时间", Width = 150, DisplayMemberBinding = new System.Windows.Data.Binding("Timestamp") { StringFormat = "yyyy-MM-dd HH:mm:ss.fff" } },
                new GridViewColumn { Header = "侧", Width = 50, DisplayMemberBinding = new System.Windows.Data.Binding("Side") },
                new GridViewColumn { Header = "格数", Width = 50, DisplayMemberBinding = new System.Windows.Data.Binding("GridCount") },
                new GridViewColumn { Header = "旧值", Width = 50, DisplayMemberBinding = new System.Windows.Data.Binding("OldValue") },
                new GridViewColumn { Header = "新值", Width = 50, DisplayMemberBinding = new System.Windows.Data.Binding("NewValue") },
                new GridViewColumn { Header = "动作", Width = 200, DisplayMemberBinding = new System.Windows.Data.Binding("Action") },
            }
        };

        var btnRefresh = MakeButton("刷新", () => Refresh());
        var btnCsv = MakeButton("导出 CSV", () => ExportCsv());
        var btnJson = MakeButton("导出 JSON", () => ExportJson());
        var btnClose = MakeButton("关闭", () => Close());
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
        var btn = new Button { Content = text, Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x24, 0x29, 0x33)), Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x34, 0x3B, 0x47)), Cursor = Cursors.Hand };
        btn.Click += (_, _) => onClick();
        return btn;
    }
}

/// <summary>设置对话框：设备、识别参数、路径、置顶框颜色。</summary>
public sealed class SettingsDialog : Window
{
    private readonly AppSettings _settings;
    private readonly TextBox _device = new(), _countdown = new(), _loading = new(), _debounce = new();
    private readonly TextBox _adb = new(), _scrcpy = new(), _dataDir = new();
    private readonly TextBox _leftColor = new(), _rightColor = new();

    public SettingsDialog(AppSettings settings)
    {
        _settings = settings;
        Title = "设置";
        Width = 520;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x18, 0x1B, 0x20));

        _device.Text = settings.DeviceSerial;
        _countdown.Text = settings.CountdownSeconds.ToString("0.#", CultureInfo.InvariantCulture);
        _loading.Text = settings.LoadingSeconds.ToString("0.##", CultureInfo.InvariantCulture);
        _debounce.Text = settings.DebounceFrames.ToString();
        _adb.Text = settings.AdbPath;
        _scrcpy.Text = settings.ScrcpyPath;
        _dataDir.Text = settings.DataDirectory;
        _leftColor.Text = settings.OverlayLeftColor;
        _rightColor.Text = settings.OverlayRightColor;

        var form = new StackPanel { Margin = new Thickness(12) };
        AddRow(form, "设备序列号", _device);
        AddRow(form, "倒计时时长（秒，默认 15）", _countdown);
        AddRow(form, "加载判定时长（秒，默认 0.3）", _loading);
        AddRow(form, "防抖帧数（默认 2）", _debounce);
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
        var scroll = new ScrollViewer { Content = form };
        panel.Children.Add(scroll);
        Content = panel;
    }

    private void AddRow(StackPanel parent, string label, TextBox tb)
    {
        var lbl = new TextBlock { Text = label, Foreground = Brushes.LightGray, Margin = new Thickness(0, 8, 0, 2) };
        tb.Margin = new Thickness(0, 0, 0, 4);
        parent.Children.Add(lbl);
        parent.Children.Add(tb);
    }

    private void Save()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_device.Text)) throw new ArgumentException("设备序列号不能为空");
            double countdown = double.Parse(_countdown.Text, CultureInfo.InvariantCulture);
            double loading = double.Parse(_loading.Text, CultureInfo.InvariantCulture);
            int debounce = int.Parse(_debounce.Text, CultureInfo.InvariantCulture);
            if (countdown <= 0) throw new ArgumentException("倒计时时长必须大于 0");
            if (loading <= 0) throw new ArgumentException("加载判定时长必须大于 0");
            if (debounce < 1) throw new ArgumentException("防抖帧数至少为 1");
            OverlayFormat.ParseHexColor(_leftColor.Text);
            OverlayFormat.ParseHexColor(_rightColor.Text);

            _settings.DeviceSerial = _device.Text.Trim();
            _settings.CountdownSeconds = countdown;
            _settings.LoadingSeconds = loading;
            _settings.DebounceFrames = debounce;
            _settings.AdbPath = _adb.Text.Trim();
            _settings.ScrcpyPath = _scrcpy.Text.Trim();
            _settings.DataDirectory = _dataDir.Text.Trim();
            _settings.OverlayLeftColor = _leftColor.Text.Trim();
            _settings.OverlayRightColor = _rightColor.Text.Trim();
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static Button MakeButton(string text, Action onClick)
    {
        var btn = new Button { Content = text, Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x24, 0x29, 0x33)), Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x34, 0x3B, 0x47)), Cursor = Cursors.Hand };
        btn.Click += (_, _) => onClick();
        return btn;
    }
}