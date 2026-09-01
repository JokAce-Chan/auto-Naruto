using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NaruttoTimer.Calibration;
using NaruttoTimer.Capture;
using NaruttoTimer.Data;
using NaruttoTimer.Overlay;
using NaruttoTimer.Recognition;
using NaruttoTimer.Rules;

namespace NaruttoTimer.App;

/// <summary>主窗口：采集、识别、规则、置顶框、数据保存全链路接线。</summary>
public partial class MainWindow : Window
{
    private static readonly Brush Gray = Brush("#6B7480");
    private static readonly Brush Green = Brush("#2ECC71");
    private static readonly Brush Yellow = Brush("#E6A23C");
    private static readonly Brush Red = Brush("#FF5C5C");

    private readonly string _dataRoot;
    private readonly JsonSettingsStore _settingsStore;
    private readonly CalibrationStore _calibrationStore;
    private readonly DataStore _dataStore;
    private readonly DispatcherTimer _uiTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    private AppSettings _settings;
    private ScreenRecordCaptureSource? _capture;
    private PipelineController? _pipeline;
    private OverlayWindow? _overlay;
    private CapturedFrame? _lastFrame;
    private RecognitionOutput? _lastOutput;
    private CountdownSnapshot _lastSnapshot = new(0, 0, SideStatus.Normal, SideStatus.Normal);
    private bool _renderScheduled;
    private bool _previewVisible = true;
    private GridCount? _lastLeftGrid;
    private GridCount? _lastRightGrid;
    private readonly string _logPath;
    private bool _firstFrameLogged;
    private int _fpsLogCounter;

    public MainWindow()
    {
        InitializeComponent();

        _dataRoot = Path.Combine(AppContext.BaseDirectory, "data");
        _settingsStore = new JsonSettingsStore(Path.Combine(_dataRoot, "settings.json"));
        _settings = _settingsStore.Load();
        _calibrationStore = new CalibrationStore(Path.Combine(_dataRoot, "calibration.json"));
        _dataStore = new DataStore(_dataRoot);
        _logPath = Path.Combine(AppContext.BaseDirectory, "logs", "nt-debug.log");
        try { Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!); } catch { }
        Log($"App 启动 {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");

        BuildCore();

        OpacitySlider.ValueChanged += (_, _) =>
        {
            OpacityValue.Text = $"{(int)OpacitySlider.Value}%";
            _overlay?.SetOpacity((int)OpacitySlider.Value);
        };
        _uiTimer.Tick += (_, _) => UiTick();

