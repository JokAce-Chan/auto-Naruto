using System.Text.Json.Serialization;

namespace NaruttoTimer.Rules;

/// <summary>左右两侧标识。</summary>
public enum Side { Left, Right }

/// <summary>值上限（源项目固定 4 豆；6 格条场景抬到 6，使空豆 0~6 全程可见）。</summary>
public static class EnergyRules
{
    public const int MaxValue = 6;
}

/// <summary>判定方式（AI 模式下的额外选项）。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EnergyJudgement>))]
public enum EnergyJudgement
{
    /// <summary>值判定：输入 = clamp(值上限 − 空豆数, 0, 值上限)，「恰好 −1」触发。</summary>
    Value,

    /// <summary>空豆判定：输入 = 空豆数本身（不做上限夹断），「恰好 +1」触发。</summary>
    EmptyCount,
}

/// <summary>检测到「使用替身/技能」时的事件参数。</summary>
public sealed class SkillUsedEventArgs : EventArgs
{
    public required Side Side { get; init; }
    public required int OldValue { get; init; }
    public required int NewValue { get; init; }
    public required DateTime Timestamp { get; init; }
}

/// <summary>触发/事件记录（数据保存模块用）。</summary>
public sealed record TriggerEvent(
    DateTime Timestamp,
    Side Side,
    int OldValue,
    int NewValue,
    string Action);

/// <summary>倒计时快照（置顶框与主界面显示用）。</summary>
public sealed record CountdownSnapshot(double LeftSeconds, double RightSeconds)
{
    public static CountdownSnapshot Zero { get; } = new(0, 0);

    public double GetSeconds(Side side) => side == Side.Left ? LeftSeconds : RightSeconds;
}
