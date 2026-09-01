using NaruttoTimer.Data;

namespace NaruttoTimer.Calibration;

/// <summary>取色校准结果（亮/暗阈值）。</summary>
public sealed record CalibrationResult(ColorRange Bright, ColorRange Dark);

/// <summary>取色校准存储与采样接口。</summary>
public interface ICalibrationStore
{
    CalibrationResult Load();
    void Save(CalibrationResult result);
    void ApplyTo(AppSettings settings);
}
