namespace NaruttoTimer.Rules;

/// <summary>
/// 触发规则状态机：输入每侧读数，输出倒计时指令。
/// 规则见 PRD §3.5：减1触发/重置15s；增与减≥2不动作不打断；
/// 格数变化(4↔6)不触发且继承值不超上限；加载中不计，加载结束置0.00；归零保持0.00。
/// 左右完全独立并行。
/// </summary>
public interface ICountdownEngine
{
    double GetSeconds(Side side);
    SideStatus GetStatus(Side side);
    CountdownSnapshot GetSnapshot();
    event EventHandler<CountdownChangedEventArgs>? CountdownChanged;
    event EventHandler<TriggerEvent>? Triggered;

    /// <summary>喂入单侧识别读数。</summary>
    void Update(Side side, RecognitionReading reading);

    /// <summary>按时间步进倒计时（由定时器每秒多次调用）。</summary>
    void Tick(double deltaSeconds);

    void Reset();
}