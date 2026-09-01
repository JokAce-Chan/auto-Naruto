using NaruttoTimer.Data;

namespace NaruttoTimer.Calibration;

/// <summary>从采样像素生成亮/暗颜色区间（最小~最大，带容差扩展）。纯函数，便于单测。</summary>
public static class ColorRangeBuilder
{
    /// <summary>从一组采样像素生成区间，各通道向两侧扩展 tolerance（钳制 0~255）。</summary>
    public static ColorRange Build(IEnumerable<(int R, int G, int B)> samples, int tolerance = 12)
    {
        if (tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance), "容差不能为负");
        int count = 0;
        int rMin = 255, rMax = 0, gMin = 255, gMax = 0, bMin = 255, bMax = 0;
        foreach (var (r, g, b) in samples)
        {
            if (r < 0 || r > 255 || g < 0 || g > 255 || b < 0 || b > 255)
                throw new ArgumentOutOfRangeException(nameof(samples), $"采样像素越界: ({r},{g},{b})");
            rMin = Math.Min(rMin, r); rMax = Math.Max(rMax, r);
            gMin = Math.Min(gMin, g); gMax = Math.Max(gMax, g);
            bMin = Math.Min(bMin, b); bMax = Math.Max(bMax, b);
            count++;
        }
        if (count == 0) throw new ArgumentException("至少需要一个采样像素", nameof(samples));

        return new ColorRange(
            (byte)Math.Clamp(rMin - tolerance, 0, 255), (byte)Math.Clamp(rMax + tolerance, 0, 255),
            (byte)Math.Clamp(gMin - tolerance, 0, 255), (byte)Math.Clamp(gMax + tolerance, 0, 255),
            (byte)Math.Clamp(bMin - tolerance, 0, 255), (byte)Math.Clamp(bMax + tolerance, 0, 255));
    }

    /// <summary>在既有区间基础上向外扩展容差。</summary>
    public static ColorRange Expand(ColorRange range, int tolerance)
    {
        if (tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance), "容差不能为负");
        return new ColorRange(
            (byte)Math.Clamp(range.RMin - tolerance, 0, 255), (byte)Math.Clamp(range.RMax + tolerance, 0, 255),
            (byte)Math.Clamp(range.GMin - tolerance, 0, 255), (byte)Math.Clamp(range.GMax + tolerance, 0, 255),
            (byte)Math.Clamp(range.BMin - tolerance, 0, 255), (byte)Math.Clamp(range.BMax + tolerance, 0, 255));
    }
}