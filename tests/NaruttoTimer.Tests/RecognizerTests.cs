using NaruttoTimer.Capture;
using NaruttoTimer.Data;
using NaruttoTimer.Recognition;
using NaruttoTimer.Rules;

namespace NaruttoTimer.Tests;

/// <summary>P3 识别层测试：合成帧驱动（不依赖设备）。</summary>
public static class RecognizerTests
{
    private const int FrameW = 240;
    private const int FrameH = 90;
    private static readonly RoiConfig LeftRoi = new(10, 10, 220, 70);

    private static readonly (byte R, byte G, byte B) BrightColor = (200, 230, 255);
    private static readonly (byte R, byte G, byte B) DarkColor = (20, 110, 125);
    private static readonly (byte R, byte G, byte B) OrangeColor = (255, 90, 60);
    private static readonly (byte R, byte G, byte B) Background = (30, 50, 62);

    private static Recognizer NewRecognizer(int debounce = 2) =>
        new(new RecognizerOptions(
            new ColorRange(180, 255, 120, 255, 120, 255),
            new ColorRange(0, 90, 70, 165, 85, 180),
            LeftRoi,
            null,
            OrangeRange: new ColorRange(180, 255, 40, 140, 0, 120),
            DebounceFrames: debounce,
            LoadingSeconds: 0.3));

    private static CapturedFrame MakeFrame(
        IEnumerable<FrameFactory.CellSpec> cells,
        DateTime ts,
        (byte R, byte G, byte B)? background = null) =>
        FrameFactory.Create(FrameW, FrameH, cells, ts, background ?? Background);

    private static CapturedFrame FourCells(int cx0, int cy, int r, (byte R, byte G, byte B) c1,
        (byte R, byte G, byte B) c2, (byte R, byte G, byte B) c3, (byte R, byte G, byte B) c4, DateTime ts) =>
        MakeFrame(new[]
        {
            new FrameFactory.CellSpec(cx0, cy, r, c1),
            new FrameFactory.CellSpec(cx0 + 50, cy, r, c2),
            new FrameFactory.CellSpec(cx0 + 100, cy, r, c3),
            new FrameFactory.CellSpec(cx0 + 150, cy, r, c4),
        }, ts);

    // ── 基础识别 ──

    [Fact]
    public static void 四格两亮两暗_识别值2格数4()
    {
        var rec = NewRecognizer();
        var t0 = DateTime.UtcNow;
        var frame = FourCells(30, 45, 14, BrightColor, BrightColor, DarkColor, DarkColor, t0);
        var r1 = rec.Recognize(frame).Left;
        var r2 = rec.Recognize(FourCells(30, 45, 14, BrightColor, BrightColor, DarkColor, DarkColor, t0 + TimeSpan.FromMilliseconds(40))).Left;
        Check.Equal(GridCount.G4, r2.GridCount, "格数=4");
        Check.Equal(2, r2.Value, "值=2（两亮）");
        Check.False(r2.InLoading, "不应加载中");
        Check.Equal(4, r1.Cells.Count, "格分类列表 4 项");
        Check.Equal(CellState.Bright, r1.Cells[0], "第1格亮");
        Check.Equal(CellState.Dark, r1.Cells[3], "第4格暗");
    }

    [Fact]
    public static void 六格三亮三暗_识别值3格数6()
    {
        var rec = NewRecognizer();
        var t0 = DateTime.UtcNow;
        var cells = new[]
        {
            new FrameFactory.CellSpec(30, 45, 12, BrightColor),
            new FrameFactory.CellSpec(66, 45, 12, BrightColor),
            new FrameFactory.CellSpec(102, 45, 12, BrightColor),
            new FrameFactory.CellSpec(138, 45, 12, DarkColor),
            new FrameFactory.CellSpec(174, 45, 12, DarkColor),
            new FrameFactory.CellSpec(210, 45, 12, DarkColor),
        };
        rec.Recognize(MakeFrame(cells, t0));
        var r = rec.Recognize(MakeFrame(cells, t0 + TimeSpan.FromMilliseconds(40))).Left;
        Check.Equal(GridCount.G6, r.GridCount, "格数=6");
        Check.Equal(3, r.Value, "值=3");
    }

