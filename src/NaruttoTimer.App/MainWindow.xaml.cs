using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NaruttoTimer.Capture;
using NaruttoTimer.Data;
using NaruttoTimer.Overlay;
using NaruttoTimer.Recognition;
using NaruttoTimer.Recognition.ImageProcess;
using NaruttoTimer.Rules;
using static NaruttoTimer.App.DialogUi;

namespace NaruttoTimer.App;

/// <summary>主窗口：采集 → AI 识别 → 规则引擎（值判定/空豆判定）→ 置顶框 + 数据保存全链路接线。</summary>
public partial class MainWindow : Window
{
    private static readonly Brush Gray = Brush("#6B7480");
    private static readonly Brush Green = Brush("#2ECC71");
    private static readonly Brush Yellow = Brush("#E6A23C");
    private static readonly Brush Red = Brush("#FF5C5C");
    private static readonly Color DetectionColor = Color.FromRgb(0xFF, 0x8C, 0x3C);

    private readonly string _dataRoot;
    private readonly JsonSettingsStore _settingsStore;
    private readonly DataStore _dataStore;
    private readonly DispatcherTimer _uiTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    private AppSettings _settings;
    private ScreenRecordCaptureSource? _capture;
    private PipelineController? _pipeline;
    private EnergyRecognizer? _recognizer;
    private OverlayWindow? _overlay;
    private CapturedFrame? _lastFrame;
    private EnergyReading? _lastReading;
    private LabelImageConfig _editDraft = LabelImageConfig.CreateDefault();
    private LabelLayoutEditor? _editor;
    private LabelHandle _dragHandle = LabelHandle.None;
    private Point _dragLast;
    private bool _renderScheduled;
    private bool _previewVisible = true;
    private bool _suppressJudgementEvent;
    private double _previewScale = 1;
    private double _previewOffsetX;
    private double _previewOffsetY;
    private WriteableBitmap? _previewBitmap;
    private System.Windows.Shapes.Rectangle? _leftBox;
    private System.Windows.Shapes.Rectangle? _rightBox;
    private TextBlock? _leftLabel;
    private TextBlock? _rightLabel;

    // 识别小点对象池：每帧复用，不再逐帧新建/销毁。
    private readonly List<System.Windows.Shapes.Rectangle> _dotPool = new();
    private int _dotCursor;
    private readonly SolidColorBrush _detectionFill = new(Color.FromArgb(90, DetectionColor.R, DetectionColor.G, DetectionColor.B));
    private readonly SolidColorBrush _detectionStroke = new(DetectionColor);
    private readonly string _logPath;
    private bool _firstFrameLogged;
    private int _fpsLogCounter;
    private int _inferenceCount;
    private DateTime _inferenceWindowStart = DateTime.UtcNow;
    private double _inferenceFps;

    public MainWindow()
    {
        InitializeComponent();

        _dataRoot = Path.Combine(AppContext.BaseDirectory, "data");
        _settingsStore = new JsonSettingsStore(Path.Combine(_dataRoot, "settings.json"));
        _settings = _settingsStore.Load().Normalize();
        _editDraft = _settings.Label.Clone();
        _editor = new LabelLayoutEditor(_editDraft);
        _dataStore = new DataStore(_dataRoot);
        _logPath = Path.Combine(AppContext.BaseDirectory, "logs", "nt-debug.log");
        try { Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!); } catch { }
        Log($"App 启动 {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");

        PreviewCanvas.Background = Brushes.Transparent;
        PreviewCanvas.MouseLeftButtonDown += PreviewCanvas_MouseLeftButtonDown;
        PreviewCanvas.MouseMove += PreviewCanvas_MouseMove;
        PreviewCanvas.MouseLeftButtonUp += PreviewCanvas_MouseLeftButtonUp;

        BuildCore();
        ApplySettingsToUi();

