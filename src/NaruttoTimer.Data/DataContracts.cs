using NaruttoTimer.Rules;

namespace NaruttoTimer.Data;

/// <summary>应用设置（JSON 持久化）。识别相关参数对齐源项目「六道带土计时器2.0」。</summary>
public sealed class AppSettings
{
    public string DeviceSerial { get; set; } = "emulator-7834";
    public string ScrcpyPath { get; set; } = "";
    public string AdbPath { get; set; } = "";
    public int VideoWidth { get; set; } = 1280;
    public int VideoHeight { get; set; } = 720;
    public int MaxFps { get; set; } = 30;

    /// <summary>标注配置（左右能量条）。</summary>
    public LabelImageConfig Label { get; set; } = LabelImageConfig.CreateDefault();

    /// <summary>替换身冷却时长（秒）。源项目 cdSeconds = 14.5。</summary>
    public double CountdownSeconds { get; set; } = 14.5;

    /// <summary>稳定判定帧数。源项目 StableValueTracker(3)。</summary>
    public int StableFrames { get; set; } = 3;

    /// <summary>推理间隔（毫秒）。源项目 fps = 8 → 125ms；可调以控制 CPU 占用。</summary>
    public int InferenceIntervalMs { get; set; } = 125;

    /// <summary>AI 置信度阈值。源项目 OnnxModel 默认 0.7。</summary>
    public double ConfidenceThreshold { get; set; } = 0.7;

    /// <summary>NMS IoU 阈值。源项目 0.25。</summary>
    public double NmsThreshold { get; set; } = 0.25;

    /// <summary>判定方式（AI 模式）：值判定 / 空豆判定。空豆判定可覆盖 6 格条（空豆 5、6）。</summary>
    public EnergyJudgement Judgement { get; set; } = EnergyJudgement.Value;

    public int OverlayOpacityPercent { get; set; } = 55;
    public int OverlayTextOpacityPercent { get; set; } = 100;
    public string OverlayLeftColor { get; set; } = "#1F9D55";
    public string OverlayRightColor { get; set; } = "#E03E3E";
    public bool OverlayLocked { get; set; }
    public string DataDirectory { get; set; } = "data";

    public AppSettings Normalize()
    {
        VideoWidth = VideoWidth <= 0 ? 1280 : VideoWidth;
        VideoHeight = VideoHeight <= 0 ? 720 : VideoHeight;
        CountdownSeconds = CountdownSeconds <= 0 ? 14.5 : CountdownSeconds;
        StableFrames = StableFrames < 1 ? 1 : StableFrames;
        InferenceIntervalMs = InferenceIntervalMs < 16 ? 16 : InferenceIntervalMs;
        Label ??= LabelImageConfig.CreateDefault();
        Label.EnergyBar ??= LabelImageConfig.CreateDefault().EnergyBar;
        return this;
    }
}

/// <summary>数据保存接口：事件日志、配置读写、导出。</summary>
public interface IDataStore
{
    void AppendTrigger(TriggerEvent evt);
    void AppendLog(string message);
    IReadOnlyList<TriggerEvent> GetTriggers(DateTime? from = null, DateTime? to = null);
    AppSettings LoadSettings();
    void SaveSettings(AppSettings settings);
    void ExportCsv(string path);
    void ExportJson(string path);
}
