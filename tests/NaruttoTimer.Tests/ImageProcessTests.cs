using NaruttoTimer.Data;
using NaruttoTimer.Recognition.ImageProcess;
using OpenCvSharp;

namespace NaruttoTimer.Tests;

/// <summary>图像处理层测试：归一化裁剪 / BGRA 构造。</summary>
public static class ImageProcessTests
{
    // ── ImageCropper ──

    [Fact]
    public static void 归一化坐标_按帧宽高换算为像素矩形()
    {
        using var frame = new Mat(720, 1280, MatType.CV_8UC4, Scalar.All(0));
        var rect = ImageCropper.ToPixelRect(frame, new LabelRect(0.5, 0.25, 0.75, 0.5));
        Check.Equal(640, rect.X, "x");
        Check.Equal(180, rect.Y, "y");
        Check.Equal(320, rect.Width, "宽");
        Check.Equal(180, rect.Height, "高");
    }

    [Fact]
    public static void 超范围坐标_被clamp到帧内且至少1像素()
    {
        using var frame = new Mat(720, 1280, MatType.CV_8UC4, Scalar.All(0));
        var rect = ImageCropper.ToPixelRect(frame, new LabelRect(-0.5, -0.5, 1.5, 1.5));
        Check.Equal(0, rect.X, "左边界");
        Check.Equal(0, rect.Y, "上边界");
        Check.Equal(1280, rect.Width, "宽度铺满");
        Check.Equal(720, rect.Height, "高度铺满");

        var degenerate = ImageCropper.ToPixelRect(frame, new LabelRect(0.5, 0.5, 0.5, 0.5));
        Check.True(degenerate.Width >= 1 && degenerate.Height >= 1, "退化矩形至少 1x1");
    }

    [Fact]
    public static void 裁剪_返回独立拷贝()
    {
        using var frame = new Mat(720, 1280, MatType.CV_8UC4, Scalar.All(0));
        using var crop = ImageCropper.CropLabelArea(frame, new LabelRect(0.1, 0.1, 0.2, 0.2));
        Check.Equal(128, crop.Cols, "宽");
        Check.Equal(72, crop.Rows, "高");
        Check.Equal(4, crop.Channels(), "保留 BGRA 通道");
    }

    [Fact]
    public static void MatUtil_按BGRA字节构造()
    {
        var pixels = new byte[4 * 4 * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 7;      // B
            pixels[i + 1] = 8;  // G
            pixels[i + 2] = 9;  // R
            pixels[i + 3] = 255;
        }
        using var mat = MatUtil.FromBgra(4, 4, pixels);
        Check.Equal(4, mat.Cols, "宽");
        Check.Equal(4, mat.Rows, "高");
        var c = mat.At<Vec4b>(1, 2);
        Check.Equal(7, (int)c.Item0, "B");
        Check.Equal(8, (int)c.Item1, "G");
        Check.Equal(9, (int)c.Item2, "R");
        Check.Equal(255, (int)c.Item3, "A");
    }
}
