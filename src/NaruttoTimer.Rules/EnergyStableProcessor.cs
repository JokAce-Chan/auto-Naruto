namespace NaruttoTimer.Rules;

/// <summary>
/// 能量稳定处理器（源 domain/dataprocess/EnergyStableProcessor）。
/// 仅在「新稳定值 == 上一次稳定值 - 1」时判定为使用技能；其余情况不计。
/// 注意：无论是否触发，本次稳定值都会写入 <c>lastStableValue</c>（与源 finally 行为一致）。
/// </summary>
public sealed class EnergyStableProcessor
{
    private readonly StableValueTracker _tracker;
    private int _lastStableValue = -1;

    public EnergyStableProcessor(int stableFrames)
    {
        _tracker = new StableValueTracker(stableFrames);
    }

    /// <summary>上一次的稳定值（未初始化时为 -1）。</summary>
    public int LastStableValue => _lastStableValue;

    /// <summary>最近一次确认的稳定值。</summary>
    public int StableValue => _tracker.StableValue;

    public bool IsUseSkill(int value)
    {
        var result = _tracker.Update(value);
        if (!result.HasNewStableValue) return false;
        try
        {
            if (_lastStableValue == -1) return false;
            return result.StableValue == _lastStableValue - 1;
        }
        finally
        {
            _lastStableValue = result.StableValue;
        }
    }

    public void Reset()
    {
        _lastStableValue = -1;
        _tracker.Reset();
    }
}
