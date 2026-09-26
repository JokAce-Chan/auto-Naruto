using NaruttoTimer.App;
using NaruttoTimer.Capture;
using NaruttoTimer.Data;
using NaruttoTimer.Recognition;
using NaruttoTimer.Rules;

namespace NaruttoTimer.Tests;

/// <summary>P6 管线控制器集成测试（fake 采集源 + fake 识别器 + 真实规则引擎 + 临时数据存储）。</summary>
public static class PipelineTests
{
    private sealed class FakeClock
    {
        public DateTime Now { get; set; } = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        public void Advance(double seconds) => Now = Now.AddSeconds(seconds);
    }

    private static (PipelineController pipeline, FakeCapture capture, FakeRecognizer recognizer, DataStore store, string dir, FakeClock clock) Create()
    {
        var capture = new FakeCapture();
        var recognizer = new FakeRecognizer();
        var clock = new FakeClock();
        var engine = new EnergyRuleEngine(14.5, 3, () => clock.Now);
        var dir = TestTemp.NewDir();
        var store = new DataStore(dir);
        var pipeline = new PipelineController(capture, recognizer, engine, store) { InferenceIntervalMs = 0 };
        return (pipeline, capture, recognizer, store, dir, clock);
    }

    private static CapturedFrame Frame(DateTime ts) => new()
    {
        Width = 4,
        Height = 2,
        Pixels = new byte[4 * 2 * 4],
        Timestamp = ts,
    };

