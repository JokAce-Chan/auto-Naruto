using System.Text.Json.Serialization;
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

    /// <summary>决斗场范围覆盖层设置。</summary>
    public ArenaOverlaySettings ArenaOverlay { get; set; } = new();

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

    // ── 主界面记忆（验收稿 v6 右侧选项栏）──

    /// <summary>右侧选项栏宽度（px，250–550）。</summary>
    public int SidePanelWidth { get; set; } = 300;

    /// <summary>右侧选项栏是否折叠为图标条（58px）。</summary>
    public bool SidePanelCollapsed { get; set; }

    /// <summary>右侧分组开合状态（键：timer / arena / debug / icons）。</summary>
    public Dictionary<string, bool> GroupOpen { get; set; } = new();

    /// <summary>界面图标（键见 UiIcons.Targets，值为 Segoe MDL2 字形）。</summary>
    public Dictionary<string, string> UiIcons { get; set; } = new();

    public AppSettings Normalize()
    {
        VideoWidth = VideoWidth <= 0 ? 1280 : VideoWidth;
        VideoHeight = VideoHeight <= 0 ? 720 : VideoHeight;
        CountdownSeconds = CountdownSeconds <= 0 ? 14.5 : CountdownSeconds;
        StableFrames = StableFrames < 1 ? 1 : StableFrames;
        InferenceIntervalMs = InferenceIntervalMs < 16 ? 16 : InferenceIntervalMs;
        ArenaOverlay ??= new ArenaOverlaySettings();
        ArenaOverlay.Normalize();
        Label ??= LabelImageConfig.CreateDefault();
        Label.EnergyBar ??= LabelImageConfig.CreateDefault().EnergyBar;
        SidePanelWidth = Math.Clamp(SidePanelWidth, 250, 550);
        GroupOpen ??= new Dictionary<string, bool>();
        UiIcons ??= new Dictionary<string, string>();
        return this;
    }
}

public sealed class ArenaOverlaySettings
{
    public bool Enabled { get; set; }
    public int EmulatorInstance { get; set; }
    public string ContentMode { get; set; } = "image";
    public int ImageOpacityPercent { get; set; } = 60;
    public bool EditMode { get; set; }
    public List<ArenaRangeLine> Lines { get; set; } = ArenaRangeLine.CreateDefaults();

    public ArenaOverlaySettings Normalize()
    {
        EmulatorInstance = Math.Clamp(EmulatorInstance, 0, 99);
        ContentMode = string.Equals(ContentMode?.Trim(), "lines", StringComparison.OrdinalIgnoreCase) ? "lines" : "image";
        ImageOpacityPercent = Math.Clamp(ImageOpacityPercent, 0, 100);
        Lines ??= ArenaRangeLine.CreateDefaults();
        if (Lines.Count == 0) Lines.AddRange(ArenaRangeLine.CreateDefaults());
        for (int i = 0; i < Lines.Count; i++)
        {
            var line = Lines[i] ??= new ArenaRangeLine();
            line.Id = string.IsNullOrWhiteSpace(line.Id) ? $"line-{i + 1}" : line.Id.Trim();
            line.Name = string.IsNullOrWhiteSpace(line.Name) ? $"横线 {i + 1}" : line.Name.Trim();
            line.Y = Math.Clamp(line.Y, 0.0, 1.0);
            line.Thickness = Math.Clamp(line.Thickness, 1.0, 50.0);
            line.OpacityPercent = Math.Clamp(line.OpacityPercent, 0, 100);
            line.Color = NormalizeHexColor(line.Color, "#FFE65A");
        }
        return this;
    }

    private static string NormalizeHexColor(string? value, string fallback)
    {
        string candidate = (value ?? "").Trim();
        if (candidate.Length == 7 && candidate[0] == '#' && candidate.Skip(1).All(Uri.IsHexDigit))
            return candidate.ToUpperInvariant();
        return fallback;
    }
}

public sealed class ArenaRangeLine
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "横线";
    public bool Enabled { get; set; } = true;
    public double Y { get; set; } = 0.5;
    public double Thickness { get; set; } = 3.0;
    public int OpacityPercent { get; set; } = 60;
    public string Color { get; set; } = "#FFE65A";

    [JsonIgnore]
    public string DisplayName => $"{Name}  ·  {Y * 100.0:0.#}%";

    public static List<ArenaRangeLine> CreateDefaults() =>
    [
        new() { Id = "line-1", Name = "横线 1", Y = 0.665 },
        new() { Id = "line-2", Name = "横线 2", Y = 0.763 },
        new() { Id = "line-3", Name = "横线 3", Y = 0.861 }
    ];
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
