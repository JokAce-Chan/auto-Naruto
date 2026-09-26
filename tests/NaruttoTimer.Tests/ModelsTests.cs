using NaruttoTimer.Rules;

namespace NaruttoTimer.Tests;

public static class ModelsTests
{
    [Fact]
    public static void Side_枚举值正确()
    {
        Check.Equal(0, (int)Side.Left, "左");
        Check.Equal(1, (int)Side.Right, "右");
    }

    [Fact]
    public static void 值上限为4豆()
    {
        Check.Equal(4, EnergyRules.MaxValue, "源项目固定 4 豆（clamp 0~4）");
    }

    [Fact]
    public static void CountdownSnapshot_按侧取值()
    {
        var snap = new CountdownSnapshot(14.5, 8.32);
        Check.Equal(14.5, snap.GetSeconds(Side.Left), "左秒数");
        Check.Equal(8.32, snap.GetSeconds(Side.Right), "右秒数");
    }

    [Fact]
    public static void CountdownSnapshot_Zero_双零()
    {
        Check.Equal(0.0, CountdownSnapshot.Zero.LeftSeconds, "左 0");
        Check.Equal(0.0, CountdownSnapshot.Zero.RightSeconds, "右 0");
    }

    [Fact]
    public static void TriggerEvent_记录字段()
    {
        var ts = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Local);
        var e = new TriggerEvent(ts, Side.Right, 3, 2, "使用技能");
        Check.Equal(ts, e.Timestamp, "时间");
        Check.Equal(Side.Right, e.Side, "侧别");
        Check.Equal(3, e.OldValue, "旧值");
        Check.Equal(2, e.NewValue, "新值");
        Check.Equal("使用技能", e.Action, "动作");
    }

    [Fact]
    public static void SkillUsedEventArgs_携带侧别与新旧值()
    {
        var ts = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        var args = new SkillUsedEventArgs { Side = Side.Left, OldValue = 4, NewValue = 3, Timestamp = ts };
        Check.Equal(Side.Left, args.Side, "侧别");
        Check.Equal(4, args.OldValue, "旧值");
        Check.Equal(3, args.NewValue, "新值");
        Check.Equal(ts, args.Timestamp, "时间");
    }
}