    [Fact]
    public static void 启动后_稳定三帧减1触发_并写入数据存储()
    {
        var (pipeline, capture, recognizer, store, dir, _) = Create();
        try
        {
            TriggerEvent? trigger = null;
            pipeline.Triggered += evt => trigger = evt;
            pipeline.Start();

            // 需先稳定在 4（3 帧）建立基线，再稳定到 3（3 帧）才判定减 1
            recognizer.Enqueue(4, 4, 4, 3, 3, 3);
            var t0 = DateTime.UtcNow;
            for (int i = 0; i < 6; i++) capture.Emit(Frame(t0.AddMilliseconds(i * 10)));

            Check.True(pipeline.IsRunning, "管线运行中");
            Check.True(trigger != null, "稳定值 4→3 应触发");
            Check.Equal(Side.Left, trigger!.Side, "左值减 1 属左侧");
            Check.Equal(4, trigger.OldValue, "旧值");
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
    public static void 触发后_倒计时按时间递减_归零后保持0()
    {
        var (pipeline, capture, recognizer, store, dir, clock) = Create();
        try
        {
            pipeline.Start();
            recognizer.Enqueue(4, 4, 4, 3, 3, 3);
            var t0 = DateTime.UtcNow;
            for (int i = 0; i < 6; i++) capture.Emit(Frame(t0.AddMilliseconds(i * 10)));

            var snap = pipeline.GetSnapshot();
            Check.Near(14.5, snap.LeftSeconds, 0.001, "触发瞬间应为 14.50");
            Check.Near(0.0, snap.RightSeconds, 0.001, "右侧未触发保持 0.00");

            clock.Advance(1.0);
            Check.Near(13.5, pipeline.GetSnapshot().LeftSeconds, 0.001, "1s 后应为 13.50");

            clock.Advance(100);
            Check.Near(0.0, pipeline.GetSnapshot().LeftSeconds, 0.001, "超时后归零且保持 0.00");
        }
        finally
        {
            pipeline.Dispose();
            TestTemp.Delete(dir);
        }
    }

    [Fact]
    public static void 每帧读数_都参与判定()
    {
        var (pipeline, capture, recognizer, store, dir, _) = Create();
        try
        {
            EnergyReading? last = null;
            pipeline.RecognitionUpdated += r => last = r;
            pipeline.Start();

            recognizer.EnqueueReading(new EnergyReading(2, 1, 2, 3, Array.Empty<EnergyDetection>(), Array.Empty<EnergyDetection>()));
            capture.Emit(Frame(DateTime.UtcNow));

            Check.True(last != null, "应上报识别结果");
            Check.Equal(2, last!.LeftValue, "读数原样参与判定，不被门控");
            Check.Equal(1, last.RightValue, "读数原样参与判定，不被门控");
            Check.Equal(0, store.GetTriggers().Count, "单帧读数不触发");
        }
        finally
        {
            pipeline.Dispose();
            TestTemp.Delete(dir);
        }
    }

    [Fact]
    public static void 推理间隔节流_跳过过密帧()
    {
        var (pipeline, capture, recognizer, store, dir, _) = Create();
        try
        {
            pipeline.InferenceIntervalMs = 60_000;
            pipeline.Start();
            recognizer.Enqueue(4, 3);
            var t0 = DateTime.UtcNow;
            capture.Emit(Frame(t0));
            capture.Emit(Frame(t0.AddMilliseconds(5)));
            Check.Equal(1, recognizer.Calls, "间隔内仅应识别首帧");
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
        var (pipeline, capture, recognizer, store, dir, _) = Create();
        try
        {
            int recognitions = 0;
            pipeline.RecognitionUpdated += _ => recognitions++;
            pipeline.Start();
            recognizer.Enqueue(4, 4, 4, 3, 3, 3);
            var t0 = DateTime.UtcNow;
            for (int i = 0; i < 3; i++) capture.Emit(Frame(t0.AddMilliseconds(i * 10)));
            Check.Equal(3, recognitions, "停止前识别 3 次");

            pipeline.Stop();
            Check.False(pipeline.IsRunning, "已停止");
            capture.Emit(Frame(t0.AddMilliseconds(40)));
            Check.Equal(3, recognitions, "停止后不再识别");
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
        var (pipeline, capture, recognizer, store, dir, _) = Create();
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
    public static void 每帧_触发帧画面事件()
    {
        var (pipeline, capture, recognizer, store, dir, _) = Create();
        try
        {
            int frames = 0;
            pipeline.FrameAvailable += _ => frames++;
            pipeline.Start();
            recognizer.Enqueue(4);
            var t0 = DateTime.UtcNow;
            capture.Emit(Frame(t0));
            capture.Emit(Frame(t0.AddMilliseconds(1)));
            Check.Equal(2, frames, "预览帧回调不受推理节流影响");
        }
        finally
        {
            pipeline.Dispose();
            TestTemp.Delete(dir);
        }
    }

    [Fact]
    public static void 空豆判定_空豆4到5触发_并写入数据存储()
    {
        var capture = new FakeCapture();
        var recognizer = new FakeRecognizer();
        var engine = new EnergyRuleEngine(14.5, 3) { Judgement = EnergyJudgement.EmptyCount };
        var dir = TestTemp.NewDir();
        var store = new DataStore(dir);
        var pipeline = new PipelineController(capture, recognizer, engine, store) { InferenceIntervalMs = 0 };
        try
        {
            TriggerEvent? trigger = null;
            pipeline.Triggered += evt => trigger = evt;
            pipeline.Start();

            // 左侧空豆稳定 4（3 帧）建立基线，再稳定到 5（3 帧）判定加 1；右侧恒为空豆 6。
            recognizer.EnqueueEmpty(4, 6);
            recognizer.EnqueueEmpty(4, 6);
            recognizer.EnqueueEmpty(4, 6);
            recognizer.EnqueueEmpty(5, 6);
            recognizer.EnqueueEmpty(5, 6);
            recognizer.EnqueueEmpty(5, 6);

            var t0 = DateTime.UtcNow;
            for (int i = 0; i < 6; i++) capture.Emit(Frame(t0.AddMilliseconds(i * 10)));

            Check.True(trigger != null, "空豆 4→5 应触发");
            Check.Equal(Side.Left, trigger!.Side, "左侧");
            Check.Equal(4, trigger.OldValue, "旧空豆");
            Check.Equal(5, trigger.NewValue, "新空豆");
            Check.Equal(1, store.GetTriggers().Count, "数据存储应有 1 条触发事件");
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

/// <summary>Fake 识别器：按队列逐帧返回预设读数。</summary>
public sealed class FakeRecognizer : IEnergyRecognizer
{
    private readonly Queue<EnergyReading> _queue = new();

    public EnergyReading Fallback { get; set; } = new(0, 0, EnergyRules.MaxValue, EnergyRules.MaxValue, Array.Empty<EnergyDetection>(), Array.Empty<EnergyDetection>());
    public int Calls { get; private set; }

    public void Enqueue(params int[] leftValues)
    {
        // 仅让左侧变化，右侧恒为 0，避免两侧同时触发干扰侧别断言
        foreach (int v in leftValues) EnqueueReading(Reading(v, 0));
    }

    /// <summary>直接按左右空豆数入队（供「空豆判定」用例使用）。</summary>
    public void EnqueueEmpty(int leftEmpty, int rightEmpty) =>
        EnqueueReading(EmptyReading(leftEmpty, rightEmpty));

    private static EnergyReading EmptyReading(int leftEmpty, int rightEmpty)
    {
        static IReadOnlyList<EnergyDetection> Empty() => Array.Empty<EnergyDetection>();
        return new EnergyReading(
            EnergyRules.MaxValue - leftEmpty,
            EnergyRules.MaxValue - rightEmpty,
            leftEmpty,
            rightEmpty,
            Empty(),
            Empty());
    }

    public void EnqueueReading(EnergyReading reading) => _queue.Enqueue(reading);

    private static EnergyReading Reading(int left, int right)
    {
        static IReadOnlyList<EnergyDetection> Empty() => Array.Empty<EnergyDetection>();
        return new EnergyReading(left, right, EnergyRules.MaxValue - left, EnergyRules.MaxValue - right, Empty(), Empty());
    }

    public EnergyReading Recognize(CapturedFrame frame)
    {
        Calls++;
        return _queue.Count > 0 ? _queue.Dequeue() : Fallback;
    }

    public void Reset() { }

    public void Dispose() { }
}
