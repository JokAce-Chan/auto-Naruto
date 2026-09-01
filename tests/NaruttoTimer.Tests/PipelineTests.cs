using NaruttoTimer.App;
using NaruttoTimer.Capture;
using NaruttoTimer.Data;
using NaruttoTimer.Recognition;
using NaruttoTimer.Rules;

namespace NaruttoTimer.Tests;

/// <summary>P6 管线控制器集成测试（fake 采集源 + 真实识别/规则 + 临时数据存储）。</summary>
public static class PipelineTests
{
    private static readonly RoiConfig TestRoi = new(10, 10, 220, 70);

    private static (PipelineController pipeline, FakeCapture capture, DataStore store, string dir) Create()
    {
        var capture = new FakeCapture();
        var recognizer = new Recognizer(new RecognizerOptions(
            new ColorRange(180, 255, 120, 255, 120, 255),
            new ColorRange(0, 60, 0, 60, 0, 90),
            TestRoi, null,
            OrangeRange: new ColorRange(180, 255, 40, 140, 0, 120),
            DebounceFrames: 1,
            LoadingSeconds: 0.3));
        var engine = new CountdownEngine(15.0);
        var dir = TestTemp.NewDir();
        var store = new DataStore(dir);
        var pipeline = new PipelineController(capture, recognizer, engine, store);
        return (pipeline, capture, store, dir);
    }

    private static CapturedFrame FourBright(DateTime ts, int brightCount)
    {
        var specs = new List<FrameFactory.CellSpec>();
        for (int i = 0; i < 4; i++)
        {
            specs.Add(new FrameFactory.CellSpec(30 + i * 50, 45, 14,
                i < brightCount ? ((byte)200, (byte)230, (byte)255) : ((byte)10, (byte)20, (byte)40)));
        }
        return FrameFactory.Create(240, 90, specs, ts, (80, 80, 90));
    }

    [Fact]
    public static void 启动后_帧流驱动_减1触发_并写入数据存储()
    {
        var (pipeline, capture, store, dir) = Create();
        try
        {
            TriggerEvent? trigger = null;
            pipeline.Triggered += evt => trigger = evt;
            pipeline.Start();

            var t0 = DateTime.UtcNow;
            capture.Emit(FourBright(t0, 4));
            capture.Emit(FourBright(t0 + TimeSpan.FromMilliseconds(40), 3));

            Check.True(pipeline.IsRunning, "管线运行中");
            Check.True(trigger != null, "值 4→3 应触发");
            Check.Equal(4, trigger!.OldValue, "旧值");
            Check.Equal(3, trigger.NewValue, "新值");
            Check.Equal(1, store.GetTriggers().Count, "数据存储应有 1 条触发事件");
        }
        finally
        {
            pipeline.Dispose();
            TestTemp.Delete(dir);
        }
    }

    [Fact]
    public static void 触发后_Tick_倒计时递减并通知()
    {
        var (pipeline, capture, store, dir) = Create();
        try
        {
            double? lastSeconds = null;
            pipeline.CountdownUpdated += s => lastSeconds = s.LeftSeconds;
            pipeline.Start();
            var t0 = DateTime.UtcNow;
            capture.Emit(FourBright(t0, 4));
            capture.Emit(FourBright(t0 + TimeSpan.FromMilliseconds(40), 3));

            pipeline.Tick(1.0);
            Check.True(lastSeconds.HasValue && Math.Abs(lastSeconds.Value - 14.0) < 0.001, "倒计时应递减到 14.00");
        }
        finally
        {
            pipeline.Dispose();
            TestTemp.Delete(dir);
        }
    }

    [Fact]
    public static void 停止后_不再处理帧()
    {
        var (pipeline, capture, store, dir) = Create();
        try
        {
            int triggers = 0;
            pipeline.Triggered += _ => triggers++;
            pipeline.Start();
            var t0 = DateTime.UtcNow;
            capture.Emit(FourBright(t0, 4));
            capture.Emit(FourBright(t0 + TimeSpan.FromMilliseconds(40), 3));
            Check.Equal(1, triggers, "停止前已触发 1 次");

            pipeline.Stop();
            Check.False(pipeline.IsRunning, "已停止");
            capture.Emit(FourBright(t0 + TimeSpan.FromMilliseconds(80), 4));
            capture.Emit(FourBright(t0 + TimeSpan.FromMilliseconds(120), 3));
            Check.Equal(1, triggers, "停止后不再触发");
        }
        finally
        {
            pipeline.Dispose();
            TestTemp.Delete(dir);
        }
    }

    [Fact]
    public static void 采集状态变化_向上传播()
    {
        var (pipeline, capture, store, dir) = Create();
        try
        {
            CaptureState? state = null;
            string? msg = null;
            pipeline.CaptureStateChanged += (s, m) => { state = s; msg = m; };
            pipeline.Start();
            capture.EmitState(CaptureState.Reconnecting, "测试断开");
            Check.Equal(CaptureState.Reconnecting, state, "状态应传播");
            Check.Equal("测试断开", msg, "消息应传播");
        }
        finally
        {
            pipeline.Dispose();
            TestTemp.Delete(dir);
        }
    }

    [Fact]
    public static void 每帧_识别结果事件()
    {
        var (pipeline, capture, store, dir) = Create();
        try
        {
            RecognitionOutput? output = null;
            pipeline.RecognitionUpdated += o => output = o;
            pipeline.Start();
            capture.Emit(FourBright(DateTime.UtcNow, 4));
            Check.True(output != null, "应收到识别结果");
            Check.Equal(4, output!.Left.Value, "左值=4");
        }
        finally
        {
            pipeline.Dispose();
            TestTemp.Delete(dir);
        }
    }
}

/// <summary>Fake 采集源（不依赖设备）。</summary>
public sealed class FakeCapture : ICaptureSource
{
    public CaptureState State { get; private set; } = CaptureState.Disconnected;
    public double CurrentFps => 30;

    public event EventHandler<CapturedFrame>? FrameReady;
    public event EventHandler<CaptureStateChangedEventArgs>? StateChanged;

    public Task StartAsync(CancellationToken ct)
    {
        State = CaptureState.Connected;
        StateChanged?.Invoke(this, new CaptureStateChangedEventArgs { State = CaptureState.Connected, Message = "已连接" });
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        State = CaptureState.Disconnected;
        return Task.CompletedTask;
    }

    public void Emit(CapturedFrame frame) => FrameReady?.Invoke(this, frame);

    public void EmitState(CaptureState state, string? message)
    {
        State = state;
        StateChanged?.Invoke(this, new CaptureStateChangedEventArgs { State = state, Message = message });
    }
}