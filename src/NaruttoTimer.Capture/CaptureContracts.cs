namespace NaruttoTimer.Capture;

/// <summary>一帧采集画面（BGRA32 像素，整帧未裁切）。</summary>
public sealed class CapturedFrame
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required byte[] Pixels { get; init; }
    public required DateTime Timestamp { get; init; }
    public long Sequence { get; init; }

    public void GetPixel(int x, int y, out byte b, out byte g, out byte r, out byte a)
    {
        int idx = (y * Width + x) * 4;
        b = Pixels[idx];
        g = Pixels[idx + 1];
        r = Pixels[idx + 2];
        a = Pixels[idx + 3];
    }
}

public enum CaptureState { Disconnected, Connecting, Connected, Reconnecting }

/// <summary>连接状态变化事件参数。</summary>
public sealed class CaptureStateChangedEventArgs : EventArgs
{
    public required CaptureState State { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// 读 H.264 流解码为 BGRA 帧，通过 FrameReady 事件逐帧抛出。
/// </summary>
public interface ICaptureSource
{
    CaptureState State { get; }
    double CurrentFps { get; }
    event EventHandler<CapturedFrame>? FrameReady;
    event EventHandler<CaptureStateChangedEventArgs>? StateChanged;

    Task StartAsync(CancellationToken ct);
    Task StopAsync();
}
