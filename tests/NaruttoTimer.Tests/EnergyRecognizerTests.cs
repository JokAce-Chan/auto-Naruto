using NaruttoTimer.Capture;
using NaruttoTimer.Data;
using NaruttoTimer.Recognition;
using NaruttoTimer.Recognition.ImageProcess;
using NaruttoTimer.Rules;
using OpenCvSharp;

namespace NaruttoTimer.Tests;

/// <summary>识别器端到端测试（真实模型 + 合成帧）。传统模式已移除，模型不可用时不再降级。</summary>
public static class EnergyRecognizerTests
{
    private static RecognizerOptions BuildOptions(string? modelPath = null) =>
        new(LabelImageConfig.CreateDefault(), modelPath ?? TestAssets.ModelPath, 0.7, 0.25);

    private static CapturedFrame Frame(byte value)
    {
        var pixels = new byte[640 * 360 * 4];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = value;
        return new CapturedFrame { Width = 640, Height = 360, Pixels = pixels, Timestamp = DateTime.UtcNow };
    }

    [Fact]
    public static void AI模式_合成帧_不抛异常且值在0到6之间()
    {
        using var recognizer = new EnergyRecognizer(BuildOptions());
        Check.True(recognizer.IsModelReady, "模型应加载成功");
        Check.True(recognizer.LoadWarning == null, "模型可用时不应有加载警告");

        var reading = recognizer.Recognize(Frame(0));
        Check.True(reading.LeftValue is >= 0 and <= EnergyRules.MaxValue, $"左值范围，实际 {reading.LeftValue}");
        Check.True(reading.RightValue is >= 0 and <= EnergyRules.MaxValue, $"右值范围，实际 {reading.RightValue}");
        Check.True(reading.LeftEmptyCount >= 0, $"左空豆非负，实际 {reading.LeftEmptyCount}");
    }

    [Fact]
    public static void 模型缺失_不可用且识别抛异常_不再回退传统()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"no-model-{Guid.NewGuid():N}.onnx");
        using var recognizer = new EnergyRecognizer(BuildOptions(modelPath: missing));
        Check.False(recognizer.IsModelReady, "模型不可用");
        Check.True(recognizer.LoadWarning != null, "应有加载失败提示");

        Check.Throws<InvalidOperationException>(() => recognizer.Recognize(Frame(0)), "无模型时识别应抛异常");
    }

    [Fact]
    public static void TestRecognize_使用临时参数_不影响当前设置()
    {
        using var recognizer = new EnergyRecognizer(BuildOptions());
        var current = recognizer.Options.Label;

        var temporary = LabelImageConfig.CreateDefault();
        temporary.EnergyBar.Left = new LabelRect(0.4, 0.4, 0.55, 0.55);
        using var mat = MatUtil.FromBgra(640, 360, new byte[640 * 360 * 4]);

        var reading = recognizer.TestRecognize(new RecognizerOptions(temporary, TestAssets.ModelPath, 0.7, 0.25), mat);
        Check.True(reading.LeftValue is >= 0 and <= EnergyRules.MaxValue, $"临时参数可识别，实际 {reading.LeftValue}");
        Check.True(ReferenceEquals(current, recognizer.Options.Label), "当前设置未被临时参数替换");
    }

    [Fact]
    public static void Dispose后_再识别抛异常()
    {
        var recognizer = new EnergyRecognizer(BuildOptions());
        recognizer.Dispose();
        Check.Throws<ObjectDisposedException>(() => recognizer.Recognize(Frame(0)), "释放后不应再识别");
    }
}