        PreviewToggle.IsChecked = true;
        UpdatePreviewVisibility();
        ApplySettingsToUi();
    }

    // ── 构建 ──

    private void BuildCore()
    {
        var adbPath = AdbCli.FindAdbPath(string.IsNullOrEmpty(_settings.AdbPath) ? null : _settings.AdbPath);
        var ffDir = FfmpegLocator.FindDirectory(string.IsNullOrEmpty(_settings.ScrcpyPath) ? null : _settings.ScrcpyPath);
        _capture = new ScreenRecordCaptureSource(
            new CaptureOptions(adbPath, _settings.DeviceSerial, _settings.VideoWidth, _settings.VideoHeight, _settings.CropTopRows, _settings.MaxFps),
            ffDir);
        _capture.StateChanged += OnCaptureStateRaw;
        _capture.FrameReady += OnRawFrame;
        _pipeline = new PipelineController(_capture, BuildRecognizer(), new CountdownEngine(_settings.CountdownSeconds), _dataStore);
        SubscribePipeline();
    }

    private Recognizer BuildRecognizer()
    {
        var cal = _calibrationStore.Load();
        var opts = new RecognizerOptions(
            cal.Bright, cal.Dark, _settings.LeftRoi, _settings.RightRoi,
            OrangeRange: RecognizerOptions.DefaultOrangeRange,
            DebounceFrames: Math.Max(1, _settings.DebounceFrames),
            LoadingSeconds: Math.Max(0.05, _settings.LoadingSeconds));
        return new Recognizer(opts);
    }

    private void SubscribePipeline()
    {
        if (_pipeline == null) return;
        _pipeline.RecognitionUpdated += OnRecognitionUpdated;
        _pipeline.CountdownUpdated += OnCountdownUpdated;
        _pipeline.Triggered += OnTriggered;
        _pipeline.Logged += OnLogged;
    }

    private void OnCaptureStateRaw(object? sender, CaptureStateChangedEventArgs e) => OnCaptureStateChanged(e.State, e.Message);

    private void OnRawFrame(object? sender, CapturedFrame frame) => OnFrameAvailable(frame);

    private void ApplySettingsToUi()
    {
        DeviceText.Text = $"雷电模拟器 {_settings.DeviceSerial}";
        OpacitySlider.Value = Math.Clamp(_settings.OverlayOpacityPercent, 10, 90);
        LockToggle.IsChecked = _settings.OverlayLocked;
        LockValue.Text = _settings.OverlayLocked ? "已锁定" : "未锁定";
        var cal = _calibrationStore.Load();
        BrightThresholdText.Text =
            $"亮：RGB({cal.Bright.RMin}-{cal.Bright.RMax},{cal.Bright.GMin}-{cal.Bright.GMax},{cal.Bright.BMin}-{cal.Bright.BMax})";
        DarkThresholdText.Text =
            $"暗：RGB({cal.Dark.RMin}-{cal.Dark.RMax},{cal.Dark.GMin}-{cal.Dark.GMax},{cal.Dark.BMin}-{cal.Dark.BMax})";
        RoiText.Text =
            $"区域A（左）：{(_settings.LeftRoi != null ? $"{_settings.LeftRoi.X},{_settings.LeftRoi.Y},{_settings.LeftRoi.Width},{_settings.LeftRoi.Height}" : "待框选")}" +
            Environment.NewLine +
            $"区域B（右）：{(_settings.RightRoi != null ? $"{_settings.RightRoi.X},{_settings.RightRoi.Y},{_settings.RightRoi.Width},{_settings.RightRoi.Height}" : "待框选")}";
    }

    // ── 标题栏 ──

    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void BtnMaximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_pipeline is { IsRunning: true })
        {
            var r = MessageBox.Show("识别正在运行，确定要退出吗？", "替身计时器", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }
        _uiTimer.Stop();
        _pipeline?.Dispose();
        _overlay?.Hide();
        base.OnClosing(e);
    }

    // ── 工具栏：设备与识别 ──

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var adb = new AdbCli(AdbCli.FindAdbPath(string.IsNullOrEmpty(_settings.AdbPath) ? null : _settings.AdbPath));
            var devices = await adb.DevicesAsync(CancellationToken.None).ConfigureAwait(true);
            var online = devices.Where(d => d.State == "device").Select(d => d.Serial).ToList();
            if (online.Count == 0)
            {
                DeviceDot.Background = Gray;
                DeviceText.Text = "未发现设备，请先启动雷电";
                Log("刷新设备：未发现在线设备");
            }
            else
            {
                DeviceDot.Background = Green;
                DeviceText.Text = online.Count == 1 ? $"雷电模拟器 {online[0]}" : $"发现 {online.Count} 台设备";
                Log($"刷新设备：发现 {online.Count} 台在线设备");
            }
        }
        catch (Exception ex)
        {
            DeviceDot.Background = Red;
            Log($"刷新设备失败：{ex.Message}");
        }
    }

    private void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        if (_capture == null) return;
        if (_capture.State is CaptureState.Connected or CaptureState.Connecting)
        {
            _pipeline?.Stop();
            _uiTimer.Stop();
            _overlay?.Hide();
            _ = _capture.StopAsync();
            BtnConnect.Content = "连接";
            BtnStart.Content = "开始识别";
            SetStatus("● 未连接", Gray);
            Log("已断开视频流");
        }
        else
        {
            try
            {
                _ = _capture.StartAsync(CancellationToken.None);
                BtnConnect.Content = "断开";
                Log("正在连接视频流…");
            }
            catch (Exception ex)
            {
                Log($"连接失败：{ex.Message}");
            }
        }
    }

    private void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        if (_pipeline == null) return;
        if (_pipeline.IsRunning)
        {
            _pipeline.Stop();
            _uiTimer.Stop();
            _overlay?.Hide();
            BtnStart.Content = "开始识别";
            SetStatus("● 已停止", Gray);
            Log("识别已停止");
        }
        else
        {
            if (_capture is not { State: CaptureState.Connected or CaptureState.Connecting })
            {
                Log("请先点击 [连接] 建立视频流");
                return;
            }
            _pipeline.Start();
            _uiTimer.Start();
            EnsureOverlay().Show();
            BtnStart.Content = "停止识别";
            SetStatus("● 识别运行中", Green);
            Log("识别已开始");
        }
    }

    private void PreviewToggle_Click(object sender, RoutedEventArgs e)
    {
        _previewVisible = PreviewToggle.IsChecked == true;
        UpdatePreviewVisibility();
        Log(_previewVisible ? "预览已开启" : "预览已关闭·识别照跑");
    }

    private void UpdatePreviewVisibility()
    {
        PreviewOffMask.Visibility = _previewVisible ? Visibility.Collapsed : Visibility.Visible;
        PreviewImage.Visibility = _previewVisible && _lastFrame != null ? Visibility.Visible : Visibility.Collapsed;
        PreviewCanvas.Visibility = _previewVisible && _lastFrame != null ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── 工具栏：视图与配置 ──

    private void BtnCalibrate_Click(object sender, RoutedEventArgs e)
    {
        if (_lastFrame == null)
        {
            Log("请先 [连接] 并进入预览后，再使用取色校准");
            return;
        }
        var dialog = new CalibrationDialog(_lastFrame, _calibrationStore, _settings);
        dialog.Owner = this;
        if (dialog.ShowDialog() == true)
        {
            _settingsStore.Save(_settings);
            if (_pipeline?.Recognizer is Recognizer r) r.UpdateOptions(BuildRecognizerOptions());
            ApplySettingsToUi();
            Log("取色校准已保存并生效");
        }
    }

    private void BtnRoi_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new RoiDialog(_settings, _lastFrame, _pipeline?.Recognizer as Recognizer);
        dialog.Owner = this;
        if (dialog.ShowDialog() == true)
        {
            _settingsStore.Save(_settings);
            if (_pipeline?.Recognizer is Recognizer r) r.UpdateOptions(BuildRecognizerOptions());
            ApplySettingsToUi();
            Log("区域配置已保存");
        }
    }

    private void BtnData_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new DataDialog(_dataStore);
        dialog.Owner = this;
        dialog.ShowDialog();
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(_settings);
        dialog.Owner = this;
        if (dialog.ShowDialog() == true)
        {
            _settingsStore.Save(_settings);
            bool wasRunning = _pipeline?.IsRunning == true;
            _pipeline?.Dispose();
            _uiTimer.Stop();
            _overlay?.Hide();
            BuildCore();
            ApplySettingsToUi();
            if (wasRunning)
            {
                _pipeline?.Start();
                _uiTimer.Start();
                EnsureOverlay().Show();
            }
            Log("设置已保存并应用");
        }
    }

    private RecognizerOptions BuildRecognizerOptions()
    {
        var cal = _calibrationStore.Load();
        return new RecognizerOptions(
            cal.Bright, cal.Dark, _settings.LeftRoi, _settings.RightRoi,
            OrangeRange: RecognizerOptions.DefaultOrangeRange,
            DebounceFrames: Math.Max(1, _settings.DebounceFrames),
            LoadingSeconds: Math.Max(0.05, _settings.LoadingSeconds));
    }

    // ── 置顶框 ──

    private OverlayWindow EnsureOverlay()
    {
        if (_overlay == null)
        {
            _overlay = new OverlayWindow();
            _overlay.SetOpacity((int)OpacitySlider.Value);
            _overlay.SetColors(_settings.OverlayLeftColor, _settings.OverlayRightColor);
            _overlay.SetLocked(LockToggle.IsChecked == true);
        }
        return _overlay;
    }

    private void LockToggle_Click(object sender, RoutedEventArgs e)
    {
        bool locked = LockToggle.IsChecked == true;
        _settings.OverlayLocked = locked;
        LockValue.Text = locked ? "已锁定" : "未锁定";
        _overlay?.SetLocked(locked);
        Log(locked ? "置顶框已锁定" : "置顶框未锁定");
    }

    // ── 管线事件（后台线程 → UI）──

    private void OnFrameAvailable(CapturedFrame frame)
    {
        _lastFrame = frame;
        if (!_firstFrameLogged) { _firstFrameLogged = true; Log($"首帧到达 {frame.Width}x{frame.Height} 序列 {frame.Sequence}"); }
        if (_renderScheduled) return;
        _renderScheduled = true;
        Dispatcher.BeginInvoke(() =>
        {
            _renderScheduled = false;
            var latest = _lastFrame;
            if (_previewVisible && latest != null) RenderPreview(latest);
        });
    }

    private void OnRecognitionUpdated(RecognitionOutput output)
    {
        _lastOutput = output;
        Dispatcher.BeginInvoke(() =>
        {
            if (_lastLeftGrid != null && _lastLeftGrid != output.Left.GridCount)
                Log($"回合切换检测：左 {_lastLeftGrid}→{output.Left.GridCount}");
            if (_lastRightGrid != null && _lastRightGrid != output.Right.GridCount)
                Log($"回合切换检测：右 {_lastRightGrid}→{output.Right.GridCount}");
            _lastLeftGrid = output.Left.GridCount;
            _lastRightGrid = output.Right.GridCount;
            UpdateCards();
        });
    }

    private void OnCountdownUpdated(CountdownSnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        Dispatcher.BeginInvoke(() =>
        {
            UpdateCards();
            PreviewOverlay.Visibility = Visibility.Visible;
            PreviewOverlayText.Text = $"{OverlayFormat.FormatSeconds(snapshot.LeftSeconds)}    {OverlayFormat.FormatSeconds(snapshot.RightSeconds)}";
            _overlay?.Update(snapshot);
        });
    }

    private void OnTriggered(TriggerEvent evt)
    {
        Log($"{evt.Side}值 {evt.OldValue}→{evt.NewValue} 触发，置顶框=15.00");
    }

    private void OnCaptureStateChanged(CaptureState state, string? message)
    {
        Dispatcher.BeginInvoke(() =>
        {
            switch (state)
            {
                case CaptureState.Connecting:
                    DeviceDot.Background = Yellow;
                    SetStatus("● 连接中", Yellow);
                    Log($"连接中：{message}" + StderrSuffix());
                    break;
                case CaptureState.Connected:
                    DeviceDot.Background = Green;
                    SetStatus("● 已连接", Green);
                    VideoText.Text = "视频流 H.264";
                    Log((message ?? "视频流已建立") + StderrSuffix());
                    break;
                case CaptureState.Reconnecting:
                    DeviceDot.Background = Yellow;
                    SetStatus("● 重连中", Yellow);
                    Log($"重连中：{message}" + StderrSuffix());
                    break;
                case CaptureState.Disconnected:
                    DeviceDot.Background = Gray;
                    BtnConnect.Content = "连接";
                    SetStatus("● 未连接", Gray);
                    VideoText.Text = "视频流 --";
                    Log("视频流已断开");
                    break;
            }
        });
    }

    private void OnLogged(string msg) => Log(msg);

    private void UpdateCards()
    {
        var o = _lastOutput;
        if (o == null) return;
        RecLeftText.Text = FormatSide("左", o.Left, _lastSnapshot.LeftSeconds, _lastSnapshot.LeftStatus);
        RecRightText.Text = FormatSide("右", o.Right, _lastSnapshot.RightSeconds, _lastSnapshot.RightStatus);
        RoundText.Text = $"回合切换检测：左 {o.Left.GridCount} · 右 {o.Right.GridCount}" + (o.Left.InLoading || o.Right.InLoading ? "（加载中）" : "");
    }

    private static string FormatSide(string label, SideRecognition rec, double seconds, SideStatus status)
    {
        string statusText = status switch
        {
            SideStatus.Counting => "倒计时中",
            SideStatus.Loading => "加载中",
            SideStatus.Lost => "丢失",
            _ => "已归零/正常",
        };
        return $"{label}  格数 {rec.GridCount} · 值 {rec.Value} · 倒计时 {seconds:0.00} · {statusText}";
    }

    // ── 预览绘制 ──

    private void RenderPreview(CapturedFrame frame)
    {
        var bmp = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null);
        bmp.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.Pixels, frame.Width * 4, 0);
        PreviewImage.Source = bmp;
        PreviewImage.Visibility = Visibility.Visible;
        PreviewPlaceholder.Visibility = Visibility.Collapsed;
        PreviewCanvas.Visibility = Visibility.Visible;

        double aw = PreviewImage.ActualWidth, ah = PreviewImage.ActualHeight;
        if (aw <= 0 || ah <= 0) return;
        double scale = Math.Min(aw / frame.Width, ah / frame.Height);
        double ox = (aw - frame.Width * scale) / 2;
        double oy = (ah - frame.Height * scale) / 2;

        PreviewCanvas.Children.Clear();
        var o = _lastOutput;
        DrawRoiOverlay(ox, oy, scale, _settings.LeftRoi, Color.FromRgb(0x2E, 0xCC, 0x71), "左", o?.Left);
        DrawRoiOverlay(ox, oy, scale, _settings.RightRoi, Color.FromRgb(0xE0, 0x3E, 0x3E), "右", o?.Right);
    }

    private void DrawRoiOverlay(double ox, double oy, double scale, RoiConfig? roi, Color border, string label, SideRecognition? rec)
    {
        if (roi == null) return;
        var rect = new System.Windows.Shapes.Rectangle
        {
            Width = roi.Width * scale,
            Height = roi.Height * scale,
            Stroke = new SolidColorBrush(border),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 2 },
        };
        Canvas.SetLeft(rect, ox + roi.X * scale);
        Canvas.SetTop(rect, oy + roi.Y * scale);
        PreviewCanvas.Children.Add(rect);

        var labelTb = new TextBlock
        {
            Text = $"{label} {rec?.Value ?? 0}",
            Foreground = new SolidColorBrush(border),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
        };
        Canvas.SetLeft(labelTb, ox + roi.X * scale);
        Canvas.SetTop(labelTb, Math.Max(0, oy + roi.Y * scale - 18));
        PreviewCanvas.Children.Add(labelTb);

        if (rec?.Centers == null) return;
        double cy = oy + roi.Y * scale + roi.Height * scale / 2;
        for (int i = 0; i < rec.Centers.Count && i < rec.Cells.Count; i++)
        {
            var cellColor = rec.Cells[i] switch
            {
                CellState.Bright => Color.FromRgb(0x4F, 0xC3, 0xF7),
                CellState.Dark => Color.FromRgb(0x1B, 0x2A, 0x4A),
                _ => Color.FromRgb(0x88, 0x88, 0x88),
            };
            if (rec.Value == 4 && rec.Cells[i] == CellState.Bright)
                cellColor = Color.FromRgb(0xFF, 0x8C, 0x3C); // 值=4 突变偏橙红

            var cell = new Border
            {
                Width = Math.Max(6, 14 * scale),
                Height = Math.Max(6, 14 * scale),
                Background = new SolidColorBrush(cellColor),
                CornerRadius = new CornerRadius(3),
            };
            Canvas.SetLeft(cell, ox + (rec.Centers[i] - 7) * scale);
            Canvas.SetTop(cell, cy - 7 * scale);
            PreviewCanvas.Children.Add(cell);
        }
    }

    // ── 状态栏与日志 ──

    private void UiTick()
    {
        _pipeline?.Tick(0.1);
        FpsText.Text = $"FPS {(_capture?.CurrentFps ?? 0):F0}";
        if (++_fpsLogCounter % 50 == 0 && _capture?.State == CaptureState.Connected) Log($"FPS {(_capture?.CurrentFps ?? 0):F0}");
        if (_pipeline != null)
        {
            _lastSnapshot = _pipeline.Engine.GetSnapshot();
            _overlay?.Update(_lastSnapshot);
            UpdateCards();
        }
    }

    private void SetStatus(string text, Brush brush)
    {
        StatusText.Text = text;
        StatusText.Foreground = brush;
    }

    private void Log(string msg)
    {
        try { File.AppendAllText(_logPath, $"{DateTime.Now:HH:mm:ss.fff} {msg}{Environment.NewLine}"); } catch { }
        if (Dispatcher.CheckAccess()) LogText.Text = $"日志：{msg}";
        else Dispatcher.BeginInvoke(() => LogText.Text = $"日志：{msg}");
    }

    private string StderrSuffix()
    {
        var s = _capture?.LastStderrLine;
        return string.IsNullOrWhiteSpace(s) ? "" : $" | stderr: {s}";
    }

    private static Brush Brush(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        return new SolidColorBrush(c);
    }
}