    [Fact]
    public static void 突变橙红_归入亮格()
    {
        var rec = NewRecognizer();
        var t0 = DateTime.UtcNow;
        var cells = new[]
        {
            new FrameFactory.CellSpec(40, 45, 14, OrangeColor),
            new FrameFactory.CellSpec(90, 45, 14, BrightColor),
            new FrameFactory.CellSpec(140, 45, 14, DarkColor),
            new FrameFactory.CellSpec(190, 45, 14, DarkColor),
        };
        rec.Recognize(MakeFrame(cells, t0));
        var r = rec.Recognize(MakeFrame(cells, t0 + TimeSpan.FromMilliseconds(40))).Left;
        Check.Equal(2, r.Value, "橙红+天蓝=2 亮格");
        Check.Equal(CellState.Bright, r.Cells[0], "橙红格归 Bright");
    }

    [Fact]
    public static void 防抖_单帧抖动不提交()
    {
        var rec = NewRecognizer(debounce: 2);
        var t0 = DateTime.UtcNow;
        var baseCells = new[]
        {
            new FrameFactory.CellSpec(30, 45, 14, BrightColor),
            new FrameFactory.CellSpec(80, 45, 14, BrightColor),
            new FrameFactory.CellSpec(130, 45, 14, DarkColor),
            new FrameFactory.CellSpec(180, 45, 14, DarkColor),
        };
        // 第1帧：值2（候选）
        var r1 = rec.Recognize(MakeFrame(baseCells, t0)).Left;
        // 第2帧：值3 抖动（单帧，未达防抖）
        var blip = new[]
        {
            new FrameFactory.CellSpec(30, 45, 14, BrightColor),
            new FrameFactory.CellSpec(80, 45, 14, BrightColor),
            new FrameFactory.CellSpec(130, 45, 14, BrightColor),
            new FrameFactory.CellSpec(180, 45, 14, DarkColor),
        };
        var r2 = rec.Recognize(MakeFrame(blip, t0 + TimeSpan.FromMilliseconds(40))).Left;
        Check.Equal(2, r2.Value, "单帧抖动不应提交");
        // 第3帧：仍是值2 → 稳定提交
        var r3 = rec.Recognize(MakeFrame(baseCells, t0 + TimeSpan.FromMilliseconds(80))).Left;
        Check.Equal(2, r3.Value, "连续两帧一致后提交");
        // 第4、5帧：值3 连续两帧 → 提交
        rec.Recognize(MakeFrame(blip, t0 + TimeSpan.FromMilliseconds(120)));
        var r5 = rec.Recognize(MakeFrame(blip, t0 + TimeSpan.FromMilliseconds(160))).Left;
        Check.Equal(3, r5.Value, "新值连续两帧后提交");
    }

    [Fact]
    public static void 加载判定_连续无格超0_3秒()
    {
        var rec = NewRecognizer();
        var t0 = DateTime.UtcNow;
        var empty = MakeFrame(Array.Empty<FrameFactory.CellSpec>(), t0);
        var r1 = rec.Recognize(empty).Left;
        Check.False(r1.InLoading, "起始瞬间未达加载时长");
        var r2 = rec.Recognize(MakeFrame(Array.Empty<FrameFactory.CellSpec>(), t0 + TimeSpan.FromMilliseconds(150))).Left;
        Check.False(r2.InLoading, "0.15s 未达 0.3s");
        var r3 = rec.Recognize(MakeFrame(Array.Empty<FrameFactory.CellSpec>(), t0 + TimeSpan.FromMilliseconds(310))).Left;
        Check.True(r3.InLoading, "0.31s 应判定加载中");
    }

    [Fact]
    public static void 加载恢复_重新稳定识别()
    {
        var rec = NewRecognizer();
        var t0 = DateTime.UtcNow;
        rec.Recognize(MakeFrame(Array.Empty<FrameFactory.CellSpec>(), t0));
        rec.Recognize(MakeFrame(Array.Empty<FrameFactory.CellSpec>(), t0 + TimeSpan.FromMilliseconds(310))); // 进入加载
        var cells = new[]
        {
            new FrameFactory.CellSpec(30, 45, 14, BrightColor),
            new FrameFactory.CellSpec(80, 45, 14, BrightColor),
            new FrameFactory.CellSpec(130, 45, 14, DarkColor),
            new FrameFactory.CellSpec(180, 45, 14, DarkColor),
        };
        var r1 = rec.Recognize(MakeFrame(cells, t0 + TimeSpan.FromMilliseconds(350))).Left;
        var r2 = rec.Recognize(MakeFrame(cells, t0 + TimeSpan.FromMilliseconds(390))).Left;
        Check.False(r2.InLoading, "稳定两帧后退出加载");
        Check.Equal(2, r2.Value, "恢复后的值=2");
    }

