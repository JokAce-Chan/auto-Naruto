using NaruttoTimer.Rules;

namespace NaruttoTimer.Tests;

/// <summary>规则引擎测试（源 Engine + ScreenCaptureService：稳定 3 帧 / 减 1 触发 / 14.5s 倒计时）。</summary>
public static class EnergyRuleEngineTests
{
    private sealed class FakeClock
    {
        public DateTime Now { get; set; } = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        public void Advance(double seconds) => Now = Now.AddSeconds(seconds);
    }

    private static (EnergyRuleEngine engine, FakeClock clock) NewEngine(double seconds = 14.5, int stable = 3)
    {
        var clock = new FakeClock();
        return (new EnergyRuleEngine(seconds, stable, () => clock.Now), clock);
    }

    private static void Feed(EnergyRuleEngine e, Side side, int value, int times)
    {
        for (int i = 0; i < times; i++) e.Update(side, value);
    }

    [Fact]
    public static void 初始状态_双0_00()
    {
        var (e, _) = NewEngine();
        Check.Near(0.0, e.GetSnapshot().LeftSeconds, 0.001, "左初始 0");
        Check.Near(0.0, e.GetSnapshot().RightSeconds, 0.001, "右初始 0");
        Check.Equal(14.5, e.CountdownSeconds, "默认 14.5s");
    }

    [Fact]
    public static void 首次读数_建立基线_不触发()
    {
        var (e, _) = NewEngine();
        int triggers = 0;
        e.SkillUsed += (_, _) => triggers++;
        Feed(e, Side.Left, 4, 3);
        Check.Equal(0, triggers, "首次读数不应触发");
        Check.Equal(4, e.LeftValue, "左值记录");
        Check.Equal(4, e.LeftStableValue, "左稳定值");
        Check.Near(0.0, e.GetSnapshot().LeftSeconds, 0.001, "秒数保持 0");
    }

    [Fact]
    public static void 稳定值恰好减1_触发并起算14_5s()
    {
        var (e, _) = NewEngine();
        SkillUsedEventArgs? args = null;
        e.SkillUsed += (_, a) => args = a;

        Feed(e, Side.Left, 4, 3);
        Feed(e, Side.Left, 3, 3);

        Check.True(args != null, "应触发");
        Check.Equal(Side.Left, args!.Side, "侧别");
        Check.Equal(4, args.OldValue, "旧值");
        Check.Equal(3, args.NewValue, "新值");
        Check.Near(14.5, e.GetSnapshot().LeftSeconds, 0.001, "倒计时 14.50");
        Check.Near(0.0, e.GetSnapshot().RightSeconds, 0.001, "右侧不受影响");
    }

    [Fact]
    public static void 倒计时按时间递减()
    {
        var (e, clock) = NewEngine();
        Feed(e, Side.Left, 4, 3);
        Feed(e, Side.Left, 3, 3);

        clock.Advance(1.0);
        Check.Near(13.5, e.GetSnapshot().LeftSeconds, 0.001, "1s 后 13.50");
        clock.Advance(4.0);
        Check.Near(9.5, e.GetSnapshot().LeftSeconds, 0.001, "5s 后 9.50");
        clock.Advance(20.0);
        Check.Near(0.0, e.GetSnapshot().LeftSeconds, 0.001, "超时归零不出现负数");
    }

    [Fact]
    public static void 倒计时中再次减1_重置而非叠加()
    {
        var (e, clock) = NewEngine();
        Feed(e, Side.Left, 4, 3);
        Feed(e, Side.Left, 3, 3);
        clock.Advance(5.0);
        Check.Near(9.5, e.GetSnapshot().LeftSeconds, 0.001, "已过 5s");

        Feed(e, Side.Left, 2, 3);
        Check.Near(14.5, e.GetSnapshot().LeftSeconds, 0.001, "重置为 14.50 而非累加");
    }

    [Fact]
    public static void 值增_不触发()
    {
        var (e, _) = NewEngine();
        int triggers = 0;
        e.SkillUsed += (_, _) => triggers++;
        Feed(e, Side.Left, 2, 3);
        Feed(e, Side.Left, 4, 3);
        Check.Equal(0, triggers, "值增不应触发");
        Check.Near(0.0, e.GetSnapshot().LeftSeconds, 0.001, "秒数保持 0");
    }

    [Fact]
    public static void 值减2_不触发_不打断进行中的倒计时()
    {
        var (e, clock) = NewEngine();
        int triggers = 0;
        e.SkillUsed += (_, _) => triggers++;
        Feed(e, Side.Left, 4, 3);
        Feed(e, Side.Left, 3, 3);
        clock.Advance(4.0);

        Feed(e, Side.Left, 1, 3);
        Check.Equal(1, triggers, "减 2 不应触发");
        Check.Near(10.5, e.GetSnapshot().LeftSeconds, 0.001, "倒计时继续走");
    }

    [Fact]
    public static void 左右两侧独立判定()
    {
        var (e, _) = NewEngine();
        var sides = new List<Side>();
        e.SkillUsed += (_, a) => sides.Add(a.Side);

        Feed(e, Side.Left, 4, 3);
        Feed(e, Side.Right, 4, 3);
        Feed(e, Side.Left, 3, 3);
        Check.Equal(1, sides.Count, "仅左侧触发");
        Check.Equal(Side.Left, sides[0], "左侧");
        Check.Near(0.0, e.GetSnapshot().RightSeconds, 0.001, "右侧仍为 0");

        Feed(e, Side.Right, 3, 3);
        Check.Equal(2, sides.Count, "右侧随后也触发");
        Check.Near(14.5, e.GetSnapshot().LeftSeconds, 0.001, "左侧倒计时不受影响");
        Check.Near(14.5, e.GetSnapshot().RightSeconds, 0.001, "右侧倒计时起算");
    }

    [Fact]
    public static void 值超上限_被clamp到4()
    {
        var (e, _) = NewEngine();
        e.Update(Side.Left, 99);
        Check.Equal(4, e.LeftValue, "clamp 到 4");
        e.Update(Side.Right, -3);
        Check.Equal(0, e.RightValue, "clamp 到 0");
    }

    [Fact]
    public static void 单帧闪断_不当作稳定变化()
    {
        var (e, _) = NewEngine();
        int triggers = 0;
        e.SkillUsed += (_, _) => triggers++;
        Feed(e, Side.Left, 4, 3);
        e.Update(Side.Left, 3); // 单帧闪到 3
        Feed(e, Side.Left, 4, 2);
        Check.Equal(0, triggers, "单帧闪断不应触发");
    }

    [Fact]
    public static void Reset_清空全部状态()
    {
        var (e, _) = NewEngine();
        Feed(e, Side.Left, 4, 3);
        Feed(e, Side.Left, 3, 3);
        e.Reset();

        Check.Near(0.0, e.GetSnapshot().LeftSeconds, 0.001, "倒计时清零");
        Check.Equal(0, e.LeftValue, "左值清零");
        Check.Equal(0, e.LeftStableValue, "左稳定值清零");
    }
}
