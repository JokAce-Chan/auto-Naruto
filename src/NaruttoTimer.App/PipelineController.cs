using NaruttoTimer.Capture;
using NaruttoTimer.Data;
using NaruttoTimer.Recognition;
using NaruttoTimer.Rules;

namespace NaruttoTimer.App;

/// <summary>
/// 识别管线：采集帧 → 按推理间隔识别 → 规则引擎（稳定 N 帧 / 值减1触发 / 14.5s 倒计时）→ 事件记录。
/// 纯 C# 逻辑（不依赖 WPF），可单元测试。
/// </summary>
public sealed class PipelineController : IDisposable
{
    private readonly ICaptureSource _capture;
    private readonly IEnergyRecognizer _recognizer;
    private readonly EnergyRuleEngine _engine;
    private readonly IDataStore _dataStore;
    private readonly object _lock = new();
    private bool _running;
    private DateTime _lastInference = DateTime.MinValue;

    public PipelineController(ICaptureSource capture, IEnergyRecognizer recognizer, EnergyRuleEngine engine, IDataStore dataStore)
    {
        _capture = capture;
        _recognizer = recognizer;
        _engine = engine;
        _dataStore = dataStore;
    }

    /// <summary>推理间隔（毫秒）。源项目 8FPS → 125ms；调大可降低 CPU 占用。</summary>
    public int InferenceIntervalMs { get; set; } = 125;

    public event Action<CapturedFrame>? FrameAvailable;
    public event Action<EnergyReading>? RecognitionUpdated;
    public event Action<TriggerEvent>? Triggered;
    public event Action<CaptureState, string?>? CaptureStateChanged;
    public event Action<string>? Logged;

    public bool IsRunning
    {
        get { lock (_lock) return _running; }
    }

    public EnergyRuleEngine Engine => _engine;

    public void Start()
    {
        lock (_lock)
        {
            if (_running) return;
            _running = true;
            _lastInference = DateTime.MinValue;
            _capture.FrameReady += OnFrame;
            _capture.StateChanged += OnState;
            _engine.SkillUsed += OnSkillUsed;
            _ = _capture.StartAsync(CancellationToken.None);
            Logged?.Invoke("识别管线已启动");
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_running) return;
            _running = false;
            _capture.FrameReady -= OnFrame;
            _capture.StateChanged -= OnState;
            _engine.SkillUsed -= OnSkillUsed;
            _ = _capture.StopAsync();
            Logged?.Invoke("识别管线已停止");
        }
    }

    /// <summary>由 UI 定时器（100ms）调用，用于刷新倒计时显示。</summary>
    public CountdownSnapshot GetSnapshot() => _engine.GetSnapshot();

    private void OnFrame(object? sender, CapturedFrame frame)
    {
        FrameAvailable?.Invoke(frame);
        if (!_running) return;

        var now = DateTime.UtcNow;
        if ((now - _lastInference).TotalMilliseconds < InferenceIntervalMs) return;
        _lastInference = now;

        try
        {
            var reading = _recognizer.Recognize(frame);
            _engine.Update(Side.Left, reading.LeftValue, reading.LeftEmptyCount);
            _engine.Update(Side.Right, reading.RightValue, reading.RightEmptyCount);
            RecognitionUpdated?.Invoke(reading);
        }
        catch (Exception ex)
        {
            Logged?.Invoke($"识别帧异常：{ex.Message}");
        }
    }

    private void OnSkillUsed(object? sender, SkillUsedEventArgs e)
    {
        var evt = new TriggerEvent(e.Timestamp, e.Side, e.OldValue, e.NewValue, "使用技能");
        _dataStore.AppendTrigger(evt);
        Triggered?.Invoke(evt);
        Logged?.Invoke($"{e.Timestamp:HH:mm:ss} {(e.Side == Side.Left ? "左" : "右")}值 {e.OldValue}→{e.NewValue} 触发，置顶框={_engine.CountdownSeconds:0.00}");
    }

    private void OnState(object? sender, CaptureStateChangedEventArgs e) =>
        CaptureStateChanged?.Invoke(e.State, e.Message);

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