    [Fact]
    public static void 五格异常_视为不稳定()
    {
        var rec = NewRecognizer();
        var t0 = DateTime.UtcNow;
        var cells = new[]
        {
            new FrameFactory.CellSpec(25, 45, 12, BrightColor),
            new FrameFactory.CellSpec(65, 45, 12, BrightColor),
            new FrameFactory.CellSpec(105, 45, 12, BrightColor),
            new FrameFactory.CellSpec(145, 45, 12, BrightColor),
            new FrameFactory.CellSpec(185, 45, 12, BrightColor),
        };
        var r = rec.Recognize(MakeFrame(cells, t0)).Left;
        Check.False(r.InLoading, "首帧未达加载时长");
        var r2 = rec.Recognize(MakeFrame(cells, t0 + TimeSpan.FromMilliseconds(310))).Left;
        Check.True(r2.InLoading, "异常格数持续应进入加载");
    }

    [Fact]
    public static void 无ROI_视为不稳定()
    {
        var rec = new Recognizer(new RecognizerOptions(
            new ColorRange(180, 255, 120, 255, 120, 255),
            new ColorRange(0, 90, 70, 165, 85, 180),
            null, null));
        var t0 = DateTime.UtcNow;
        var r1 = rec.Recognize(MakeFrame(Array.Empty<FrameFactory.CellSpec>(), t0)).Left;
        Check.False(r1.InLoading, "首帧未达加载时长");
        var r2 = rec.Recognize(MakeFrame(Array.Empty<FrameFactory.CellSpec>(), t0 + TimeSpan.FromMilliseconds(310))).Left;
        Check.True(r2.InLoading, "无 ROI 持续应进入加载");
    }

    [Fact]
    public static void Reset_清除状态()
    {
        var rec = NewRecognizer();
        var t0 = DateTime.UtcNow;
        rec.Recognize(MakeFrame(Array.Empty<FrameFactory.CellSpec>(), t0));
        rec.Recognize(MakeFrame(Array.Empty<FrameFactory.CellSpec>(), t0 + TimeSpan.FromMilliseconds(310)));
        var before = rec.Recognize(MakeFrame(Array.Empty<FrameFactory.CellSpec>(), t0 + TimeSpan.FromMilliseconds(320))).Left;
        Check.True(before.InLoading, "Reset 前为加载中");
        rec.Reset();
        var after = rec.Recognize(MakeFrame(Array.Empty<FrameFactory.CellSpec>(), t0 + TimeSpan.FromMilliseconds(330))).Left;
        Check.False(after.InLoading, "Reset 后重新计时");
    }
}

/// <summary>合成帧工厂：在背景上绘制菱形格。</summary>
public static class FrameFactory
{
    public sealed record CellSpec(int Cx, int Cy, int R, (byte R, byte G, byte B) Color);

    public static CapturedFrame Create(int width, int height, IEnumerable<CellSpec> cells, DateTime ts,
        (byte R, byte G, byte B) background)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = (y * width + x) * 4;
                pixels[idx] = background.B;
                pixels[idx + 1] = background.G;
                pixels[idx + 2] = background.R;
                pixels[idx + 3] = 255;
            }
        }

        foreach (var c in cells)
        {
            for (int dy = -c.R; dy <= c.R; dy++)
            {
                int y = c.Cy + dy;
                if (y < 0 || y >= height) continue;
                int half = c.R - Math.Abs(dy);
                for (int dx = -half; dx <= half; dx++)
                {
                    int x = c.Cx + dx;
                    if (x < 0 || x >= width) continue;
                    int idx = (y * width + x) * 4;
                    pixels[idx] = c.Color.B;
                    pixels[idx + 1] = c.Color.G;
                    pixels[idx + 2] = c.Color.R;
                    pixels[idx + 3] = 255;
                }
            }
        }

        return new CapturedFrame { Width = width, Height = height, Pixels = pixels, Timestamp = ts };
    }
}