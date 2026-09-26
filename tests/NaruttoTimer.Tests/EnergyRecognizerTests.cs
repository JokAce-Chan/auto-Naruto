using NaruttoTimer.Capture;
using NaruttoTimer.Data;
using NaruttoTimer.Recognition;
using NaruttoTimer.Recognition.ImageProcess;
using OpenCvSharp;

namespace NaruttoTimer.Tests;

/// <summary>识别器端到端测试（真实模型 + 合成帧）。</summary>
public static class EnergyRecognizerTests
{
    private static LabelImageConfig DefaultLabelOfMode(LabelMode mode)
    {
        var label = LabelImageConfig.CreateDefault();
        label.Mode = mode;
        return label;
    }

    private static RecognizerOptions BuildOptions(LabelMode mode, string? modelPath = null) =>
        new(DefaultLabelOfMode(mode), modelPath ?? TestAssets.ModelPath, 0.7, 0.25, 110);

    private static CapturedFrame Frame(byte value)
    {
        var pixels = new byte[640 * 360 * 4];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = value;
        return new CapturedFrame { Width = 640, Height = 360, Pixels = pixels, Timestamp = DateTime.UtcNow };
    }

    [Fact]
    public static void 传统模式_全暗帧_两侧空豆为4_值为0()
    {
        using var recognizer = new EnergyRecognizer(BuildOptions(LabelMode.TRADITIONAL));
        Check.Equal(LabelMode.TRADITIONAL, recognizer.EffectiveMode, "传统模式生效");

        var reading = recognizer.Recognize(Frame(0));
        Check.Equal(4, reading.LeftEmptyCount, "左侧空豆");
        Check.Equal(4, reading.RightEmptyCount, "右侧空豆");
        Check.Equal(0, reading.LeftValue, "左值 = 4 - 4");
        Check.Equal(0, reading.RightValue, "右值 = 4 - 4");
    }

    [Fact]
    public static void 传统模式_全亮帧_空豆为0_值为4()
    {
        using var recognizer = new EnergyRecognizer(BuildOptions(LabelMode.TRADITIONAL));
        var reading = recognizer.Recognize(Frame(255));
        Check.Equal(0, reading.LeftEmptyCount, "左侧空豆");
        Check.Equal(0, reading.RightEmptyCount, "右侧空豆");
        Check.Equal(4, reading.LeftValue, "左值 = 4 - 0");
        Check.Equal(4, reading.RightValue, "右值 = 4 - 0");
    }

    [Fact]
    public static void AI模式_合成帧_不抛异常且值在0到4之间()
    {
        using var recognizer = new EnergyRecognizer(BuildOptions(LabelMode.AI));
        Check.Equal(LabelMode.AI, recognizer.EffectiveMode, "模型可用时 AI 模式生效");
        Check.True(recognizer.LoadWarning == null, "模型应加载成功");

        var reading = recognizer.Recognize(Frame(0));
        Check.True(reading.LeftValue is >= 0 and <= 4, $"左值范围，实际 {reading.LeftValue}");
        Check.True(reading.RightValue is >= 0 and <= 4, $"右值范围，实际 {reading.RightValue}");
        Check.True(reading.LeftEmptyCount is >= 0 and <= 16, $"左空豆范围，实际 {reading.LeftEmptyCount}");
    }

    [Fact]
    public static void AI模式_模型缺失_回退传统并给出提示()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"no-model-{Guid.NewGuid():N}.onnx");
        using var recognizer = new EnergyRecognizer(BuildOptions(LabelMode.AI, modelPath: missing));
        Check.True(recognizer.LoadWarning != null, "应有回退提示");
        Check.Equal(LabelMode.TRADITIONAL, recognizer.EffectiveMode, "回退到传统模式");

        var reading = recognizer.Recognize(Frame(0));
        Check.Equal(4, reading.LeftEmptyCount, "回退后仍能按传统算法识别");
    }

    [Fact]
    public static void TestRecognize_使用临时参数_不影响当前设置()
    {
        using var recognizer = new EnergyRecognizer(BuildOptions(LabelMode.AI));
        var temporary = BuildOptions(LabelMode.TRADITIONAL);
        using var mat = MatUtil.FromBgra(640, 360, new byte[640 * 360 * 4]);

        var reading = recognizer.TestRecognize(temporary, mat);
        Check.Equal(4, reading.LeftEmptyCount, "临时参数按传统算法识别");
        Check.Equal(LabelMode.AI, recognizer.Options.Label.Mode, "当前设置仍为 AI");
    }

    [Fact]
    public static void Dispose后_再识别抛异常()
    {
        var recognizer = new EnergyRecognizer(BuildOptions(LabelMode.TRADITIONAL));
        recognizer.Dispose();
        Check.Throws<ObjectDisposedException>(() => recognizer.Recognize(Frame(0)), "释放后不应再识别");
    }
}
