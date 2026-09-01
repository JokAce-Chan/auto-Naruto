using System.Globalization;

namespace NaruttoTimer.Overlay;

/// <summary>置顶框显示逻辑（纯函数，便于单测）：格式化、字号、间距、颜色解析。</summary>
public static class OverlayFormat
{
    /// <summary>倒计时秒 → "0.00" 格式（两位小数，无方括号无标签）。</summary>
    public static string FormatSeconds(double seconds) => seconds.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>字号随窗口高度变化（比例 0.62，最小 8）。</summary>
    public static double FontSizeForHeight(double height) => Math.Max(8, height * 0.62);

    /// <summary>两数间距随窗口宽度变化（比例 0.06，最小 10）。</summary>
    public static double SpacingForWidth(double width) => Math.Max(10, width * 0.06);

    /// <summary>解析 #RRGGBB（可带或不带 #）为 RGB 字节。</summary>
    public static (byte R, byte G, byte B) ParseHexColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) throw new ArgumentException("颜色不能为空", nameof(hex));
        var s = hex.Trim().TrimStart('#');
        if (s.Length != 6 || !byte.TryParse(s.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r)
            || !byte.TryParse(s.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g)
            || !byte.TryParse(s.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
        {
            throw new ArgumentException($"非法颜色: {hex}（应为 #RRGGBB）", nameof(hex));
        }
        return (r, g, b);
    }

    /// <summary>校验透明度百分比（10~90），越界抛异常。</summary>
    public static int ValidateOpacityPercent(int percent)
    {
        if (percent < 10 || percent > 90)
            throw new ArgumentOutOfRangeException(nameof(percent), "透明度必须在 10%~90%");
        return percent;
    }
}