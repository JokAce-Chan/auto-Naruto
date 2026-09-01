using System.Text.Json;
using NaruttoTimer.Data;

namespace NaruttoTimer.Calibration;

/// <summary>取色校准持久化：亮/暗区间写入 JSON，重启沿用。</summary>
public sealed class CalibrationStore : ICalibrationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    public CalibrationStore(string path) => _path = path;

    public CalibrationResult Load()
    {
        if (!File.Exists(_path)) return Default();
        try
        {
            var json = File.ReadAllText(_path);
            var result = JsonSerializer.Deserialize<CalibrationResult>(json);
            if (result == null) return Default();
            // 校验区间合法性，损坏则回退默认
            if (!IsValid(result.Bright) || !IsValid(result.Dark)) return Default();
            return result;
        }
        catch (JsonException)
        {
            return Default();
        }
    }

    public void Save(CalibrationResult result)
    {
        if (!IsValid(result.Bright) || !IsValid(result.Dark))
            throw new ArgumentException("校准区间非法", nameof(result));
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(result, JsonOptions));
    }

    public void ApplyTo(AppSettings settings)
    {
        var r = Load();
        settings.BrightRange = r.Bright;
        settings.DarkRange = r.Dark;
    }

    public static CalibrationResult Default() => new(
        new ColorRange(180, 255, 120, 255, 120, 255),
        new ColorRange(0, 60, 0, 60, 0, 90));

    private static bool IsValid(ColorRange range) =>
        range.RMin <= range.RMax && range.GMin <= range.GMax && range.BMin <= range.BMax;
}