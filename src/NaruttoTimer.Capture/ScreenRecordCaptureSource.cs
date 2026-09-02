using System.Diagnostics;

namespace NaruttoTimer.Capture;

/// <summary>采集参数。</summary>
public sealed record CaptureOptions(
    string AdbPath,
    string DeviceSerial,
    int VideoWidth = 1280,
    int VideoHeight = 720,
    int CropTopRows = 150,
    int MaxFps = 30,
    long BitRate = 4_000_000,
    int TimeLimitSeconds = 180);

/// <summary>
/// 采集源：通过 adb exec-out screenrecord（原始 H.264 stdout）实时取帧。
/// screenrecord 每 180 秒自动结束 → 自动重启；设备断开 → 自动重连。
/// </summary>
public sealed class ScreenRecordCaptureSource : ICaptureSource
{
    private readonly CaptureOptions _options;
    private readonly AdbCli _adb;
    private readonly string _ffmpegDirectory;
    private readonly FpsCounter _fps = new();
    private long _sequence;
    private string? _lastStderrLine;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public CaptureState State { get; private set; } = CaptureState.Disconnected;
    public double CurrentFps => _fps.Current;
    public string? LastStderrLine => _lastStderrLine;
    public string DeviceSerial => _options.DeviceSerial;

    public event EventHandler<CapturedFrame>? FrameReady;
    public event EventHandler<CaptureStateChangedEventArgs>? StateChanged;

    public ScreenRecordCaptureSource(CaptureOptions options, string ffmpegDirectory)
    {
        _options = options;
        _adb = new AdbCli(options.AdbPath);
        _ffmpegDirectory = ffmpegDirectory;
    }

    public Task StartAsync(CancellationToken ct)
    {
        if (_loopTask != null && !_loopTask.IsCompleted) return Task.CompletedTask;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loopTask = Task.Run(() => RunLoopAsync(_cts.Token));
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_loopTask != null)
        {
            try { await _loopTask.ConfigureAwait(false); } catch { /* 忽略 */ }
        }
        _cts?.Dispose();
        _cts = null;
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                SetState(CaptureState.Reconnecting, ex.Message);
            }
            if (ct.IsCancellationRequested) break;
            try { await Task.Delay(1500, ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
        }
        SetState(CaptureState.Disconnected, "已停止");
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        SetState(CaptureState.Connecting, "启动 screenrecord 视频流");
        using var decoder = new H264StreamDecoder(_ffmpegDirectory);
        decoder.FrameDecoded += OnFrameDecoded;

        string[] args =
        {
            "-s", _options.DeviceSerial,
            "exec-out", "screenrecord",
            "--output-format=h264",
            $"--size={_options.VideoWidth}x{_options.VideoHeight}",
            $"--bit-rate={_options.BitRate}",
            $"--time-limit={_options.TimeLimitSeconds}",
            "-",
        };

        Process proc;
        Stream stdout;
        try
        {
            proc = _adb.StartExecOut(args, out stdout);
        }
        catch (Exception ex)
        {
            SetState(CaptureState.Reconnecting, $"启动失败: {ex.Message}");
            return;
        }

        SetState(CaptureState.Connected, "视频流已建立");
        _fps.Reset();
        var buf = new byte[128 * 1024];
        var stderrTask = DrainStderrAsync(proc);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int n = await stdout.ReadAsync(buf.AsMemory(0, buf.Length), ct).ConfigureAwait(false);
                if (n == 0) break; // 流结束（正常结束或断开）
                decoder.Feed(buf, 0, n);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        finally
        {
            if (!proc.HasExited) { try { proc.Kill(); } catch { } }
            proc.Dispose();
            try { stderrTask.Wait(1000); } catch { /* 忽略 */ }
        }
    }

    private void OnFrameDecoded(int width, int height, byte[] bgra, DateTime timestamp)
    {
        int crop = Math.Clamp(_options.CropTopRows, 0, height);
        byte[] pixels = bgra;
        if (crop > 0 && crop < height)
        {
            int rowBytes = width * 4;
            pixels = new byte[rowBytes * crop];
            Buffer.BlockCopy(bgra, 0, pixels, 0, rowBytes * crop);
            height = crop;
        }
        _fps.Push();
        FrameReady?.Invoke(this, new CapturedFrame
        {
            Width = width,
            Height = height,
            Pixels = pixels,
            Timestamp = timestamp,
            Sequence = Interlocked.Increment(ref _sequence),
        });
    }

    private async Task DrainStderrAsync(Process proc)
    {
        try
        {
            using var reader = proc.StandardError;
            string? line;
            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                if (line.Length > 0) _lastStderrLine = line;
            }
        }
        catch { /* 进程结束后读取失败可忽略 */ }
    }

    private void SetState(CaptureState state, string? message = null)
    {
        State = state;
        StateChanged?.Invoke(this, new CaptureStateChangedEventArgs { State = state, Message = message });
    }
}