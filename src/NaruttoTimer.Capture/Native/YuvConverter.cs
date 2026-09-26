using System.Runtime.InteropServices;

namespace NaruttoTimer.Capture.Native;

/// <summary>YUV420P → BGRA32 转换（BT.601）。</summary>
public static class YuvConverter
{
    /// <summary>YUV420P → BGRA32，返回新分配的缓冲区（单次调用场景）。</summary>
    public static byte[] ToBgra(IntPtr framePtr, int width, int height)
    {
        var output = new byte[width * height * 4];
        ToBgra(framePtr, width, height, output);
        return output;
    }

    /// <summary>YUV420P → BGRA32，写入调用方提供的缓冲区（逐帧复用，避免每帧分配）。</summary>
    public static void ToBgra(IntPtr framePtr, int width, int height, byte[] destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var frame = Marshal.PtrToStructure<FFmpegNative.AVFrame>(framePtr);
        var data0 = frame.Data0; var data1 = frame.Data1; var data2 = frame.Data2;
        var ls0 = frame.Linesize0; var ls1 = frame.Linesize1; var ls2 = frame.Linesize2;
        if (ls1 == 0) ls1 = width / 2;
        if (ls2 == 0) ls2 = width / 2;

        int needed = width * height * 4;
        if (destination.Length < needed)
            throw new ArgumentException($"目标缓冲区不足：需要 {needed}，实际 {destination.Length}", nameof(destination));
        unsafe
        {
            fixed (byte* dst = destination)
            {
                byte* yPlane = (byte*)data0.ToPointer();
                byte* uPlane = (byte*)data1.ToPointer();
                byte* vPlane = (byte*)data2.ToPointer();
                byte* d = dst;
                for (int yy = 0; yy < height; yy++)
                {
                    int uvY = yy >> 1;
                    for (int xx = 0; xx < width; xx++)
                    {
                        int uvX = xx >> 1;
                        int yv = yPlane[yy * ls0 + xx];
                        int u = uPlane[uvY * ls1 + uvX] - 128;
                        int v = vPlane[uvY * ls2 + uvX] - 128;
                        int c = 298 * (yv - 16) + 128;
                        int r = (c + 409 * v) >> 8;
                        int g = (c - 100 * u - 208 * v) >> 8;
                        int b = (c + 516 * u) >> 8;
                        *d++ = (byte)Clamp(b);
                        *d++ = (byte)Clamp(g);
                        *d++ = (byte)Clamp(r);
                        *d++ = 255;
                    }
                }
            }
        }
    }

    private static int Clamp(int v) => v < 0 ? 0 : v > 255 ? 255 : v;
}
