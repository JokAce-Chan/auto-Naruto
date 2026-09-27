using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private readonly DispatcherTimer _arenaTrackerTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    private AppSettings _settings;
    private ScreenRecordCaptureSource? _capture;
    private PipelineController? _pipeline;
    private EnergyRecognizer? _recognizer;
    private OverlayWindow? _overlay;
    private ArenaOverlayWindow? _arenaOverlay;
    private IntPtr _arenaRenderHandle;
    private bool _suppressArenaUi;
    private bool _arenaUiReady;
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

    // ── 右侧选项栏（主界面验收稿 v6：分组 / 拖宽 / 折叠 / 图标设置）──
    private const int SideMinWidth = 250;
    private const int SideMaxWidth = 550;
    private const int SideDefaultWidth = 300;
    private const int SideRailWidth = 58;
    private static readonly FontFamily IconFont = new("Segoe MDL2 Assets");
    private static readonly Brush ChipBg = Brush("#1F242C");
    private static readonly Brush ChipBorder = Brush("#2A2F38");
    private static readonly Brush ChipFg = Brush("#9AA3B0");
    private static readonly Brush ChipSelBg = Brush("#1D2A40");
    private static readonly Brush ChipSelBorder = Brush("#2E6FD0");
    private static readonly Brush ChipSelFg = Brush("#DDE2EA");
    private readonly Dictionary<string, ToggleButton> _groupHeads = new();
    private string _iconTargetKey = UiIcons.GroupTimer;
    private bool _sideResizing;
    private double _sideResizeStartX;
    private double _sideResizeStartWidth;

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
        ApplyUiLayout();

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
        _arenaTrackerTimer.Tick += (_, _) => UpdateArenaOverlay();

        PreviewToggle.IsChecked = true;
        UpdatePreviewVisibility();
        InitDeviceCombo();
        _arenaUiReady = true;
        StartArenaTrackingIfEnabled();
    }

    /// <summary>关闭主窗口时收尾：停识别/采集（否则 adb screenrecord 子进程会残留，最长占用设备 180s）并关闭置顶框。</summary>
    protected override void OnClosed(EventArgs e)
    {
        try { _uiTimer.Stop(); } catch { }
        try { _arenaTrackerTimer.Stop(); } catch { }
        try { _pipeline?.Stop(); } catch { }
        try { _capture?.StopAsync().Wait(TimeSpan.FromSeconds(2)); } catch { }
        try { _pipeline?.Dispose(); } catch { }
        try { _overlay?.Close(); } catch { }
        try { _arenaOverlay?.Close(); } catch { }
        base.OnClosed(e);
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

        ApplyArenaSettingsToUi();
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

    // ── 右侧选项栏：分组 / 拖宽 / 折叠 / 图标设置 ──

    /// <summary>应用右侧栏的全部记忆项（分组开合、折叠态、图标选择）。</summary>
    private void ApplyUiLayout()
    {
        ApplySidePanelState();
        ApplyUiIcons();
        BuildIconSettings();
    }

    private void ApplySidePanelState()
    {
        _settings.SidePanelWidth = Math.Clamp(_settings.SidePanelWidth, SideMinWidth, SideMaxWidth);
        _groupHeads.Clear();
        _groupHeads[UiIcons.GroupTimerName] = HeadTimer;
        _groupHeads[UiIcons.GroupArenaName] = HeadArena;
        _groupHeads[UiIcons.GroupDebugName] = HeadDebug;
        _groupHeads[UiIcons.GroupIconsName] = HeadIcons;

        // 首次运行：仅「替身计时」展开，其余折叠
        foreach (var (key, head) in _groupHeads)
        {
            bool open = _settings.GroupOpen.TryGetValue(key, out var saved) ? saved : key == UiIcons.GroupTimerName;
            head.IsChecked = open;
        }

        ShowSideCollapsed(_settings.SidePanelCollapsed);
        SideColumn.Width = new GridLength(_settings.SidePanelCollapsed ? SideRailWidth : _settings.SidePanelWidth);
    }

    private void ShowSideCollapsed(bool collapsed)
    {
        SideExpanded.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        SideRail.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
    }

    private void GroupHead_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton head || head.Tag is not string key) return;
        _settings.GroupOpen[key] = head.IsChecked == true;
        SaveUiMemory();
    }

    /// <summary>点图标条上的组图标：展开侧栏并只开这一组。</summary>
    private void RailButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string key) return;
        foreach (var (groupKey, head) in _groupHeads)
        {
            head.IsChecked = groupKey == key;
            _settings.GroupOpen[groupKey] = head.IsChecked == true;
        }
        SetSideCollapsed(false);
    }

    private void BtnCollapseSide_Click(object sender, RoutedEventArgs e) => SetSideCollapsed(true);

    private void BtnExpandSide_Click(object sender, RoutedEventArgs e) => SetSideCollapsed(false);

    private void SetSideCollapsed(bool collapsed)
    {
        _settings.SidePanelCollapsed = collapsed;
        ShowSideCollapsed(collapsed);
        SideColumn.Width = new GridLength(collapsed ? SideRailWidth : _settings.SidePanelWidth);
        SaveUiMemory();
    }

    // ── 栏宽拖拽（250–550，双击复位 300）──

    private void SideResizer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            _settings.SidePanelWidth = SideDefaultWidth;
            SideColumn.Width = new GridLength(SideDefaultWidth);
            SaveUiMemory();
            return;
        }
        _sideResizing = true;
        _sideResizeStartX = e.GetPosition(this).X;
        _sideResizeStartWidth = SideColumn.ActualWidth;
        SideResizer.CaptureMouse();
        e.Handled = true;
    }

    private void SideResizer_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_sideResizing) return;
        double delta = e.GetPosition(this).X - _sideResizeStartX;
        double width = Math.Clamp(_sideResizeStartWidth - delta, SideMinWidth, SideMaxWidth);
        SideColumn.Width = new GridLength(Math.Round(width));
    }

    private void SideResizer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_sideResizing) return;
        _sideResizing = false;
        SideResizer.ReleaseMouseCapture();
        _settings.SidePanelWidth = Math.Clamp((int)Math.Round(SideColumn.ActualWidth), SideMinWidth, SideMaxWidth);
        SaveUiMemory();
    }

    // ── 图标设置 ──

    private void ApplyUiIcons()
    {
        SetGlyph(GlyphGroupTimer, UiIcons.GroupTimer);
        SetGlyph(GlyphGroupArena, UiIcons.GroupArena);
        SetGlyph(GlyphGroupDebug, UiIcons.GroupDebug);
        SetGlyph(GlyphGroupIcons, UiIcons.GroupIcons);
        SetGlyph(GlyphPinnedSettings, UiIcons.PinnedSettings);
        SetGlyph(GlyphItemStatus, UiIcons.ItemStatus);
        SetGlyph(GlyphItemPin, UiIcons.ItemPin);
        SetGlyph(GlyphItemRoi, UiIcons.ItemRoi);
        SetGlyph(GlyphItemRoi2, UiIcons.ItemRoi);
        SetGlyph(GlyphItemParams, UiIcons.ItemParams);
        SetGlyph(GlyphItemRules, UiIcons.ItemRules);
        SetGlyph(GlyphItemScope, UiIcons.ItemScope);
        SetGlyph(GlyphItemFrame, UiIcons.ItemFrame);
        SetGlyph(GlyphItemData, UiIcons.ItemData);

        // 折叠条与组头共用同一字形
        RailTimer.Content = UiIcons.Resolve(_settings.UiIcons, UiIcons.GroupTimer);
        RailArena.Content = UiIcons.Resolve(_settings.UiIcons, UiIcons.GroupArena);
        RailDebug.Content = UiIcons.Resolve(_settings.UiIcons, UiIcons.GroupDebug);
        RailIcons.Content = UiIcons.Resolve(_settings.UiIcons, UiIcons.GroupIcons);
        RailSettings.Content = UiIcons.Resolve(_settings.UiIcons, UiIcons.PinnedSettings);
    }

    private void SetGlyph(TextBlock target, string key) => target.Text = UiIcons.Resolve(_settings.UiIcons, key);

    /// <summary>构建「图标设置」组：目标 chip（组头 / 组内条目）+ 候选字形网格。</summary>
    private void BuildIconSettings()
    {
        IconTargetsHead.Children.Clear();
        IconTargetsItem.Children.Clear();
        IconPalette.Children.Clear();

        foreach (var target in UiIcons.Targets)
        {
            var chip = new Button
            {
                Style = (Style)FindResource("IconChip"),
                Tag = target.Key,
                ToolTip = $"{target.Section}：{target.Name}",
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock
            {
                Text = UiIcons.Resolve(_settings.UiIcons, target.Key),
                FontFamily = IconFont,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
            });
            row.Children.Add(new TextBlock
            {
                Text = target.Name,
                Margin = new Thickness(5, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            chip.Content = row;
            chip.Click += IconTarget_Click;
            (target.Section == UiIcons.SectionHead ? IconTargetsHead : IconTargetsItem).Children.Add(chip);
        }

        foreach (var candidate in UiIcons.Candidates)
        {
            var cell = new Button
            {
                Style = (Style)FindResource("IconCell"),
                Tag = candidate.Glyph,
                ToolTip = $"{candidate.Name}（U+{candidate.Glyph[0]:X4}）",
            };
            var column = new StackPanel();
            column.Children.Add(new TextBlock
            {
                Text = candidate.Glyph,
                FontFamily = IconFont,
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            column.Children.Add(new TextBlock
            {
                Text = candidate.Name,
                FontSize = 9,
                Foreground = Gray,
                Margin = new Thickness(0, 3, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            cell.Content = column;
            cell.Click += IconPalette_Click;
            IconPalette.Children.Add(cell);
        }

        RefreshIconSelection();
    }

    private void IconTarget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button chip || chip.Tag is not string key) return;
        _iconTargetKey = key;
        RefreshIconSelection();
    }
    private void IconPalette_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button cell || cell.Tag is not string glyph) return;
        _settings.UiIcons[_iconTargetKey] = glyph;
        ApplyUiIcons();
        SaveUiMemory();
        RefreshIconSelection();
    }

    /// <summary>刷新「图标设置」的选中高亮与当前字形。</summary>
    private void RefreshIconSelection()
    {
        foreach (var chip in IconTargetsHead.Children.OfType<Button>().Concat(IconTargetsItem.Children.OfType<Button>()))
        {
            if (chip.Tag is not string key) continue;
            bool selected = key == _iconTargetKey;
            chip.Background = selected ? ChipSelBg : ChipBg;
            chip.BorderBrush = selected ? ChipSelBorder : ChipBorder;
            chip.Foreground = selected ? ChipSelFg : ChipFg;
            if (chip.Content is StackPanel row && row.Children.Count > 0 && row.Children[0] is TextBlock glyphText)
                glyphText.Text = UiIcons.Resolve(_settings.UiIcons, key);
        }

        string current = UiIcons.Resolve(_settings.UiIcons, _iconTargetKey);
        foreach (var cell in IconPalette.Children.OfType<Button>())
        {
            bool selected = cell.Tag is string glyph && glyph == current;
            cell.Background = selected ? ChipSelBg : ChipBg;
            cell.BorderBrush = selected ? ChipSelBorder : ChipBorder;
        }
    }

    private void SaveUiMemory() => _settingsStore.Save(_settings);

    // ── 决斗场范围显示：绑定 RenderWindow、参考图和自定义横线 ──

    private void ApplyArenaSettingsToUi()
    {
        _suppressArenaUi = true;
        try
        {
            ArenaOverlaySettings arena = _settings.ArenaOverlay;
            ArenaEnabledToggle.IsChecked = arena.Enabled;
            ArenaStatusText.Text = arena.Enabled ? "等待定位" : "未启用";
            ArenaInstanceCombo.SelectedIndex = Math.Clamp(arena.EmulatorInstance, 0, ArenaInstanceCombo.Items.Count - 1);
            ArenaContentCombo.SelectedIndex = string.Equals(arena.ContentMode, "lines", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            ArenaImageOpacitySlider.Value = arena.ImageOpacityPercent;
            ArenaImageOpacityValue.Text = $"{arena.ImageOpacityPercent}%";
            bool lineEditing = string.Equals(arena.ContentMode, "lines", StringComparison.OrdinalIgnoreCase);
            ArenaEditToggle.IsEnabled = arena.Enabled && lineEditing;
            ArenaEditToggle.IsChecked = arena.Enabled && lineEditing && arena.EditMode;
            ArenaEditValue.Text = ArenaEditToggle.IsChecked == true ? "可拖动横线" : "鼠标穿透";
            ArenaRefreshLineList();
        }
        finally
        {
            _suppressArenaUi = false;
        }
    }

    private void ArenaRefreshLineList(int? selectedIndex = null)
    {
        int targetIndex = selectedIndex ?? ArenaLinesList.SelectedIndex;
        bool previous = _suppressArenaUi;
        _suppressArenaUi = true;
        try
        {
            ArenaLinesList.ItemsSource = null;
            ArenaLinesList.ItemsSource = _settings.ArenaOverlay.Lines;
            if (_settings.ArenaOverlay.Lines.Count > 0)
            {
                ArenaLinesList.SelectedIndex = Math.Clamp(targetIndex, 0, _settings.ArenaOverlay.Lines.Count - 1);
            }
            ApplyArenaSelectedLineToUi();
        }
        finally
        {
            _suppressArenaUi = previous;
        }
    }

    private ArenaRangeLine? SelectedArenaLine()
    {
        int index = ArenaLinesList.SelectedIndex;
        return index >= 0 && index < _settings.ArenaOverlay.Lines.Count
            ? _settings.ArenaOverlay.Lines[index]
            : null;
    }

    private void ApplyArenaSelectedLineToUi()
    {
        ArenaRangeLine? line = SelectedArenaLine();
        ArenaLineEnabledToggle.IsEnabled = line != null;
        ArenaLineYSlider.IsEnabled = line != null;
        ArenaLineThicknessSlider.IsEnabled = line != null;
        ArenaLineOpacitySlider.IsEnabled = line != null;
        ArenaLineColorBox.IsEnabled = line != null;
        if (line == null) return;

        ArenaLineEnabledToggle.IsChecked = line.Enabled;
        ArenaLineYSlider.Value = Math.Clamp(line.Y * 100.0, 0, 100);
        ArenaLineYValue.Text = $"{line.Y * 100.0:0.#}%";
        ArenaLineThicknessSlider.Value = Math.Clamp(line.Thickness, 1, 20);
        ArenaLineThicknessValue.Text = $"{line.Thickness:0.#} px";
        ArenaLineOpacitySlider.Value = line.OpacityPercent;
        ArenaLineOpacityValue.Text = $"{line.OpacityPercent}%";
        ArenaLineColorBox.Text = line.Color;
    }

    private ArenaOverlayWindow EnsureArenaOverlay()
    {
        if (_arenaOverlay != null) return _arenaOverlay;
        _arenaOverlay = new ArenaOverlayWindow();
        _arenaOverlay.LineChanged += ArenaOverlay_LineChanged;
        _arenaOverlay.LineEditCompleted += ArenaOverlay_LineEditCompleted;
        ApplyArenaConfigurationToOverlay(_arenaOverlay);
        return _arenaOverlay;
    }

    private void ApplyArenaConfigurationToOverlay(ArenaOverlayWindow overlay)
    {
        ArenaOverlaySettings arena = _settings.ArenaOverlay;
        overlay.SetSettings(arena);
        overlay.SetReferenceImage(ArenaReferenceImagePath());
        overlay.SetEditMode(arena.Enabled && arena.EditMode && string.Equals(arena.ContentMode, "lines", StringComparison.OrdinalIgnoreCase));
    }

    private string ArenaReferenceImagePath()
    {
        return Path.Combine(AppContext.BaseDirectory, "assets", "arena", "X轴范围显示.png");
    }

    private void StartArenaTrackingIfEnabled()
    {
        if (!_settings.ArenaOverlay.Enabled)
        {
            _arenaTrackerTimer.Stop();
            _arenaOverlay?.HideOverlay();
            return;
        }
        EnsureArenaOverlay();
        _arenaTrackerTimer.Start();
        UpdateArenaOverlay();
    }

    private void UpdateArenaOverlay()
    {
        if (!_settings.ArenaOverlay.Enabled)
        {
            _arenaOverlay?.HideOverlay();
            return;
        }

        ArenaOverlayWindow overlay = EnsureArenaOverlay();
        EmulatorWindowBounds bounds = default;
        bool found = _arenaRenderHandle != IntPtr.Zero && EmulatorWindowTracker.TryGetBounds(_arenaRenderHandle, out bounds);
        if (!found)
        {
            _arenaRenderHandle = IntPtr.Zero;
            found = EmulatorWindowTracker.TryFindRenderWindow(
                _settings.ArenaOverlay.EmulatorInstance,
                string.IsNullOrWhiteSpace(_settings.AdbPath) ? null : _settings.AdbPath,
                out bounds);
        }

        if (!found)
        {
            ArenaStatusText.Text = $"实例 {_settings.ArenaOverlay.EmulatorInstance} 未运行";
            overlay.HideOverlay();
            return;
        }

        _arenaRenderHandle = bounds.Handle;
        overlay.SetBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        overlay.ShowOverlay();
        ArenaStatusText.Text = $"已绑定 {bounds.Width}×{bounds.Height}";
    }

    private void ArenaOverlay_LineChanged(object? sender, ArenaLineChangedEventArgs e)
    {
        ArenaRangeLine? line = _settings.ArenaOverlay.Lines.FirstOrDefault(item => item.Id == e.LineId);
        if (line == null) return;
        bool previous = _suppressArenaUi;
        _suppressArenaUi = true;
        ArenaLineYSlider.Value = Math.Clamp(e.Y * 100.0, 0, 100);
        ArenaLineYValue.Text = $"{e.Y * 100.0:0.#}%";
        ArenaLinesList.Items.Refresh();
        _suppressArenaUi = previous;
    }

    private void ArenaOverlay_LineEditCompleted(object? sender, EventArgs e)
    {
        SaveArenaSettings();
        Log("决斗场横线位置已更新");
    }

    private void SaveArenaSettings()
    {
        _settings.ArenaOverlay.Normalize();
        _settingsStore.Save(_settings);
    }

    private void ArenaEnabledToggle_Click(object sender, RoutedEventArgs e)
    {
        if (!_arenaUiReady || _suppressArenaUi) return;
        _settings.ArenaOverlay.Enabled = ArenaEnabledToggle.IsChecked == true;
        if (!_settings.ArenaOverlay.Enabled)
        {
            _settings.ArenaOverlay.EditMode = false;
            ArenaEditToggle.IsChecked = false;
            ArenaEditValue.Text = "鼠标穿透";
            _arenaRenderHandle = IntPtr.Zero;
            ArenaStatusText.Text = "未启用";
        }
        ApplyArenaSettingsToUi();
        ApplyArenaConfigurationToOverlay(EnsureArenaOverlay());
        SaveArenaSettings();
        StartArenaTrackingIfEnabled();
        Log(_settings.ArenaOverlay.Enabled ? "决斗场范围覆盖层已启用" : "决斗场范围覆盖层已关闭");
    }

    private void ArenaInstanceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_arenaUiReady || _suppressArenaUi || ArenaInstanceCombo.SelectedIndex < 0) return;
        _settings.ArenaOverlay.EmulatorInstance = ArenaInstanceCombo.SelectedIndex;
        _arenaRenderHandle = IntPtr.Zero;
        SaveArenaSettings();
        UpdateArenaOverlay();
    }

    private void ArenaContentCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_arenaUiReady || _suppressArenaUi || ArenaContentCombo.SelectedIndex < 0) return;
        _settings.ArenaOverlay.ContentMode = ArenaContentCombo.SelectedIndex == 1 ? "lines" : "image";
        if (!string.Equals(_settings.ArenaOverlay.ContentMode, "lines", StringComparison.OrdinalIgnoreCase))
            _settings.ArenaOverlay.EditMode = false;
        ApplyArenaSettingsToUi();
        ApplyArenaConfigurationToOverlay(EnsureArenaOverlay());
        SaveArenaSettings();
    }

    private void ArenaImageOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_arenaUiReady || _suppressArenaUi) return;
        _settings.ArenaOverlay.ImageOpacityPercent = (int)Math.Round(e.NewValue);
        ArenaImageOpacityValue.Text = $"{_settings.ArenaOverlay.ImageOpacityPercent}%";
        _arenaOverlay?.UpdateSettings();
        SaveArenaSettings();
    }

    private void ArenaEditToggle_Click(object sender, RoutedEventArgs e)
    {
        if (!_arenaUiReady || _suppressArenaUi) return;
        bool editing = ArenaEditToggle.IsChecked == true;
        _settings.ArenaOverlay.EditMode = editing;
        ArenaEditValue.Text = editing ? "可拖动横线" : "鼠标穿透";
        _arenaOverlay?.SetEditMode(editing);
        SaveArenaSettings();
    }

    private void ArenaLinesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressArenaUi) return;
        ApplyArenaSelectedLineToUi();
    }

    private void ArenaAddLine_Click(object sender, RoutedEventArgs e)
    {
        var line = new ArenaRangeLine
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = $"横线 {_settings.ArenaOverlay.Lines.Count + 1}",
            Y = 0.5,
        };
        _settings.ArenaOverlay.Lines.Add(line);
        ArenaRefreshLineList(_settings.ArenaOverlay.Lines.Count - 1);
        _arenaOverlay?.UpdateSettings();
        SaveArenaSettings();
    }

    private void ArenaDuplicateLine_Click(object sender, RoutedEventArgs e)
    {
        ArenaRangeLine? source = SelectedArenaLine();
        if (source == null) return;
        var copy = new ArenaRangeLine
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = source.Name + " 副本",
            Enabled = source.Enabled,
            Y = Math.Clamp(source.Y + 0.03, 0, 1),
            Thickness = source.Thickness,
            OpacityPercent = source.OpacityPercent,
            Color = source.Color,
        };
        int index = ArenaLinesList.SelectedIndex + 1;
        _settings.ArenaOverlay.Lines.Insert(index, copy);
        ArenaRefreshLineList(index);
        _arenaOverlay?.UpdateSettings();
        SaveArenaSettings();
    }

    private void ArenaDeleteLine_Click(object sender, RoutedEventArgs e)
    {
        int index = ArenaLinesList.SelectedIndex;
        if (index < 0 || _settings.ArenaOverlay.Lines.Count <= 1) return;
        _settings.ArenaOverlay.Lines.RemoveAt(index);
        ArenaRefreshLineList(Math.Min(index, _settings.ArenaOverlay.Lines.Count - 1));
        _arenaOverlay?.UpdateSettings();
        SaveArenaSettings();
    }

    private void ArenaLineEnabledToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressArenaUi) return;
        ArenaRangeLine? line = SelectedArenaLine();
        if (line == null) return;
        line.Enabled = ArenaLineEnabledToggle.IsChecked == true;
        _arenaOverlay?.UpdateSettings();
        SaveArenaSettings();
    }

    private void ArenaLineYSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressArenaUi) return;
        ArenaRangeLine? line = SelectedArenaLine();
        if (line == null) return;
        line.Y = Math.Clamp(e.NewValue / 100.0, 0, 1);
        ArenaLineYValue.Text = $"{line.Y * 100.0:0.#}%";
        ArenaLinesList.Items.Refresh();
        _arenaOverlay?.UpdateSettings();
        SaveArenaSettings();
    }

    private void ArenaLineThicknessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressArenaUi) return;
        ArenaRangeLine? line = SelectedArenaLine();
        if (line == null) return;
        line.Thickness = Math.Clamp(e.NewValue, 1, 50);
        ArenaLineThicknessValue.Text = $"{line.Thickness:0.#} px";
        _arenaOverlay?.UpdateSettings();
        SaveArenaSettings();
    }

    private void ArenaLineOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressArenaUi) return;
        ArenaRangeLine? line = SelectedArenaLine();
        if (line == null) return;
        line.OpacityPercent = (int)Math.Round(e.NewValue);
        ArenaLineOpacityValue.Text = $"{line.OpacityPercent}%";
        _arenaOverlay?.UpdateSettings();
        SaveArenaSettings();
    }

    private void ArenaLineColorBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressArenaUi) return;
        ArenaRangeLine? line = SelectedArenaLine();
        if (line == null) return;
        string value = ArenaLineColorBox.Text.Trim();
        if (value.Length == 7 && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit))
            line.Color = value.ToUpperInvariant();
        else
            ArenaLineColorBox.Text = line.Color;
        _arenaOverlay?.UpdateSettings();
        SaveArenaSettings();
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
        _arenaTrackerTimer.Stop();
        _pipeline?.Dispose();
        _recognizer?.Dispose();
        _overlay?.Hide();
        _arenaOverlay?.HideOverlay();
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
        ApplyUiLayout();
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
        RecMetaText.Text = $"稳定 {_settings.StableFrames} 帧 · 推理 {_inferenceFps:0.0}/s · 置信 {_settings.ConfidenceThreshold:0.##}";
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