        OpacitySlider.ValueChanged += (_, _) =>
        {
            OpacityValue.Text = $"{(int)OpacitySlider.Value}%";
            _settings.OverlayOpacityPercent = (int)OpacitySlider.Value;
            _overlay?.SetOpacity((int)OpacitySlider.Value);
        };
        TextOpacitySlider.ValueChanged += (_, _) =>
        {
            TextOpacityValue.Text = $"{(int)TextOpacitySlider.Value}%";
            _settings.OverlayTextOpacityPercent = (int)TextOpacitySlider.Value;
            _overlay?.SetTextOpacity((int)TextOpacitySlider.Value);
        };
        _uiTimer.Tick += (_, _) => UiTick();

        PreviewToggle.IsChecked = true;
        UpdatePreviewVisibility();
        InitDeviceCombo();
    }

    // ── 构建 ──

    private void BuildCore()
    {
        var adbPath = AdbCli.FindAdbPath(string.IsNullOrEmpty(_settings.AdbPath) ? null : _settings.AdbPath);
        var ffDir = FfmpegLocator.FindDirectory(string.IsNullOrEmpty(_settings.ScrcpyPath) ? null : _settings.ScrcpyPath);
        _capture = new ScreenRecordCaptureSource(
            new CaptureOptions(adbPath, _settings.DeviceSerial, _settings.VideoWidth, _settings.VideoHeight, _settings.MaxFps),
            ffDir);
        _capture.StateChanged += OnCaptureStateRaw;
        _capture.FrameReady += OnRawFrame;

        _recognizer?.Dispose();
        _recognizer = new EnergyRecognizer(BuildRecognizerOptions(_settings.Label));
        if (_recognizer.LoadWarning != null) Log(_recognizer.LoadWarning);

        var engine = new EnergyRuleEngine(_settings.CountdownSeconds, _settings.StableFrames)
        {
            Judgement = _settings.Judgement,
        };
        _pipeline = new PipelineController(_capture, _recognizer, engine, _dataStore)
        {
            InferenceIntervalMs = _settings.InferenceIntervalMs,
        };
        _pipeline.RecognitionUpdated += OnRecognitionUpdated;
        _pipeline.Triggered += OnTriggered;
        _pipeline.Logged += OnLogged;
        _pipeline.CaptureStateChanged += OnCaptureStateChanged;
    }

    private RecognizerOptions BuildRecognizerOptions(LabelImageConfig label)
    {
        var assets = RecognizerOptions.DefaultAssetDirectory;
        return new RecognizerOptions(
            label,
            Path.Combine(assets, "best.onnx"),
            _settings.ConfidenceThreshold,
            _settings.NmsThreshold);
    }

    private void ApplySettingsToUi()
    {
        DeviceText.Text = $"雷电模拟器 {_settings.DeviceSerial}";
        OpacitySlider.Value = Math.Clamp(_settings.OverlayOpacityPercent, 10, 90);
        OpacityValue.Text = $"{(int)OpacitySlider.Value}%";
        TextOpacitySlider.Value = Math.Clamp(_settings.OverlayTextOpacityPercent, 0, 100);
        TextOpacityValue.Text = $"{(int)TextOpacitySlider.Value}%";
        LockToggle.IsChecked = _settings.OverlayLocked;
        LockValue.Text = _settings.OverlayLocked ? "已锁定" : "未锁定";

        _suppressJudgementEvent = true;
        JudgementCombo.Items.Clear();
        JudgementCombo.Items.Add("值判定");
        JudgementCombo.Items.Add("空豆判定");
        JudgementCombo.SelectedIndex = _settings.Judgement == EnergyJudgement.EmptyCount ? 1 : 0;
        _suppressJudgementEvent = false;

        UpdateParameterTexts();
    }

    private void UpdateParameterTexts()
    {
        ModeText.Text = $"判定方式：{JudgementLabel(_settings.Judgement)}（AI 模式 · 值上限 {EnergyRules.MaxValue}）";
        ParamText.Text = $"倒计时 {_settings.CountdownSeconds:0.##}s ｜ 稳定 {_settings.StableFrames} 帧 ｜ 推理间隔 {_settings.InferenceIntervalMs}ms ｜ 置信度 {_settings.ConfidenceThreshold:0.##} ｜ NMS {_settings.NmsThreshold:0.##}";
        ModelText.Text = _recognizer?.LoadWarning ?? "模型：best.onnx（YOLOv8 · 单类空豆 · 输入 128×128）";
        LabelText.Text =
            $"左能量条：{_settings.Label.EnergyBar.Left}" + Environment.NewLine +
            $"右能量条：{_settings.Label.EnergyBar.Right}";
    }

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
        _recognizer?.Dispose();
        _overlay?.Hide();
        _settingsStore.Save(_settings);
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
            RefreshDeviceCombo(online);
            if (online.Count == 0)
            {
                DeviceDot.Background = Gray;
                DeviceText.Text = "未发现设备，请先启动雷电";
                Log("刷新设备：未发现在线设备");
            }
            else
            {
                DeviceDot.Background = Green;
                DeviceText.Text = $"发现 {online.Count} 台设备";
                Log($"刷新设备：发现 {online.Count} 台在线设备（{string.Join(", ", online)}）");
            }
        }
        catch (Exception ex)
        {
            DeviceDot.Background = Red;
            Log($"刷新设备失败：{ex.Message}");
        }
    }

    private void RefreshDeviceCombo(IReadOnlyList<string> online)
    {
        DeviceCombo.Items.Clear();
        foreach (var serial in online) DeviceCombo.Items.Add(serial);
        var current = _settings.DeviceSerial;
        if (online.Contains(current)) DeviceCombo.SelectedItem = current;
        else if (online.Count > 0) DeviceCombo.SelectedItem = online[0];
    }

    private void InitDeviceCombo()
    {
        DeviceCombo.Items.Clear();
        DeviceCombo.Items.Add(_settings.DeviceSerial);
        DeviceCombo.SelectedItem = _settings.DeviceSerial;
    }

    private void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        if (_capture == null) return;
        var selected = DeviceCombo.SelectedItem as string;
        if (string.IsNullOrEmpty(selected))
        {
            Log("请先 [刷新设备] 并选择一台设备");
            return;
        }
        string current = _capture.DeviceSerial;

        if (_pipeline?.IsRunning == true && selected != current)
        {
            MessageBox.Show(this, "正在识别中，请先点击 [开始识别] 停止后再切换设备。",
                "切换设备", MessageBoxButton.OK, MessageBoxImage.Information);
            Log("切换设备被拦截：正在识别，请先停止识别");
            return;
        }

        if (_capture.State is CaptureState.Connected or CaptureState.Connecting)
        {
            if (selected == current)
            {
                Disconnect();
            }
            else
            {
                SwitchDevice(selected);
            }
        }
        else
        {
            if (selected == current)
            {
                _ = _capture.StartAsync(CancellationToken.None);
                BtnConnect.Content = "断开";
                Log("正在连接视频流…");
            }
            else
            {
                SwitchDevice(selected);
            }
        }
    }

    private void Disconnect()
    {
        _pipeline?.Stop();
        _uiTimer.Stop();
        _overlay?.Hide();
        _ = _capture?.StopAsync() ?? Task.CompletedTask;
        BtnConnect.Content = "连接";
        BtnStart.Content = "开始识别";
        SetStatus("● 未连接", Gray);
        Log("已断开视频流");
    }

    private void SwitchDevice(string serial)
    {
        try
        {
            _pipeline?.Stop();
            _uiTimer.Stop();
            _overlay?.Hide();
            _ = _capture?.StopAsync() ?? Task.CompletedTask;
            if (_capture != null)
            {
                _capture.StateChanged -= OnCaptureStateRaw;
                _capture.FrameReady -= OnRawFrame;
            }
            _settings.DeviceSerial = serial;
            _settingsStore.Save(_settings);
            _pipeline?.Dispose();
            BuildCore();
            ApplySettingsToUi();
            _ = _capture!.StartAsync(CancellationToken.None);
            BtnConnect.Content = "断开";
            Log($"已切换到设备 {serial}");
        }
        catch (Exception ex)
        {
            Log($"切换设备失败：{ex.Message}");
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
            if (_recognizer?.IsModelReady != true)
            {
                Log($"AI 模型不可用，无法开始识别：{_recognizer?.LoadWarning ?? "未加载 best.onnx"}");
                MessageBox.Show(this, $"AI 模型不可用，无法开始识别。{Environment.NewLine}{_recognizer?.LoadWarning ?? "请确认 assets/best.onnx 存在。"}", "识别", MessageBoxButton.OK, MessageBoxImage.Warning);
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

    private void JudgementCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressJudgementEvent || JudgementCombo.SelectedIndex < 0) return;
        var judgement = JudgementCombo.SelectedIndex == 1 ? EnergyJudgement.EmptyCount : EnergyJudgement.Value;
        if (_settings.Judgement == judgement) return;
        _settings.Judgement = judgement;
        _settingsStore.Save(_settings);
        ApplyJudgementToEngine();
        UpdateParameterTexts();
        Log($"判定方式已切换为 {JudgementLabel(judgement)}（稳定值与倒计时已重置）");
    }

    /// <summary>把判定方式应用到规则引擎，并重置稳定值/倒计时，避免切换瞬间跳变误触发。</summary>
    private void ApplyJudgementToEngine()
    {
        if (_pipeline == null) return;
        _pipeline.Engine.Judgement = _settings.Judgement;
        _pipeline.Engine.Reset();
    }

    private static string JudgementLabel(EnergyJudgement judgement) =>
        judgement == EnergyJudgement.EmptyCount ? "空豆判定" : "值判定";

    private void PreviewToggle_Click(object sender, RoutedEventArgs e)
    {
        _previewVisible = PreviewToggle.IsChecked == true;
        UpdatePreviewVisibility();
        Log(_previewVisible ? "预览已开启" : "预览已关闭／仅识别");
    }

    private void UpdatePreviewVisibility()
    {
        PreviewOffMask.Visibility = _previewVisible ? Visibility.Collapsed : Visibility.Visible;
        PreviewImage.Visibility = _previewVisible && _lastFrame != null ? Visibility.Visible : Visibility.Collapsed;
        PreviewCanvas.Visibility = _previewVisible && _lastFrame != null ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── 工具栏：视图与配置 ──

    private void BtnLabelConfig_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new LabelConfigDialog(_settings.Label.Clone(), _lastFrame, RunLabelTest)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            _settings.Label = dialog.Result;
            _editDraft = _settings.Label.Clone();
            _editor = new LabelLayoutEditor(_editDraft);
            _settingsStore.Save(_settings);
            _recognizer?.UpdateOptions(BuildRecognizerOptions(_settings.Label));
            UpdateParameterTexts();
            RenderCurrent();
            Log("区域标注已保存");
        }
    }

    private string RunLabelTest(LabelImageConfig config)
    {
        if (_lastFrame == null) return "暂无帧，请先 [连接] 并等待预览";
        if (_recognizer == null) return "识别器不可用";
        try
        {
            using var mat = MatUtil.FromBgra(_lastFrame);
            var reading = _recognizer.TestRecognize(BuildRecognizerOptions(config), mat);
            return $"左 空豆 {reading.LeftEmptyCount} → 值 {reading.LeftValue} ｜ 右 空豆 {reading.RightEmptyCount} → 值 {reading.RightValue}";
        }
        catch (Exception ex)
        {
            return $"测试失败：{ex.Message}";
        }
    }

    private void BtnDumpFrame_Click(object sender, RoutedEventArgs e)
    {
        if (_lastFrame == null)
        {
            Log("请先 [连接] 并进入预览后，再导出识别帧");
            return;
        }
        try
        {
            var f = _lastFrame;
            // 帧缓冲会被解码线程轮转复用，导出前先快照，避免编码过程中被覆写。
            var pixels = (byte[])f.Pixels.Clone();
            var dir = Path.Combine(_dataRoot, "debug");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"frame-{DateTime.Now:HHmmssfff}.png");
            int stride = f.Width * 4;
            var bmp = BitmapSource.Create(f.Width, f.Height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var fs = File.Create(path)) enc.Save(fs);
            Log("已导出识别帧：" + path);
        }
        catch (Exception ex)
        {
            Log("导出识别帧失败：" + ex.Message);
        }
    }

    private void BtnData_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new DataDialog(_dataStore) { Owner = this };
        dialog.ShowDialog();
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(_settings) { Owner = this };
        if (dialog.ShowDialog() != true) return;

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

    // ── 置顶框 ──

    private OverlayWindow EnsureOverlay()
    {
        if (_overlay == null)
        {
            _overlay = new OverlayWindow();
            _overlay.SetOpacity((int)OpacitySlider.Value);
            _overlay.SetTextOpacity((int)TextOpacitySlider.Value);
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

    // ── 管线事件（后台线程 → UI） ──

    private void OnCaptureStateRaw(object? sender, CaptureStateChangedEventArgs e) => OnCaptureStateChanged(e.State, e.Message);

    private void OnRawFrame(object? sender, CapturedFrame frame) => OnFrameAvailable(frame);

    private void OnFrameAvailable(CapturedFrame frame)
    {
        _lastFrame = frame;
        if (!_firstFrameLogged)
        {
            _firstFrameLogged = true;
            Log($"首帧到达 {frame.Width}x{frame.Height} 序列 {frame.Sequence}");
        }
        if (_renderScheduled) return;
        _renderScheduled = true;
        Dispatcher.BeginInvoke(() =>
        {
            _renderScheduled = false;
            RenderCurrent();
        });
    }

    private void OnRecognitionUpdated(EnergyReading reading)
    {
        _lastReading = reading;
        Interlocked.Increment(ref _inferenceCount);
        Dispatcher.BeginInvoke(() =>
        {
            UpdateCards();
            if (_previewVisible) RenderCurrent();
        });
    }

    private void OnTriggered(TriggerEvent evt)
    {
        Log($"{(evt.Side == Side.Left ? "左" : "右")} · {JudgementLabel(_settings.Judgement)} {evt.OldValue}→{evt.NewValue} 触发，置顶框={_settings.CountdownSeconds:0.00}");
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
                    VideoText.Text = $"视频流 {_settings.VideoWidth}x{_settings.VideoHeight} H.264";
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
        var snapshot = _pipeline?.GetSnapshot() ?? CountdownSnapshot.Zero;
        var reading = _lastReading;
        RecLeftText.Text = FormatReading("左", reading?.LeftValue, reading?.LeftEmptyCount, snapshot.LeftSeconds);
        RecRightText.Text = FormatReading("右", reading?.RightValue, reading?.RightEmptyCount, snapshot.RightSeconds);
        _overlay?.Update(snapshot);
    }

    /// <summary>按当前判定方式决定主显示：值判定突出「值」，空豆判定突出「空豆」。</summary>
    private string FormatReading(string side, int? value, int? empty, double seconds)
    {
        string v = value?.ToString() ?? "--";
        string e = empty?.ToString() ?? "--";
        string main = _settings.Judgement == EnergyJudgement.EmptyCount ? $"空豆 {e}" : $"值 {v}";
        string alt = _settings.Judgement == EnergyJudgement.EmptyCount ? $"值 {v}" : $"空豆 {e}";
        return $"{side}  {main}（{alt}）· 倒计时 {seconds:0.00}";
    }

    // ── 预览绘制 ──

    private void RenderCurrent()
    {
        var frame = _lastFrame;
        if (frame == null)
        {
            PreviewPlaceholder.Visibility = Visibility.Visible;
            return;
        }
        if (!_previewVisible) return;

        // 复用同一块 WriteableBitmap（仅尺寸变化时重建），避免每帧新建 3.7MB 位图。
        if (_previewBitmap == null || _previewBitmap.PixelWidth != frame.Width || _previewBitmap.PixelHeight != frame.Height)
        {
            _previewBitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null);
            PreviewImage.Source = _previewBitmap;
        }
        _previewBitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.Pixels, frame.Width * 4, 0);
        PreviewImage.Visibility = Visibility.Visible;
        PreviewPlaceholder.Visibility = Visibility.Collapsed;
        PreviewCanvas.Visibility = Visibility.Visible;

        double aw = PreviewImage.ActualWidth, ah = PreviewImage.ActualHeight;
        if (aw <= 0 || ah <= 0) return;
        _previewScale = Math.Min(aw / frame.Width, ah / frame.Height);
        var origin = PreviewImage.TranslatePoint(new Point(0, 0), PreviewCanvas);
        _previewOffsetX = origin.X + (aw - frame.Width * _previewScale) / 2;
        _previewOffsetY = origin.Y + (ah - frame.Height * _previewScale) / 2;

        // 复用画布对象：框/标签/识别小点只建一次，之后仅更新位置与文本。
        EnsurePreviewVisuals();
        var layout = _editDraft;
        _dotCursor = 0;
        UpdateLabelRect(_leftBox!, _leftLabel!, layout.EnergyBar.Left,
            PreviewLabel("左", _lastReading?.LeftValue, _lastReading?.LeftEmptyCount), _lastReading?.LeftDetections);
        UpdateLabelRect(_rightBox!, _rightLabel!, layout.EnergyBar.Right,
            PreviewLabel("右", _lastReading?.RightValue, _lastReading?.RightEmptyCount), _lastReading?.RightDetections);
        HideUnusedDots();
    }

    private string PreviewLabel(string side, int? value, int? empty)
    {
        if (value == null || empty == null) return $"{side} -";
        return _settings.Judgement == EnergyJudgement.EmptyCount
            ? $"{side} 空豆 {empty}（值 {value}）"
            : $"{side} 值 {value}（空豆 {empty}）";
    }

    /// <summary>首次渲染时创建画布对象（框、标签）；小点按需插入到标签之前。</summary>
    private void EnsurePreviewVisuals()
    {
        if (_leftBox != null) return;
        _leftBox = NewRoiBox(LeftBarColor);
        _rightBox = NewRoiBox(RightBarColor);
        _leftLabel = NewRoiLabel(LeftBarColor);
        _rightLabel = NewRoiLabel(RightBarColor);
        PreviewCanvas.Children.Add(_leftBox);
        PreviewCanvas.Children.Add(_rightBox);
        PreviewCanvas.Children.Add(_leftLabel);
        PreviewCanvas.Children.Add(_rightLabel);
    }

    private static System.Windows.Shapes.Rectangle NewRoiBox(Color color) => new()
    {
        Stroke = new SolidColorBrush(color),
        StrokeThickness = 2,
        StrokeDashArray = new DoubleCollection { 4, 2 },
        Fill = Brushes.Transparent,
        IsHitTestVisible = false,
    };

    private static TextBlock NewRoiLabel(Color color) => new()
    {
        Foreground = new SolidColorBrush(color),
        FontSize = 13,
        FontWeight = FontWeights.Bold,
        IsHitTestVisible = false,
    };

    /// <summary>更新单个 AB 框：位置/尺寸/标签文本/识别小点（复用已有对象）。</summary>
    private void UpdateLabelRect(System.Windows.Shapes.Rectangle box, TextBlock label, LabelRect rect, string text, IReadOnlyList<EnergyDetection>? detections)
    {
        if (rect == null)
        {
            box.Visibility = Visibility.Collapsed;
            label.Visibility = Visibility.Collapsed;
            return;
        }
        double fw = _lastFrame!.Width, fh = _lastFrame.Height;
        double x = _previewOffsetX + rect.LeftTop.X * fw * _previewScale;
        double y = _previewOffsetY + rect.LeftTop.Y * fh * _previewScale;
        double w = Math.Max(1, rect.Width * fw * _previewScale);
        double h = Math.Max(1, rect.Height * fh * _previewScale);

        box.Visibility = Visibility.Visible;
        box.Width = w;
        box.Height = h;
        Canvas.SetLeft(box, x);
        Canvas.SetTop(box, y);

        label.Visibility = Visibility.Visible;
        label.Text = text;
        Canvas.SetLeft(label, x);
        Canvas.SetTop(label, Math.Max(0, y - 18));

        if (detections == null) return;
        foreach (var d in detections)
        {
            var dot = RentDot();
            dot.Visibility = Visibility.Visible;
            dot.Width = Math.Max(3, d.Width * w);
            dot.Height = Math.Max(3, d.Height * h);
            Canvas.SetLeft(dot, x + d.X * w);
            Canvas.SetTop(dot, y + d.Y * h);
        }
    }

    /// <summary>取一个复用的识别小点；数量不足时创建并插到标签之前，保证标签在最上层。</summary>
    private System.Windows.Shapes.Rectangle RentDot()
    {
        if (_dotCursor < _dotPool.Count) return _dotPool[_dotCursor++];

        var dot = new System.Windows.Shapes.Rectangle
        {
            Fill = _detectionFill,
            Stroke = _detectionStroke,
            StrokeThickness = 1,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        _dotPool.Add(dot);
        PreviewCanvas.Children.Insert(2 + _dotPool.Count - 1, dot);
        _dotCursor++;
        return dot;
    }

    private void HideUnusedDots()
    {
        for (int i = _dotCursor; i < _dotPool.Count; i++)
            if (_dotPool[i].Visibility != Visibility.Collapsed) _dotPool[i].Visibility = Visibility.Collapsed;
    }

    // ── 主界面预览区拖拽调整标注 ──

    private void PreviewCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_lastFrame == null || !_previewVisible || _editor == null) return;
        var p = e.GetPosition(PreviewCanvas);
        ToFramePixel(p, out double fx, out double fy);
        _dragHandle = _editor.HitTest(fx, fy, _lastFrame.Width, _lastFrame.Height);
        if (_dragHandle == LabelHandle.None) return;
        _dragLast = p;
        PreviewCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void PreviewCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragHandle == LabelHandle.None || _lastFrame == null || _editor == null) return;
        var p = e.GetPosition(PreviewCanvas);
        double dx = (p.X - _dragLast.X) / _previewScale;
        double dy = (p.Y - _dragLast.Y) / _previewScale;
        _dragLast = p;
        _editor.Drag(_dragHandle, dx, dy, _lastFrame.Width, _lastFrame.Height);
        RenderCurrent();
    }

    private void PreviewCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragHandle == LabelHandle.None) return;
        _dragHandle = LabelHandle.None;
        PreviewCanvas.ReleaseMouseCapture();
        ApplyEditedLabel();
    }

    /// <summary>把预览区拖拽结果落到设置并生效。</summary>
    private void ApplyEditedLabel()
    {
        if (_editor == null) return;
        var error = _editDraft.Validate();
        if (error != null)
        {
            Log($"区域标注无效（{error}），已撤销本次拖拽");
            _editDraft = _settings.Label.Clone();
            _editor = new LabelLayoutEditor(_editDraft);
            RenderCurrent();
            return;
        }
        _settings.Label = _editDraft;
        _settingsStore.Save(_settings);
        _recognizer?.UpdateOptions(BuildRecognizerOptions(_settings.Label));
        UpdateParameterTexts();
        Log("区域标注已更新（预览区拖拽）");
    }

    private void ToFramePixel(Point canvasPoint, out double fx, out double fy)
    {
        fx = (canvasPoint.X - _previewOffsetX) / _previewScale;
        fy = (canvasPoint.Y - _previewOffsetY) / _previewScale;
    }

    // ── 状态栏与日志 ──

    private void UiTick()
    {
        FpsText.Text = $"FPS {(_capture?.CurrentFps ?? 0):F0}";
        var now = DateTime.UtcNow;
        if ((now - _inferenceWindowStart).TotalSeconds >= 1.0)
        {
            double seconds = (now - _inferenceWindowStart).TotalSeconds;
            _inferenceFps = Interlocked.Exchange(ref _inferenceCount, 0) / seconds;
            _inferenceWindowStart = now;
            InferText.Text = $"推理 {_inferenceFps:F1}/s";
        }
        if (++_fpsLogCounter % 50 == 0 && _capture?.State == CaptureState.Connected)
            Log($"FPS {(_capture?.CurrentFps ?? 0):F0} ｜ 推理 {_inferenceFps:F1}/s");
        UpdateCards();
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
