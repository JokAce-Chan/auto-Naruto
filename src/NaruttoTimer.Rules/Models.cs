namespace NaruttoTimer.Rules;

/// <summary>左右两侧标识。</summary>
public enum Side { Left, Right }

/// <summary>该侧菱形格数（4 或 6，按侧自动判定）。</summary>
public enum GridCount { G4 = 4, G6 = 6 }

/// <summary>单格分类结果。</summary>
public enum CellState { Bright, Dark, Unknown }

/// <summary>该侧整体状态。</summary>
public enum SideStatus { Normal, Counting, Loading, Lost }

/// <summary>该侧识别结果：格数 + 亮格数（值）。</summary>
public sealed record SideValue(GridCount GridCount, int Value)
{
    public SideValue Validate()
    {
        int max = (int)GridCount;
        if (Value < 0 || Value > max)
            throw new ArgumentOutOfRangeException(nameof(Value), $"值 {Value} 超出格数上限 {max}");
        return this;
    }
}

/// <summary>识别层输出的一侧读数（供规则引擎输入）。</summary>
public sealed record RecognitionReading(GridCount GridCount, int Value, bool InLoading);

/// <summary>一次识别周期两侧的完整读数。</summary>
public sealed record RecognitionResult(SideReading Left, SideReading Right)
{
    public static RecognitionResult Empty { get; } =
        new(new SideReading(null, null, true), new SideReading(null, null, true));
}

/// <summary>单侧读数（格数/值可为空 = 未识别到）。</summary>
public sealed record SideReading(GridCount? GridCount, int? Value, bool InLoading);

/// <summary>倒计时快照（置顶框与主界面显示用）。</summary>
public sealed record CountdownSnapshot(double LeftSeconds, double RightSeconds, SideStatus LeftStatus, SideStatus RightStatus)
{
    public double GetSeconds(Side side) => side == Side.Left ? LeftSeconds : RightSeconds;
    public SideStatus GetStatus(Side side) => side == Side.Left ? LeftStatus : RightStatus;
}

/// <summary>触发/事件记录（数据保存模块用）。</summary>
public sealed record TriggerEvent(
    DateTime Timestamp,
    Side Side,
    GridCount GridCount,
    int OldValue,
    int NewValue,
    string Action);

/// <summary>倒计时变化事件参数。</summary>
public sealed class CountdownChangedEventArgs : EventArgs
{
    public required Side Side { get; init; }
    public required double Seconds { get; init; }
    public required SideStatus Status { get; init; }
}
