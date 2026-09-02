using NaruttoTimer.Rules;

namespace NaruttoTimer.Data;

/// <summary>亮/暗颜色判定范围（RGB 最小~最大，取色校准模块维护）。</summary>
public sealed record ColorRange(byte RMin, byte RMax, byte GMin, byte GMax, byte BMin, byte BMax)
{
    public bool Contains(int r, int g, int b) =>
        r >= RMin && r <= RMax && g >= GMin && g <= GMax && b >= BMin && b <= BMax;
}

/// <summary>识别区域配置（屏幕坐标）。</summary>
public sealed record RoiConfig(int X, int Y, int Width, int Height);

/// <summary>应用设置（JSON 持久化）。</summary>
public sealed class AppSettings
{
    /// <summary>Default left ROI (region A) for the 1280x150 recognition frame; auto-filled.</summary>
    public static RoiConfig DefaultLeftRoi { get; } = new(112, 66, 132, 48);

    /// <summary>Default right ROI (region B) for the 1280x150 recognition frame; auto-filled.</summary>
    public static RoiConfig DefaultRightRoi { get; } = new(1050, 66, 110, 48);

    public string DeviceSerial { get; set; } = "emulator-7834";
    public string ScrcpyPath { get; set; } = "";
    public string AdbPath { get; set; } = "";
    public int VideoWidth { get; set; } = 1280;
    public int VideoHeight { get; set; } = 720;
    public int CropTopRows { get; set; } = 150;
    public int MaxFps { get; set; } = 30;
    public RoiConfig? LeftRoi { get; set; } = DefaultLeftRoi;
    public RoiConfig? RightRoi { get; set; } = DefaultRightRoi;
    public double CountdownSeconds { get; set; } = 15.0;
    public double LoadingSeconds { get; set; } = 0.3;
    public double GridJudgeSeconds { get; set; } = 1.0;
    public int DebounceFrames { get; set; } = 2;
    public ColorRange BrightRange { get; set; } = new(180, 255, 120, 255, 120, 255);
    public ColorRange DarkRange { get; set; } = new(0, 90, 70, 165, 85, 180);
    public int OverlayOpacityPercent { get; set; } = 55;
    public int OverlayTextOpacityPercent { get; set; } = 100;
    public string OverlayLeftColor { get; set; } = "#1F9D55";
    public string OverlayRightColor { get; set; } = "#E03E3E";
    public bool OverlayLocked { get; set; }
    public string DataDirectory { get; set; } = "data";
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
