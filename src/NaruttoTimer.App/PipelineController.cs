using NaruttoTimer.Capture;
using NaruttoTimer.Data;
using NaruttoTimer.Recognition;
using NaruttoTimer.Rules;

namespace NaruttoTimer.App;

/// <summary>
/// 识别管线：采集帧 → 识别器 → 规则引擎 → 事件记录，并向 UI 抛出事件。
/// 纯 C# 逻辑（不依赖 WPF），可单元测试。
/// </summary>
public sealed class PipelineController : IDisposable
{
    private readonly ICaptureSource _capture;
    private readonly IRecognizer _recognizer;
    private readonly ICountdownEngine _engine;
    private readonly IDataStore _dataStore;
    private readonly object _lock = new();
    private bool _running;

    public event Action<CapturedFrame>? FrameAvailable;
    public event Action<RecognitionOutput>? RecognitionUpdated;
    public event Action<CountdownSnapshot>? CountdownUpdated;
    public event Action<TriggerEvent>? Triggered;
    public event Action<CaptureState, string?>? CaptureStateChanged;
    public event Action<string>? Logged;

    public PipelineController(ICaptureSource capture, IRecognizer recognizer, ICountdownEngine engine, IDataStore dataStore)
    {
        _capture = capture;
        _recognizer = recognizer;
        _engine = engine;
        _dataStore = dataStore;
    }

    public bool IsRunning
    {
        get { lock (_lock) return _running; }
    }

    public ICaptureSource Capture => _capture;
    public IRecognizer Recognizer => _recognizer;
    public ICountdownEngine Engine => _engine;

    public void Start()
    {
        lock (_lock)
        {
            if (_running) return;
            _running = true;
            _capture.FrameReady += OnFrame;
            _capture.StateChanged += OnState;
            _engine.CountdownChanged += OnCountdown;
            _engine.Triggered += OnTrigger;
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
            _engine.CountdownChanged -= OnCountdown;
            _engine.Triggered -= OnTrigger;
            _ = _capture.StopAsync();
            Logged?.Invoke("识别管线已停止");
        }
    }

    /// <summary>由定时器周期调用，步进倒计时。</summary>
    public void Tick(double deltaSeconds) => _engine.Tick(deltaSeconds);

    private void OnFrame(object? sender, CapturedFrame frame)
    {
        FrameAvailable?.Invoke(frame);
        var output = _recognizer.Recognize(frame);
        _engine.Update(Side.Left, new RecognitionReading(output.Left.GridCount, output.Left.Value, output.Left.InLoading));
        _engine.Update(Side.Right, new RecognitionReading(output.Right.GridCount, output.Right.Value, output.Right.InLoading));
        RecognitionUpdated?.Invoke(output);
    }

    private void OnCountdown(object? sender, CountdownChangedEventArgs e)
    {
        CountdownUpdated?.Invoke(_engine.GetSnapshot());
    }

    private void OnTrigger(object? sender, TriggerEvent evt)
    {
        _dataStore.AppendTrigger(evt);
        Triggered?.Invoke(evt);
        Logged?.Invoke($"{evt.Timestamp:HH:mm:ss} {evt.Side}值 {evt.OldValue}→{evt.NewValue} 触发，置顶框=15.00");
    }

    private void OnState(object? sender, CaptureStateChangedEventArgs e)
    {
        CaptureStateChanged?.Invoke(e.State, e.Message);
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}