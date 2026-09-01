using NaruttoTimer.Calibration;
using NaruttoTimer.Data;

namespace NaruttoTimer.Tests;

/// <summary>P4 取色校准测试。</summary>
public static class CalibrationTests
{
    [Fact]
    public static void 区间生成_多采样_取通道最小最大()
    {
        var range = ColorRangeBuilder.Build(new[] { (100, 150, 200), (110, 160, 210) }, tolerance: 0);
        Check.Equal(100, (int)range.RMin, "R 最小");
        Check.Equal(110, (int)range.RMax, "R 最大");
        Check.Equal(150, (int)range.GMin, "G 最小");
        Check.Equal(160, (int)range.GMax, "G 最大");
        Check.Equal(200, (int)range.BMin, "B 最小");
        Check.Equal(210, (int)range.BMax, "B 最大");
    }

    [Fact]
    public static void 区间生成_容差扩展()
    {
        var range = ColorRangeBuilder.Build(new[] { (100, 150, 200) }, tolerance: 12);
        Check.Equal(88, (int)range.RMin, "R 下界扩展");
        Check.Equal(112, (int)range.RMax, "R 上界扩展");
        Check.Equal(138, (int)range.GMin, "G 下界扩展");
        Check.Equal(212, (int)range.BMax, "B 上界扩展");
    }

    [Fact]
    public static void 区间生成_边界钳制到0_255()
    {
        var range = ColorRangeBuilder.Build(new[] { (5, 250, 1) }, tolerance: 10);
        Check.Equal(0, (int)range.RMin, "R 下界钳制到 0");
        Check.Equal(255, (int)range.GMax, "G 上界钳制到 255");
        Check.Equal(0, (int)range.BMin, "B 下界钳制到 0");
        Check.Equal(11, (int)range.BMax, "B 上界 1+10");
    }

    [Fact]
    public static void 区间生成_单采样()
    {
        var range = ColorRangeBuilder.Build(new[] { (200, 230, 255) }, tolerance: 0);
        Check.Equal(200, (int)range.RMin, "R");
        Check.Equal(200, (int)range.RMax, "R max");
        Check.True(range.Contains(200, 230, 255), "应包含原色");
        Check.False(range.Contains(0, 0, 0), "不应包含黑色");
    }

    [Fact]
    public static void 区间生成_空采样_抛异常()
    {
        Check.Throws<ArgumentException>(() => ColorRangeBuilder.Build(Array.Empty<(int, int, int)>()), "空采样应抛异常");
    }

    [Fact]
    public static void 区间生成_非法像素_抛异常()
    {
        Check.Throws<ArgumentOutOfRangeException>(() => ColorRangeBuilder.Build(new[] { (300, 0, 0) }), "越界像素应抛异常");
    }

    [Fact]
    public static void 区间扩展_向两侧扩展()
    {
        var range = ColorRangeBuilder.Expand(new ColorRange(100, 110, 150, 160, 200, 210), tolerance: 5);
        Check.Equal(95, (int)range.RMin, "R 下界");
        Check.Equal(115, (int)range.RMax, "R 上界");
        Check.Equal(150 - 5, (int)range.GMin, "G 下界");
    }

    [Fact]
    public static void 校准存储_保存加载往返()
    {
        var dir = TestTemp.NewDir();
        try
        {
            var store = new CalibrationStore(Path.Combine(dir, "calib.json"));
            var result = new CalibrationResult(new ColorRange(170, 250, 100, 240, 90, 230), new ColorRange(0, 50, 0, 50, 0, 80));
            store.Save(result);
            var loaded = store.Load();
            Check.Equal(170, (int)loaded.Bright.RMin, "亮 R 下界");
            Check.Equal(250, (int)loaded.Bright.RMax, "亮 R 上界");
            Check.Equal(0, (int)loaded.Dark.GMin, "暗 G 下界");
            Check.Equal(80, (int)loaded.Dark.BMax, "暗 B 上界");
        }
        finally { TestTemp.Delete(dir); }
    }

    [Fact]
    public static void 校准存储_文件缺失_返回默认()
    {
        var dir = TestTemp.NewDir();
        try
        {
            var store = new CalibrationStore(Path.Combine(dir, "missing.json"));
            var loaded = store.Load();
            Check.Equal(180, (int)loaded.Bright.RMin, "默认亮 R 下界");
            Check.Equal(90, (int)loaded.Dark.BMax, "默认暗 B 上界");
        }
        finally { TestTemp.Delete(dir); }
    }

    [Fact]
    public static void 校准存储_损坏文件_返回默认()
    {
        var dir = TestTemp.NewDir();
        try
        {
            var path = Path.Combine(dir, "corrupt.json");
            File.WriteAllText(path, "{not-json");
            var store = new CalibrationStore(path);
            var loaded = store.Load();
            Check.Equal(180, (int)loaded.Bright.RMin, "损坏时回退默认");
        }
        finally { TestTemp.Delete(dir); }
    }

    [Fact]
    public static void 校准存储_应用到设置()
    {
        var dir = TestTemp.NewDir();
        try
        {
            var store = new CalibrationStore(Path.Combine(dir, "calib.json"));
            store.Save(new CalibrationResult(new ColorRange(1, 2, 3, 4, 5, 6), new ColorRange(7, 8, 9, 10, 11, 12)));
            var settings = new AppSettings();
            store.ApplyTo(settings);
            Check.Equal(1, (int)settings.BrightRange.RMin, "设置亮区间已应用");
            Check.Equal(12, (int)settings.DarkRange.BMax, "设置暗区间已应用");
        }
        finally { TestTemp.Delete(dir); }
    }
}

/// <summary>测试临时目录工具（位于测试输出目录，自动清理）。</summary>
public static class TestTemp
{
    public static string NewDir()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "tmp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void Delete(string dir)
    {
        if (Directory.Exists(dir))
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* 忽略清理失败 */ }
        }
    }
}