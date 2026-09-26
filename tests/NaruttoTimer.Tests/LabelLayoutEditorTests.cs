using NaruttoTimer.App;
using NaruttoTimer.Data;

namespace NaruttoTimer.Tests;

/// <summary>标注矩形编辑器测试（主界面预览拖拽与区域标注弹窗共用）。</summary>
public static class LabelLayoutEditorTests
{
    private const int W = 1280;
    private const int H = 720;

    private static LabelLayoutEditor NewEditor() => new(LabelImageConfig.CreateDefault());

    private static (double X, double Y) Center(LabelRect rect) =>
        ((rect.LeftTop.X + rect.RightBottom.X) / 2 * W, (rect.LeftTop.Y + rect.RightBottom.Y) / 2 * H);

    [Fact]
    public static void 命中_左条上边缘_返回顶部缩放把手()
    {
        var editor = NewEditor();
        var (x, _) = Center(editor.LeftBar);
        double top = editor.LeftBar.LeftTop.Y * H;
        Check.Equal(LabelHandle.BarsEdgeN, editor.HitTest(x, top, W, H), "上边缘应可缩放（左右条联动）");
    }

    [Fact]
    public static void 命中_左条内部_返回左条移动_右条同理()
    {
        var editor = NewEditor();
        var (lx, ly) = Center(editor.LeftBar);
        Check.Equal(LabelHandle.LeftBarMove, editor.HitTest(lx, ly, W, H), "左条内部");
        var (rx, ry) = Center(editor.RightBar);
        Check.Equal(LabelHandle.RightBarMove, editor.HitTest(rx, ry, W, H), "右条内部");
    }

    [Fact]
    public static void 命中_左条右边缘_返回缩放把手()
    {
        var editor = NewEditor();
        var (_, y) = Center(editor.LeftBar);
        double right = editor.LeftBar.RightBottom.X * W;
        Check.Equal(LabelHandle.BarsEdgeE, editor.HitTest(right, y, W, H), "右边缘应可缩放");
    }

    [Fact]
    public static void 命中_空白区域_返回None()
    {
        var editor = NewEditor();
        Check.Equal(LabelHandle.None, editor.HitTest(2, H - 2, W, H), "左下角空白");
        Check.Equal(LabelHandle.None, editor.HitTest(0, 0, 0, 0), "非法帧尺寸");
    }

    [Fact]
    public static void 拖动_左条水平移动_右条水平不受影响()
    {
        var editor = NewEditor();
        double x0 = editor.LeftBar.LeftTop.X;
        double rx0 = editor.RightBar.LeftTop.X;

        editor.Drag(LabelHandle.LeftBarMove, 20, 0, W, H);

        Check.Near(x0 + 20.0 / W, editor.LeftBar.LeftTop.X, 1e-9, "左条水平位移");
        Check.Near(rx0, editor.RightBar.LeftTop.X, 1e-9, "右条水平不受影响");
    }

    [Fact]
    public static void 拖动_条缩放_最小尺寸受保护()
    {
        var editor = NewEditor();
        double w0 = editor.LeftBar.Width;
        editor.Drag(LabelHandle.BarsEdgeE, 30, 0, W, H);
        Check.True(editor.LeftBar.Width > w0, "向东拖动应变宽");

        editor.Drag(LabelHandle.BarsEdgeE, -10000, 0, W, H);
        Check.True(editor.LeftBar.Width * W >= 8 - 1e-6, "宽度不应小于 8 像素");
    }

    [Fact]
    public static void 拖动_左右条同高同宽联动()
    {
        var editor = NewEditor();
        editor.Drag(LabelHandle.BarsEdgeE, 12, 0, W, H);
        editor.Drag(LabelHandle.BarsEdgeS, 0, 8, W, H);

        Check.Near(editor.LeftBar.Width, editor.RightBar.Width, 1e-6, "左右条等宽");
        Check.Near(editor.LeftBar.Height, editor.RightBar.Height, 1e-6, "左右条等高");
        Check.Near(editor.LeftBar.LeftTop.Y, editor.RightBar.LeftTop.Y, 1e-6, "顶边对齐");
    }

    [Fact]
    public static void 拖动_左条竖直移动_两条一起移动()
    {
        var editor = NewEditor();
        double topLeft = editor.LeftBar.LeftTop.Y;
        double topRight = editor.RightBar.LeftTop.Y;
        double xLeft = editor.LeftBar.LeftTop.X;
        double xRight = editor.RightBar.LeftTop.X;

        editor.Drag(LabelHandle.LeftBarMove, 0, 25, W, H);

        Check.Near(topLeft + 25.0 / H, editor.LeftBar.LeftTop.Y, 1e-9, "左条下移");
        Check.Near(topRight + 25.0 / H, editor.RightBar.LeftTop.Y, 1e-9, "右条跟随下移");
        Check.Near(xLeft, editor.LeftBar.LeftTop.X, 1e-9, "左条水平不变");
        Check.Near(xRight, editor.RightBar.LeftTop.X, 1e-9, "右条水平不变");
    }

    [Fact]
    public static void 拖动_不越出帧边界()
    {
        var editor = NewEditor();
        editor.Drag(LabelHandle.LeftBarMove, -100000, -100000, W, H);
        Check.True(editor.LeftBar.LeftTop.X >= 0 && editor.LeftBar.LeftTop.Y >= 0, "不应移出左上角");

        editor.Drag(LabelHandle.LeftBarMove, 100000, 100000, W, H);
        Check.True(editor.LeftBar.RightBottom.X <= 1 + 1e-9 && editor.LeftBar.RightBottom.Y <= 1 + 1e-9,
            "不应移出右下角");
    }
}
