using NaruttoTimer.Capture;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace NaruttoTimer.Recognition.ImageProcess;

/// <summary>字节/Mat 转换工具。</summary>
public static class MatUtil
{
    /// <summary>BGRA32 像素 → CV_8UC4 Mat（通道顺序 B,G,R,A）。</summary>
    public static Mat FromBgra(int width, int height, byte[] pixels)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), "宽度必须为正");
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), "高度必须为正");
        ArgumentNullException.ThrowIfNull(pixels);
        int expected = width * height * 4;
        if (pixels.Length < expected)
            throw new ArgumentException($"像素缓冲区长度不足：需要 {expected}，实际 {pixels.Length}", nameof(pixels));

        // 注意：Mat.SetArray 对多通道 Mat 不适用（按元素而非字节计数），直接按字节拷入底层缓冲区。
        var mat = new Mat(height, width, MatType.CV_8UC4);
        Marshal.Copy(pixels, 0, mat.Data, expected);
        return mat;
    }

    public static Mat FromBgra(CapturedFrame frame) => FromBgra(frame.Width, frame.Height, frame.Pixels);

    /// <summary>BGRA32 像素拷入已存在的 Mat（复用缓冲区，避免每帧新建 3.7MB Mat）。</summary>
    public static void CopyBgra(Mat destination, int width, int height, byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(pixels);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), "宽度必须为正");
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), "高度必须为正");
        int expected = width * height * 4;
        if (pixels.Length < expected)
            throw new ArgumentException($"像素缓冲区长度不足：需要 {expected}，实际 {pixels.Length}", nameof(pixels));

        if (destination.Rows != height || destination.Cols != width || destination.Type() != MatType.CV_8UC4)
            destination.Create(height, width, MatType.CV_8UC4);
        Marshal.Copy(pixels, 0, destination.Data, expected);
    }
}
