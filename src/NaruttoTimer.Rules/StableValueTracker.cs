namespace NaruttoTimer.Rules;

/// <summary>
/// 稳定值跟踪器（源 六道带土计时器2.0 domain/dataprocess/StableValueTracker）。
/// 连续 <c>stableThreshold</c> 帧读到相同值才认为产生新的稳定值。
/// </summary>
public sealed class StableValueTracker
{
    private readonly int _stableThreshold;
    private int _candidate;
    private int _count;

    public StableValueTracker(int stableThreshold)
    {
        if (stableThreshold < 1) throw new ArgumentOutOfRangeException(nameof(stableThreshold), "稳定帧数至少为 1");
        _stableThreshold = stableThreshold;
    }

    /// <summary>最近一次确认的稳定值（初始 0，与源一致）。</summary>
    public int StableValue { get; private set; }

    /// <summary>单次更新结果（源 StableValueTracker.UpdateResult）。</summary>
    public readonly record struct UpdateResult(bool HasNewStableValue, int StableValue);

    public UpdateResult Update(int value)
    {
        if (value == _candidate) _count++;
        else
        {
            _candidate = value;
            _count = 1;
        }

        if (_count >= _stableThreshold)
        {
            StableValue = _candidate;
            return new UpdateResult(true, StableValue);
        }
        return new UpdateResult(false, StableValue);
    }

    public void Reset()
    {
        _candidate = 0;
        _count = 0;
        StableValue = 0;
    }
}
