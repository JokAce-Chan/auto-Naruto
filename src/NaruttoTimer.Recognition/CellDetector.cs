using NaruttoTimer.Capture;
using NaruttoTimer.Data;
using NaruttoTimer.Rules;

namespace NaruttoTimer.Recognition;

/// <summary>检测到的一个菱形格（中心 x + 分类）。</summary>
public sealed record DetectedCell(int CenterX, CellState State);

/// <summary>
/// ROI 内的菱形格检测：列投影（亮/暗菱形像素）→ 聚簇 → 按等间距切分发光合并的宽簇 → 逐格分类。
/// 深蓝/灰蓝背景不再计入暗格，避免整行被当作一格；亮格用亮/橙红判定，暗格用青蓝判定。
/// 纯函数，便于单元测试。
/// </summary>
public static class CellDetector
{
    /// <summary>最大可接受格数（超出视为噪声，返回空）。</summary>
    public const int MaxCells = 8;

    /// <summary>默认菱形间距（px），用于发光合并时的等距切分。</summary>
    private const int DefaultSpacing = 21;

    public static List<DetectedCell> Detect(CapturedFrame frame, RoiConfig roi, RecognizerOptions options)
    {
        if (roi.Width <= 0 || roi.Height <= 0 || frame.Width <= 0 || frame.Height <= 0) return new();

        int x0 = Math.Clamp(roi.X, 0, frame.Width);
        int y0 = Math.Clamp(roi.Y, 0, frame.Height);
        int x1 = Math.Clamp(roi.X + roi.Width, 0, frame.Width);
        int y1 = Math.Clamp(roi.Y + roi.Height, 0, frame.Height);
        if (x1 - x0 < 6 || y1 - y0 < 6) return new();

        // 菱形体带：取 ROI 中间 50% 行（避开血条与下边缘）。
        int bandTop = y0 + (y1 - y0) / 4;
        int bandBottom = y1 - (y1 - y0) / 4;
        if (bandBottom <= bandTop) bandBottom = bandTop + 1;
        int bandH = bandBottom - bandTop;

        int roiW = x1 - x0;
        var cell = new int[roiW];
        var bright = new int[roiW];
        var dark = new int[roiW];
        for (int y = bandTop; y < bandBottom; y++)
        {
            int rowBase = (y * frame.Width + x0) * 4;
            for (int x = 0; x < roiW; x++)
            {
                int idx = rowBase + x * 4;
                byte b = frame.Pixels[idx], g = frame.Pixels[idx + 1], r = frame.Pixels[idx + 2];
                if (IsBright(r, g, b, options)) { bright[x]++; cell[x]++; }
                else if (IsDark(r, g, b, options)) { dark[x]++; cell[x]++; }
            }
        }

        int threshold = Math.Max(2, bandH / 6);

        // 列聚簇
        var clusters = new List<(int Start, int End)>();
        int cStart = -1;
        for (int x = 0; x < roiW; x++)
        {
            if (cell[x] >= threshold)
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

        // 合并近邻簇（间距 ≤ 3px 抗噪）
        var merged = new List<(int Start, int End)>();
        foreach (var c in clusters)
        {
            if (merged.Count > 0 && c.Start - merged[^1].End <= 3)
                merged[^1] = (merged[^1].Start, c.End);
            else
                merged.Add(c);
        }
        if (merged.Count == 0) return new();

        // 估计间距（分离簇的中点间隔中位数）
        int spacing = EstimateSpacing(merged, cell, threshold);
        if (spacing < 12) spacing = DefaultSpacing;
        if (spacing > 34) spacing = DefaultSpacing;

        int minWidth = Math.Max(4, spacing / 3);

        var result = new List<DetectedCell>();
        foreach (var (s, e) in merged)
        {
            int width = e - s + 1;
            if (width < minWidth) continue;

            // 该簇是否由多颗菱形发光合并而成
            int slots = width >= spacing * 3 / 2 ? (int)Math.Round((double)width / spacing) : 1;
            if (slots > MaxCells) slots = MaxCells;

            for (int k = 0; k < slots; k++)
            {
                int center;
                if (slots == 1)
                {
                    center = WeightedCenter(cell, s, e, x0);
                }
                else
                {
                    int span = Math.Max(1, e - s);
                    int cx = s + (int)((double)(2 * k + 1) * span / (2 * slots));
                    center = x0 + cx;
                }

                var state = ClassifyCluster(frame, x0, x1, bandTop, bandBottom, center, spacing, options);
                if (state != CellState.Unknown) result.Add(new DetectedCell(center, state));
            }
        }

        // 按中心从左到右排序
        result.Sort((a, b) => a.CenterX.CompareTo(b.CenterX));
        // 去重（相邻中心过近保留一个）
        var dedup = new List<DetectedCell>();
        foreach (var c in result)
        {
            if (dedup.Count > 0 && c.CenterX - dedup[^1].CenterX < spacing / 2) continue;
            dedup.Add(c);
        }

        return dedup.Count > MaxCells ? new() : dedup;
    }

    private static int EstimateSpacing(List<(int Start, int End)> clusters, int[] cell, int threshold)
    {
        var centers = new List<int>();
        foreach (var (s, e) in clusters) centers.Add(WeightedCenter(cell, s, e, 0));
        if (centers.Count < 2) return DefaultSpacing;
        var gaps = new List<int>();
        for (int i = 1; i < centers.Count; i++) gaps.Add(centers[i] - centers[i - 1]);
        gaps.Sort();
        return gaps[gaps.Count / 2];
    }

    private static int WeightedCenter(int[] cell, int s, int e, int offset)
    {
        long wsum = 0, csum = 0;
        for (int x = s; x <= e; x++) { wsum += (long)cell[x] * x; csum += cell[x]; }
        return offset + (int)(csum > 0 ? wsum / csum : (s + e) / 2);
    }

    // ── 颜色判定的功能谓词（蓝/橙两族 + 亮度区分亮暗）──
    // 说明：旧版用固定 RGB 盒，会随“蓝色常态/橙色突变”改变而失效；改用相对色相与亮度判定，
    // 蓝族（亮蓝核心、暗蓝/深青暗格）与橙/白突变亮格统一覆盖，深灰绿背景一并排除。

    private static bool IsBright(byte r, byte g, byte b, RecognizerOptions options)
    {
        // 橙色/白色突变亮格
        if (options.EffectiveOrangeRange.Contains(r, g, b)) return true;
        if (r >= 170 && g >= 170 && b >= 160) return true; // 亮蓝核心的白色高光
        // 蓝族亮核心
        if (IsBlueFamily(r, g, b) && IsBrightBlue(r, g, b)) return true;
        return false;
    }

    private static bool IsDark(byte r, byte g, byte b, RecognizerOptions options)
    {
        // 蓝族且非亮核心 → 暗蓝/暗青格
        return IsBlueFamily(r, g, b) && !IsBrightBlue(r, g, b);
    }

    /// <summary>蓝/青族：蓝色占优、红色低、亮度高于深色背景；排除绿灰背景与本底。</summary>
    private static bool IsBlueFamily(byte r, byte g, byte b) =>
        r <= 100 && b >= 95 && b >= g && (b - r) >= 35;

    /// <summary>亮蓝核心：蓝色突出且亮度高。</summary>
    private static bool IsBrightBlue(byte r, byte g, byte b) =>
        b >= 150 && g >= 80 && (b - g) >= 30;

    private static CellState ClassifyCluster(CapturedFrame frame, int x0, int x1, int bandTop, int bandBottom,
        int centerX, int spacing, RecognizerOptions options)
    {
        int halfW = Math.Clamp(spacing / 4, 3, 5);
        int sx0 = Math.Max(x0, centerX - halfW);
        int sx1 = Math.Min(x1 - 1, centerX + halfW);
        int bright = 0, dark = 0;

        for (int y = bandTop; y < bandBottom; y++)
        {
            int rowBase = y * frame.Width;
            for (int x = sx0; x <= sx1; x++)
            {
                int idx = (rowBase + x) * 4;
                byte b = frame.Pixels[idx], g = frame.Pixels[idx + 1], r = frame.Pixels[idx + 2];
                if (IsBright(r, g, b, options)) bright++;
                else if (IsDark(r, g, b, options)) dark++;
            }
        }

        int total = bright + dark;
        if (total == 0) return CellState.Unknown;
        return bright >= dark ? CellState.Bright : CellState.Dark;
    }
}