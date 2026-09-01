using NaruttoTimer.Capture;
using NaruttoTimer.Data;
using NaruttoTimer.Rules;

namespace NaruttoTimer.Recognition;

/// <summary>检测到的一个菱形格（中心 x + 分类）。</summary>
public sealed record DetectedCell(int CenterX, CellState State);

/// <summary>
/// ROI 内的菱形格检测：列投影聚类（亮/暗/突变色像素）→ 合并 → 逐格颜色归类。
/// 纯函数，便于单元测试。
/// </summary>
public static class CellDetector
{
    /// <summary>最大可接受格数（超出视为噪声，返回空）。</summary>
    public const int MaxCells = 8;

    public static List<DetectedCell> Detect(CapturedFrame frame, RoiConfig roi, RecognizerOptions options)
    {
        if (roi.Width <= 0 || roi.Height <= 0 || frame.Width <= 0 || frame.Height <= 0) return new();

        int x0 = Math.Clamp(roi.X, 0, frame.Width);
        int y0 = Math.Clamp(roi.Y, 0, frame.Height);
        int x1 = Math.Clamp(roi.X + roi.Width, 0, frame.Width);
        int y1 = Math.Clamp(roi.Y + roi.Height, 0, frame.Height);
        if (x1 - x0 < 4 || y1 - y0 < 4) return new();

        int bandTop = y0 + (y1 - y0) / 4;
        int bandBottom = y1 - (y1 - y0) / 4; // 中间 50% 行带
        if (bandBottom <= bandTop) bandBottom = bandTop + 1;

        int roiW = x1 - x0;
        var colScore = new int[roiW];
        for (int y = bandTop; y < bandBottom; y++)
        {
            int rowBase = (y * frame.Width + x0) * 4;
            for (int x = 0; x < roiW; x++)
            {
                int idx = rowBase + x * 4;
                byte b = frame.Pixels[idx], g = frame.Pixels[idx + 1], r = frame.Pixels[idx + 2];
                if (IsCellColor(r, g, b, options)) colScore[x]++;
            }
        }

        int threshold = Math.Max(2, (bandBottom - bandTop) / 4);

        // 列聚类
        var clusters = new List<(int Start, int End)>();
        int cStart = -1;
        for (int x = 0; x < roiW; x++)
        {
            if (colScore[x] >= threshold)
            {
                if (cStart < 0) cStart = x;
            }
            else if (cStart >= 0)
            {
                if (x - cStart >= 2) clusters.Add((cStart, x - 1));
                cStart = -1;
            }
        }
        if (cStart >= 0 && roiW - cStart >= 2) clusters.Add((cStart, roiW - 1));

        // 合并间距 ≤3px 的近邻簇（抗噪）
        var merged = new List<(int Start, int End)>();
        foreach (var c in clusters)
        {
            if (merged.Count > 0 && c.Start - merged[^1].End <= 3)
                merged[^1] = (merged[^1].Start, c.End);
            else
                merged.Add(c);
        }

        int minWidth = Math.Max(3, roi.Width / 40);
        var result = new List<DetectedCell>();
        foreach (var (s, e) in merged)
        {
            int width = e - s + 1;
            if (width < minWidth) continue; // 过滤噪声小簇

            long wsum = 0, csum = 0;
            for (int x = s; x <= e; x++) { wsum += (long)colScore[x] * x; csum += colScore[x]; }
            int centerX = x0 + (int)(csum > 0 ? wsum / csum : (s + e) / 2);

            result.Add(new DetectedCell(centerX, ClassifyCluster(frame, x0, x1, bandTop, bandBottom, centerX, s, e, options)));
        }

        return result.Count > MaxCells ? new() : result;
    }

    private static bool IsCellColor(byte r, byte g, byte b, RecognizerOptions options) =>
        options.BrightRange.Contains(r, g, b) ||
        options.DarkRange.Contains(r, g, b) ||
        options.EffectiveOrangeRange.Contains(r, g, b);

    private static CellState ClassifyCluster(CapturedFrame frame, int x0, int x1, int bandTop, int bandBottom,
        int centerX, int clusterStart, int clusterEnd, RecognizerOptions options)
    {
        int halfW = Math.Max(3, (clusterEnd - clusterStart + 1) / 4);
        int sx0 = Math.Max(x0, centerX - halfW);
        int sx1 = Math.Min(x1 - 1, centerX + halfW);
        int bright = 0, dark = 0, orange = 0;

        for (int y = bandTop; y < bandBottom; y++)
        {
            int rowBase = y * frame.Width;
            for (int x = sx0; x <= sx1; x++)
            {
                int idx = (rowBase + x) * 4;
                byte b = frame.Pixels[idx], g = frame.Pixels[idx + 1], r = frame.Pixels[idx + 2];
                if (options.EffectiveOrangeRange.Contains(r, g, b)) orange++;
                else if (options.BrightRange.Contains(r, g, b)) bright++;
                else if (options.DarkRange.Contains(r, g, b)) dark++;
            }
        }

        int total = bright + dark + orange;
        if (total == 0) return CellState.Unknown;
        // 突变橙红归入 Bright；否则按亮/暗多数归类
        if (orange > 0 && orange * 2 >= total) return CellState.Bright;
        return bright >= dark ? CellState.Bright : CellState.Dark;
    }
}