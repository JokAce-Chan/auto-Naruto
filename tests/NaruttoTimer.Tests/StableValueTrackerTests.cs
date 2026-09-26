using NaruttoTimer.Rules;

namespace NaruttoTimer.Tests;

/// <summary>稳定值跟踪器测试（源 domain/dataprocess/StableValueTracker）。</summary>
public static class StableValueTrackerTests
{
    [Fact]
    public static void 稳定帧数非法_抛异常()
    {
        Check.Throws<ArgumentOutOfRangeException>(() => new StableValueTracker(0), "稳定帧数至少为 1");
    }

    [Fact]
    public static void 连续N帧同值_第N帧才确认稳定()
    {
        var tracker = new StableValueTracker(3);
        Check.False(tracker.Update(4).HasNewStableValue, "第 1 帧不确认");
        Check.False(tracker.Update(4).HasNewStableValue, "第 2 帧不确认");
        var third = tracker.Update(4);
        Check.True(third.HasNewStableValue, "第 3 帧确认");
        Check.Equal(4, third.StableValue, "稳定值");
        Check.Equal(4, tracker.StableValue, "属性同步");
    }

    [Fact]
    public static void 值中箭_计数重置()
    {
        var tracker = new StableValueTracker(3);
        tracker.Update(4);
        tracker.Update(4);
        Check.False(tracker.Update(3).HasNewStableValue, "换成 3 后重新计数");
        Check.False(tracker.Update(3).HasNewStableValue, "第 2 帧 3");
        Check.True(tracker.Update(3).HasNewStableValue, "第 3 帧 3 确认稳定");
        Check.Equal(3, tracker.StableValue, "稳定值变为 3");
    }

    [Fact]
    public static void 稳定后同值持续_每帧都算新稳定值_但不重复触发()
    {
        var tracker = new StableValueTracker(3);
        for (int i = 0; i < 5; i++) tracker.Update(4);
        Check.Equal(4, tracker.StableValue, "稳定值保持 4");
        Check.True(tracker.Update(4).HasNewStableValue, "已稳定后同值仍视为新的稳定值（源行为）");
    }

    [Fact]
    public static void Reset_回到初始态()
    {
        var tracker = new StableValueTracker(2);
        tracker.Update(3);
        tracker.Update(3);
        Check.Equal(3, tracker.StableValue, "已稳定 3");
        tracker.Reset();
        Check.Equal(0, tracker.StableValue, "Reset 后回到 0");
        Check.False(tracker.Update(3).HasNewStableValue, "Reset 后重新计数");
    }
}
