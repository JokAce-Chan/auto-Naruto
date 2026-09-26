using Microsoft.ML.OnnxRuntime;
using NaruttoTimer.Recognition.Ai;
using OpenCvSharp;

namespace NaruttoTimer.Tests;

/// <summary>ONNX 空豆模型测试（真实加载 best.onnx，验证源 OnnxModel 的输入输出契约）。</summary>
public static class OnnxModelTests
{
    [Fact]
    public static void 模型_输入输出维度符合预期()
    {
        using var session = new InferenceSession(TestAssets.ModelPath);

        var input = session.InputMetadata.First();
        Check.Equal("images", input.Key, "输入名");
        var inDims = input.Value.Dimensions;
        Check.Equal(4, inDims.Length, "输入维度数");
        Check.Equal(1, inDims[0], "batch");
        Check.Equal(3, inDims[1], "channels（RGB）");
        Check.Equal(128, inDims[2], "输入高");
        Check.Equal(128, inDims[3], "输入宽");

        var output = session.OutputMetadata.First();
        Check.Equal("output0", output.Key, "输出名");
        var outDims = output.Value.Dimensions;
        Check.Equal(3, outDims.Length, "输出维度数");
        Check.Equal(1, outDims[0], "batch");
        Check.Equal(5, outDims[1], "4 框 + 1 置信度（单类空豆）");
        Check.Equal(336, outDims[2], "16²+8²+4² 候选框");
    }

    [Fact]
    public static void 构造_读取输入尺寸与阈值()
    {
        using var model = new OnnxEnergyModel(TestAssets.ModelPath, confidence: 0.7, nms: 0.25);
        Check.Equal(128, model.InputWidth, "输入宽");
        Check.Equal(128, model.InputHeight, "输入高");
        Check.Near(0.7, model.ConfidenceThreshold, 0.0001, "置信度");
        Check.Near(0.25, model.NmsThreshold, 0.0001, "NMS");
    }

    [Fact]
    public static void 模型文件缺失_抛异常()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"no-model-{Guid.NewGuid():N}.onnx");
        Check.Throws<FileNotFoundException>(() => new OnnxEnergyModel(missing), "缺失模型应抛异常");
    }

    [Fact]
    public static void 单条推理_不抛异常且空豆数在合理范围()
    {
        using var model = new OnnxEnergyModel(TestAssets.ModelPath);
        using var bar = new Mat(48, 77, MatType.CV_8UC3, new Scalar(0, 0, 0));
        int empty = model.InferEmptyCount(bar);
        Check.True(empty is >= 0 and <= 16, $"空豆数应在 0~16，实际 {empty}");
    }

    [Fact]
    public static void 左右拼接推理_两侧计数与检出框在范围内()
    {
        using var model = new OnnxEnergyModel(TestAssets.ModelPath);
        using var left = new Mat(48, 77, MatType.CV_8UC3, new Scalar(20, 20, 20));
        using var right = new Mat(48, 77, MatType.CV_8UC3, new Scalar(200, 200, 200));

        var result = model.InferCombined(left, right);

        Check.True(result.LeftEmptyCount is >= 0 and <= 16, $"左侧空豆数合理，实际 {result.LeftEmptyCount}");
        Check.True(result.RightEmptyCount is >= 0 and <= 16, $"右侧空豆数合理，实际 {result.RightEmptyCount}");
        Check.Equal(result.LeftEmptyCount, result.LeftDetections.Count, "左检出框数与左计数一致");
        Check.Equal(result.RightEmptyCount, result.RightDetections.Count, "右检出框数与右计数一致");

        foreach (var d in result.LeftDetections)
        {
            Check.True(d.Score >= 0.7f, "检出框置信度不低于阈值");
            Check.True(d.X >= -0.5f && d.X <= 1.5f, "归一化 X 在合理范围");
            Check.True(d.Y >= -0.5f && d.Y <= 1.5f, "归一化 Y 在合理范围");
        }
    }
}
