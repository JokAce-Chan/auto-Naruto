using NaruttoTimer.Overlay;

namespace NaruttoTimer.Tests;

/// <summary>P5 置顶框纯逻辑测试（WPF 窗口本身由 App 侧实例化，测试只覆盖可单测的格式逻辑）。</summary>
public static class OverlayFormatTests
{
    [Fact]
    public static void 格式化_两位小数()
    {
        Check.Equal("15.00", OverlayFormat.FormatSeconds(15.0), "15 秒");
        Check.Equal("0.00", OverlayFormat.FormatSeconds(0.0), "0 秒");
        Check.Equal("9.50", OverlayFormat.FormatSeconds(9.5), "9.5 秒");
        Check.Equal("7.78", OverlayFormat.FormatSeconds(7.777), "7.777 秒四舍五入");
    }

    [Fact]
    public static void 字号_随高度()
    {
        Check.Near(64 * 0.62, OverlayFormat.FontSizeForHeight(64), 0.001, "高度 64 的字号");
        Check.Near(100 * 0.62, OverlayFormat.FontSizeForHeight(100), 0.001, "高度 100 的字号");
        Check.Near(8.0, OverlayFormat.FontSizeForHeight(5), 0.001, "最小字号 8");
    }

    [Fact]
    public static void 间距_随宽度()
    {
        Check.Near(220 * 0.06, OverlayFormat.SpacingForWidth(220), 0.001, "宽度 220 的间距");
        Check.Near(10.0, OverlayFormat.SpacingForWidth(50), 0.001, "最小间距 10");
    }

    [Fact]
    public static void 颜色解析_标准格式()
    {
        var c = OverlayFormat.ParseHexColor("#1F9D55");
        Check.Equal(0x1F, (int)c.R, "R");
        Check.Equal(0x9D, (int)c.G, "G");
        Check.Equal(0x55, (int)c.B, "B");
    }

    [Fact]
    public static void 颜色解析_无井号_小写()
    {
        var c = OverlayFormat.ParseHexColor("e03e3e");
        Check.Equal(0xE0, (int)c.R, "R");
        Check.Equal(0x3E, (int)c.G, "G");
        Check.Equal(0x3E, (int)c.B, "B");
    }

    [Fact]
    public static void 颜色解析_非法_抛异常()
    {
        Check.Throws<ArgumentException>(() => OverlayFormat.ParseHexColor(""), "空串");
        Check.Throws<ArgumentException>(() => OverlayFormat.ParseHexColor("#12345"), "长度不足");
        Check.Throws<ArgumentException>(() => OverlayFormat.ParseHexColor("zzz999"), "非十六进制");
    }

    [Fact]
    public static void 透明度校验()
    {
        Check.Equal(10, OverlayFormat.ValidateOpacityPercent(10), "下限合法");
        Check.Equal(90, OverlayFormat.ValidateOpacityPercent(90), "上限合法");
        Check.Throws<ArgumentOutOfRangeException>(() => OverlayFormat.ValidateOpacityPercent(9), "低于 10 抛异常");
        Check.Throws<ArgumentOutOfRangeException>(() => OverlayFormat.ValidateOpacityPercent(91), "高于 90 抛异常");
    }
}