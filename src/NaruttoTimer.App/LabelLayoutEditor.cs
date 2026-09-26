using NaruttoTimer.Data;

namespace NaruttoTimer.App;

/// <summary>主界面预览 / 区域标注弹窗共用的命中部。</summary>
public enum LabelHandle
{
    None,

    /// <summary>条内部（由 <see cref="LabelLayoutEditor.HitTest"/> 具体化为 LeftBarMove / RightBarMove）。</summary>
    BarsMove,

    LeftBarMove,
    RightBarMove,
    BarsEdgeN,
    BarsEdgeS,
    BarsEdgeW,
    BarsEdgeE,
    BarsCornerNW,
    BarsCornerNE,
    BarsCornerSW,
    BarsCornerSE,
}

/// <summary>
/// 标注矩形编辑器（归一化坐标）：左右能量条强制「同高同宽」，竖直方向联动
/// （对齐源 LabelEnergyAIView 行为）。
/// </summary>
public sealed class LabelLayoutEditor
{
    private const double MinSize = 8.0; // 帧像素

    private readonly LabelImageConfig _config;

    public LabelLayoutEditor(LabelImageConfig config)
    {
        _config = config;
    }

    public LabelImageConfig Config => _config;

    public LabelRect LeftBar => _config.EnergyBar.Left;
    public LabelRect RightBar => _config.EnergyBar.Right;

    /// <summary>命中最短像素距离。</summary>
    public double HitTolerance { get; set; } = 14.0;

    public LabelHandle HitTest(double pixelX, double pixelY, int frameWidth, int frameHeight)
    {
        if (frameWidth <= 0 || frameHeight <= 0) return LabelHandle.None;
        double nx = pixelX / frameWidth;
        double ny = pixelY / frameHeight;
        double tolX = HitTolerance / frameWidth;
        double tolY = HitTolerance / frameHeight;

        var left = Describe(nx, ny, LeftBar, tolX, tolY);
        if (left != LabelHandle.None) return ToBarHandle(left, leftBar: true);

        var right = Describe(nx, ny, RightBar, tolX, tolY);
        if (right != LabelHandle.None) return ToBarHandle(right, leftBar: false);

        return LabelHandle.None;
    }

    public void Drag(LabelHandle handle, double deltaXPixels, double deltaYPixels, int frameWidth, int frameHeight)
    {
        if (handle == LabelHandle.None || frameWidth <= 0 || frameHeight <= 0) return;
        double dx = deltaXPixels / frameWidth;
        double dy = deltaYPixels / frameHeight;
        double minW = MinSize / frameWidth;
        double minH = MinSize / frameHeight;

        switch (handle)
        {
            case LabelHandle.LeftBarMove:
                ShiftHorizontal(LeftBar, dx);
                ShiftVerticalBoth(dy);
                break;

            case LabelHandle.RightBarMove:
                ShiftHorizontal(RightBar, dx);
                ShiftVerticalBoth(dy);
                break;

            default:
                ResizeBars(handle, dx, dy, minW, minH);
                break;
        }
    }

    // ── 命中描述 ──

    private static LabelHandle Describe(double nx, double ny, LabelRect rect, double tolX, double tolY)
    {
        if (rect == null) return LabelHandle.None;
        bool inX = nx >= rect.LeftTop.X - tolX && nx <= rect.RightBottom.X + tolX;
        bool inY = ny >= rect.LeftTop.Y - tolY && ny <= rect.RightBottom.Y + tolY;
        if (!inX || !inY) return LabelHandle.None;

        bool nearW = Math.Abs(nx - rect.LeftTop.X) <= tolX;
        bool nearE = Math.Abs(nx - rect.RightBottom.X) <= tolX;
        bool nearN = Math.Abs(ny - rect.LeftTop.Y) <= tolY;
        bool nearS = Math.Abs(ny - rect.RightBottom.Y) <= tolY;

        if (nearW && nearN) return LabelHandle.BarsCornerNW;
        if (nearE && nearN) return LabelHandle.BarsCornerNE;
        if (nearW && nearS) return LabelHandle.BarsCornerSW;
        if (nearE && nearS) return LabelHandle.BarsCornerSE;
        if (nearW) return LabelHandle.BarsEdgeW;
        if (nearE) return LabelHandle.BarsEdgeE;
        if (nearN) return LabelHandle.BarsEdgeN;
        if (nearS) return LabelHandle.BarsEdgeS;
        return LabelHandle.BarsMove;
    }

    private static LabelHandle ToBarHandle(LabelHandle probe, bool leftBar) =>
        probe == LabelHandle.BarsMove ? (leftBar ? LabelHandle.LeftBarMove : LabelHandle.RightBarMove) : probe;

    // ── 变换 ──

    private static void ShiftHorizontal(LabelRect rect, double dx)
    {
        double width = rect.Width;
        rect.LeftTop.X = Clamp(rect.LeftTop.X + dx, 0, 1 - width);
        rect.RightBottom.X = rect.LeftTop.X + width;
    }

    /// <summary>左右条竖直联动（顶边一起移动）。</summary>
    private void ShiftVerticalBoth(double dy)
    {
        double height = LeftBar.Height;
        double top = Clamp(LeftBar.LeftTop.Y + dy, 0, 1 - height);
        foreach (var rect in new[] { LeftBar, RightBar })
        {
            rect.LeftTop.Y = top;
            rect.RightBottom.Y = top + height;
        }
    }

    /// <summary>左右条同步缩放：宽高同时变化，保持两条完全一致。</summary>
    private void ResizeBars(LabelHandle handle, double dx, double dy, double minW, double minH)
    {
        bool west = handle is LabelHandle.BarsEdgeW or LabelHandle.BarsCornerNW or LabelHandle.BarsCornerSW;
        bool east = handle is LabelHandle.BarsEdgeE or LabelHandle.BarsCornerNE or LabelHandle.BarsCornerSE;
        bool north = handle is LabelHandle.BarsEdgeN or LabelHandle.BarsCornerNW or LabelHandle.BarsCornerNE;
        bool south = handle is LabelHandle.BarsEdgeS or LabelHandle.BarsCornerSW or LabelHandle.BarsCornerSE;

        foreach (var rect in new[] { LeftBar, RightBar })
        {
            if (west) rect.LeftTop.X = Clamp(rect.LeftTop.X + dx, 0, rect.RightBottom.X - minW);
            if (east) rect.RightBottom.X = Clamp(rect.RightBottom.X + dx, rect.LeftTop.X + minW, 1);
            if (north) rect.LeftTop.Y = Clamp(rect.LeftTop.Y + dy, 0, rect.RightBottom.Y - minH);
            if (south) rect.RightBottom.Y = Clamp(rect.RightBottom.Y + dy, rect.LeftTop.Y + minH, 1);
        }
    }

    private static double Clamp(double value, double min, double max) =>
        max < min ? min : Math.Max(min, Math.Min(max, value));
}
