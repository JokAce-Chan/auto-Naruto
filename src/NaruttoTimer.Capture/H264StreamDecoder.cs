using System.Runtime.InteropServices;
using NaruttoTimer.Capture.Native;

namespace NaruttoTimer.Capture;

/// <summary>
/// H.264 Annex B 流解码器：按 start code 分包（不依赖 av_parser，部分 FFmpeg 构建禁用了 parser），
/// 每 NAL 送入 avcodec 解码，YUV420→BGRA 输出。
/// </summary>
public sealed class H264StreamDecoder : IDisposable
{
    private const int PendingCapacity = 2 * 1024 * 1024;

    private readonly IntPtr _codecCtx;
    private readonly IntPtr _frame;
    private readonly IntPtr _packet;
    private readonly byte[] _pending = new byte[PendingCapacity];
    private int _pendingLen;
    private readonly object _lock = new();

    /// <summary>解码出帧：width, height, BGRA 像素, 时间戳。</summary>
    public event Action<int, int, byte[], DateTime>? FrameDecoded;

    public H264StreamDecoder(string ffmpegDirectory)
    {
        FFmpegNative.EnsureResolver(ffmpegDirectory);
        FFmpegNative.av_log_set_level(-8); // AV_LOG_QUIET
        var codec = FFmpegNative.avcodec_find_decoder(FFmpegNative.AV_CODEC_ID_H264);
        if (codec == IntPtr.Zero) throw new InvalidOperationException("找不到 H.264 解码器");
        _codecCtx = FFmpegNative.avcodec_alloc_context3(codec);
        if (_codecCtx == IntPtr.Zero) throw new InvalidOperationException("无法分配解码上下文");
        int rc = FFmpegNative.avcodec_open2(_codecCtx, codec, IntPtr.Zero);
        if (rc < 0) throw new InvalidOperationException($"打开 H.264 解码器失败: {FFmpegNative.ErrorString(rc)}");
        _frame = FFmpegNative.av_frame_alloc();
        _packet = FFmpegNative.av_packet_alloc();
    }

    public void Feed(byte[] data, int offset, int count)
    {
        if (count <= 0) return;
        lock (_lock)
        {
            if (_pendingLen + count > _pending.Length)
            {
                // 空间不足时丢弃最旧字节，保证新数据可写入
                int drop = _pendingLen + count - _pending.Length;
                if (drop > 0 && drop < _pendingLen)
                {
                    Buffer.BlockCopy(_pending, drop, _pending, 0, _pendingLen - drop);
                    _pendingLen -= drop;
                }
                else if (drop >= _pendingLen)
                {
                    _pendingLen = 0;
                }
            }
            Array.Copy(data, offset, _pending, _pendingLen, count);
            _pendingLen += count;
            ParsePending();
        }
    }

    /// <summary>将缓冲尾部当作完整 NAL 解码（流结束时调用，避免丢失最后一帧）。</summary>
    public void Flush()
    {
        lock (_lock)
        {
            if (_pendingLen <= 0) return;
            var handle = GCHandle.Alloc(_pending, GCHandleType.Pinned);
            try
            {
                DecodePacket(handle.AddrOfPinnedObject(), _pendingLen);
            }
            finally
            {
                handle.Free();
                _pendingLen = 0;
            }
        }
    }

    private void ParsePending()
    {
        var handle = GCHandle.Alloc(_pending, GCHandleType.Pinned);
        try
        {
            IntPtr basePtr = handle.AddrOfPinnedObject();
            int len = _pendingLen;

            // 定位第一个 start code（丢弃之前的不完整字节）
            int pos = 0;
            while (pos + 3 <= len && !IsStartCode(_pending, pos, out _)) pos++;
            if (pos + 3 > len)
            {
                _pendingLen = 0; // 尚无完整 start code，全部丢弃
                return;
            }

            int nalStart = pos;
            int scan = pos + StartCodeLenAt(_pending, pos);
            while (scan + 3 <= len)
            {
                if (IsStartCode(_pending, scan, out int scLen))
                {
                    DecodePacket(IntPtr.Add(basePtr, nalStart), scan - nalStart);
                    nalStart = scan;
                    scan += scLen;
                }
                else
                {
                    scan++;
                }
            }

            // 尾部 nalStart..len 是未完成 NAL，保留等待后续数据
            int remain = len - nalStart;
            if (remain > 0)
            {
                Buffer.BlockCopy(_pending, nalStart, _pending, 0, remain);
                _pendingLen = remain;
            }
            else
            {
                _pendingLen = 0;
            }
        }
        finally { handle.Free(); }
    }

    private static bool IsStartCode(byte[] b, int i, out int len)
    {
        if (i + 4 <= b.Length && b[i] == 0 && b[i + 1] == 0 && b[i + 2] == 0 && b[i + 3] == 1) { len = 4; return true; }
        if (i + 3 <= b.Length && b[i] == 0 && b[i + 1] == 0 && b[i + 2] == 1) { len = 3; return true; }
        len = 0;
        return false;
    }

    private static int StartCodeLenAt(byte[] b, int i) => b[i] == 0 && b[i + 1] == 0 && b[i + 2] == 0 && b[i + 3] == 1 ? 4 : 3;

    private void DecodePacket(IntPtr data, int size)
    {
        if (size <= 0) return;
        var pkt = new FFmpegNative.AVPacket { Data = data, Size = size };
        Marshal.StructureToPtr(pkt, _packet, false);
        int send = FFmpegNative.avcodec_send_packet(_codecCtx, _packet);
        FFmpegNative.av_packet_unref(_packet);
        if (send < 0 && send != FFmpegNative.AVERROR_EAGAIN) return;

        while (true)
        {
            int recv = FFmpegNative.avcodec_receive_frame(_codecCtx, _frame);
            if (recv == FFmpegNative.AVERROR_EAGAIN || recv == FFmpegNative.AVERROR_EOF) break;
            if (recv < 0) break;
            var frame = Marshal.PtrToStructure<FFmpegNative.AVFrame>(_frame);
            int w = frame.Width, h = frame.Height;
            if (w > 0 && h > 0 && frame.Format == 0) // AV_PIX_FMT_YUV420P == 0
            {
                var bgra = YuvConverter.ToBgra(_frame, w, h);
                FrameDecoded?.Invoke(w, h, bgra, DateTime.UtcNow);
            }
        }
    }

    public void Dispose()
    {
        Flush();
        if (_frame != IntPtr.Zero) { var f = _frame; FFmpegNative.av_frame_free(ref f); }
        if (_packet != IntPtr.Zero) { var p = _packet; FFmpegNative.av_packet_free(ref p); }
        if (_codecCtx != IntPtr.Zero) { var c = _codecCtx; FFmpegNative.avcodec_free_context(ref c); }
    }
}