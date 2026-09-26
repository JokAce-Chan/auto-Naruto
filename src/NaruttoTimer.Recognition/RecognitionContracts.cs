using NaruttoTimer.Capture;
using NaruttoTimer.Data;
using NaruttoTimer.Rules;

namespace NaruttoTimer.Recognition;

/// <summary>AI 检出的一颗「空豆」框（坐标已归一化到所在能量条矩形内，便于预览叠层绘制）。</summary>
public readonly record struct EnergyDetection(float X, float Y, float Width, float Height, float Score);

/// <summary>
/// 单帧识别结果（源 Engine.onFrame）。
/// 值 = 4 - 空豆数，并 clamp 到 0~4。
/// </summary>
public sealed record EnergyReading(
    int LeftValue,
    int RightValue,
    int LeftEmptyCount,
    int RightEmptyCount,
    IReadOnlyList<EnergyDetection> LeftDetections,
    IReadOnlyList<EnergyDetection> RightDetections)
{
    public int GetValue(Side side) => side == Side.Left ? LeftValue : RightValue;
}

/// <summary>识别参数（模型 / 标注 / 阈值）。</summary>
public sealed record RecognizerOptions(
    LabelImageConfig Label,
    string ModelPath,
    double ConfidenceThreshold = 0.7,
    double NmsThreshold = 0.25,
    int TraditionalGrayThreshold = 110)
{
    /// <summary>默认资源目录（随输出拷入的 assets）。</summary>
    public static string DefaultAssetDirectory => Path.Combine(AppContext.BaseDirectory, "assets");
}

/// <summary>识别器：输入一帧（BGRA 整帧），输出两侧值。</summary>
public interface IEnergyRecognizer : IDisposable
{
    EnergyReading Recognize(CapturedFrame frame);
    void Reset();
}
