using NaruttoTimer.Capture;
using NaruttoTimer.Data;
using NaruttoTimer.Recognition;
using NaruttoTimer.Rules;

namespace NaruttoTimer.Tests;

/// <summary>P3 格检测纯函数测试。</summary>
public static class CellDetectorTests
{
    private static readonly RecognizerOptions Options = new(
        new ColorRange(180, 255, 120, 255, 120, 255),
        new ColorRange(0, 90, 70, 165, 85, 180),
        OrangeRange: new ColorRange(180, 255, 40, 150, 0, 130));

    private static CapturedFrame FrameWith(params FrameFactory.CellSpec[] cells) =>
        FrameFactory.Create(240, 90, cells, DateTime.UtcNow, (30, 50, 62));

    [Fact]
    public static void 检测_四亮格_居中分类()
    {
        var frame = FrameWith(
            new FrameFactory.CellSpec(40, 45, 14, (200, 230, 255)),
            new FrameFactory.CellSpec(90, 45, 14, (200, 230, 255)),
            new FrameFactory.CellSpec(140, 45, 14, (200, 230, 255)),
            new FrameFactory.CellSpec(190, 45, 14, (200, 230, 255)));
        var cells = CellDetector.Detect(frame, new RoiConfig(10, 10, 220, 70), Options);
        Check.Equal(4, cells.Count, "应检测出 4 格");
        Check.True(cells.All(c => c.State == CellState.Bright), "全部为亮格");
        Check.True(cells[0].CenterX < cells[1].CenterX, "中心按从左到右排序");
    }

    [Fact]
    public static void 检测_混合亮暗_归类正确()
    {
        var frame = FrameWith(
            new FrameFactory.CellSpec(40, 45, 14, (200, 230, 255)),
            new FrameFactory.CellSpec(90, 45, 14, (20, 110, 125)),
            new FrameFactory.CellSpec(140, 45, 14, (200, 230, 255)),
            new FrameFactory.CellSpec(190, 45, 14, (20, 110, 125)));
        var cells = CellDetector.Detect(frame, new RoiConfig(10, 10, 220, 70), Options);
        Check.Equal(4, cells.Count, "应检测出 4 格");
        Check.Equal(CellState.Bright, cells[0].State, "格1亮");
        Check.Equal(CellState.Dark, cells[1].State, "格2暗");
        Check.Equal(CellState.Bright, cells[2].State, "格3亮");
        Check.Equal(CellState.Dark, cells[3].State, "格4暗");
    }

    [Fact]
    public static void 检测_无格_返回空()
    {
        var frame = FrameWith();
        var cells = CellDetector.Detect(frame, new RoiConfig(10, 10, 220, 70), Options);
        Check.Equal(0, cells.Count, "纯背景应无格");
    }

    [Fact]
    public static void 检测_噪声小簇_被过滤()
    {
        var frame = FrameWith(
            new FrameFactory.CellSpec(40, 45, 14, (200, 230, 255)),
            new FrameFactory.CellSpec(90, 45, 14, (200, 230, 255)),
            new FrameFactory.CellSpec(140, 45, 14, (200, 230, 255)),
            new FrameFactory.CellSpec(190, 45, 14, (200, 230, 255)),
            // 2px 噪声点
            new FrameFactory.CellSpec(15, 20, 1, (200, 230, 255)));
        var cells = CellDetector.Detect(frame, new RoiConfig(10, 10, 220, 70), Options);
        Check.Equal(4, cells.Count, "噪声点不应成为一格");
    }

    [Fact]
    public static void 检测_ROI越界_钳制不崩溃()
    {
        var frame = FrameWith(
            new FrameFactory.CellSpec(40, 45, 14, (200, 230, 255)),
            new FrameFactory.CellSpec(90, 45, 14, (200, 230, 255)),
            new FrameFactory.CellSpec(140, 45, 14, (200, 230, 255)),
            new FrameFactory.CellSpec(190, 45, 14, (200, 230, 255)));
        var cells = CellDetector.Detect(frame, new RoiConfig(-50, -50, 400, 400), Options);
        Check.Equal(4, cells.Count, "越界 ROI 应钳制到帧内并正常检测");
    }

    [Fact]
    public static void 检测_空ROI_返回空()
    {
        var frame = FrameWith();
        Check.Equal(0, CellDetector.Detect(frame, new RoiConfig(0, 0, 0, 0), Options).Count, "零尺寸 ROI 返回空");
        Check.Equal(0, CellDetector.Detect(frame, new RoiConfig(10, 10, 2, 2), Options).Count, "过小 ROI 返回空");
    }
}