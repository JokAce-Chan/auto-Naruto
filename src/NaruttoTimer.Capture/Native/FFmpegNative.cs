using System.Runtime.InteropServices;

namespace NaruttoTimer.Capture.Native;

/// <summary>FFmpeg avcodec/avutil 最小互操作绑定（针对 avcodec-62.dll / avutil-60.dll）。</summary>
public static class FFmpegNative
{
    public const int AV_CODEC_ID_H264 = 27;

    public const int AVERROR_EAGAIN = -11;
    public const int AVERROR_EOF = -541478725; // -(MKTAG('E','O','F',' '))

    private static bool _resolverSet;

    /// <summary>注册 DLL 解析器，从指定目录加载 FFmpeg 库。</summary>
    public static void EnsureResolver(string ffmpegDirectory)
    {
        if (_resolverSet) return;
        _resolverSet = true;
        NativeLibrary.SetDllImportResolver(typeof(FFmpegNative).Assembly, (name, assembly, path) =>
        {
            var candidates = new[]
            {
                Path.Combine(ffmpegDirectory, name + ".dll"),
                Path.Combine(ffmpegDirectory, name),
            };
            foreach (var c in candidates)
            {
                if (File.Exists(c)) return NativeLibrary.Load(c);
            }
            return IntPtr.Zero;
        });
    }

    // ── avcodec ──
    [DllImport("avcodec-62", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr avcodec_find_decoder(int codecId);

    [DllImport("avcodec-62", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr avcodec_alloc_context3(IntPtr codec);

    [DllImport("avcodec-62", CallingConvention = CallingConvention.Cdecl)]
    public static extern int avcodec_open2(IntPtr avctx, IntPtr codec, IntPtr options);

    [DllImport("avcodec-62", CallingConvention = CallingConvention.Cdecl)]
    public static extern int avcodec_send_packet(IntPtr avctx, IntPtr avpkt);

    [DllImport("avcodec-62", CallingConvention = CallingConvention.Cdecl)]
    public static extern int avcodec_receive_frame(IntPtr avctx, IntPtr frame);

    [DllImport("avcodec-62", CallingConvention = CallingConvention.Cdecl)]
    public static extern void avcodec_free_context(ref IntPtr avctx);

    [DllImport("avcodec-62", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr av_parser_init(int codecId);

    [DllImport("avcodec-62", CallingConvention = CallingConvention.Cdecl)]
    public static extern int av_parser_parse2(IntPtr parser, IntPtr avctx,
        out IntPtr poutbuf, out int poutbuf_size,
        IntPtr buf, int buf_size,
        long pts, long dts, long pos);

    [DllImport("avcodec-62", CallingConvention = CallingConvention.Cdecl)]
    public static extern void av_parser_close(IntPtr parser);

    [DllImport("avcodec-62", CallingConvention = CallingConvention.Cdecl)]
    public static extern int av_strerror(int errnum, IntPtr errbuf, int errbufSize);

    // ── avutil ──
    [DllImport("avutil-60", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr av_frame_alloc();

    [DllImport("avutil-60", CallingConvention = CallingConvention.Cdecl)]
    public static extern void av_frame_free(ref IntPtr frame);

    [DllImport("avcodec-62", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr av_packet_alloc();

    [DllImport("avcodec-62", CallingConvention = CallingConvention.Cdecl)]
    public static extern void av_packet_free(ref IntPtr pkt);

    [DllImport("avcodec-62", CallingConvention = CallingConvention.Cdecl)]
    public static extern void av_packet_unref(IntPtr pkt);

    [DllImport("avutil-60", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr av_malloc(UIntPtr size);

    [DllImport("avutil-60", CallingConvention = CallingConvention.Cdecl)]
    public static extern void av_log_set_level(int level);

    [DllImport("avutil-60", CallingConvention = CallingConvention.Cdecl)]
    public static extern void av_free(IntPtr ptr);

    public static string ErrorString(int code)
    {
        var buf = av_malloc(new UIntPtr(256));
        if (buf == IntPtr.Zero) return code.ToString();
        try
        {
            av_strerror(code, buf, 256);
            return Marshal.PtrToStringAnsi(buf) ?? code.ToString();
        }
        finally { av_free(buf); }
    }

    /// <summary>AVFrame 前部字段（data[8] + linesize[8]）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AVFrame
    {
        public IntPtr Data0, Data1, Data2, Data3, Data4, Data5, Data6, Data7;
        public int Linesize0, Linesize1, Linesize2, Linesize3, Linesize4, Linesize5, Linesize6, Linesize7;
        public IntPtr ExtendedData;
        public int Width, Height;
        public int NbSamples;
        public int Format;
    }

    /// <summary>AVPacket 前部字段。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AVPacket
    {
        public IntPtr Buf;
        public long Pts, Dts;
        public IntPtr Data;
        public int Size;
        public int StreamIndex;
        public int Flags;
        public IntPtr SideData;
        public int SideDataElems;
        public long Duration, Pos;
    }
}
