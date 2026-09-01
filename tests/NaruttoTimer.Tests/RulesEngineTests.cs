using NaruttoTimer.Rules;

namespace NaruttoTimer.Tests;

/// <summary>P3 规则状态机测试：覆盖 PRD §3.5 全部判定场景。</summary>
public static class RulesEngineTests
{
    private static RecognitionReading R(GridCount g, int v, bool loading = false) => new(g, v, loading);
    private static CountdownEngine NewEngine() => new(15.0);

    [Fact]
    public static void 初始状态_Normal_0_00()
    {
        var e = NewEngine();
        Check.Equal(SideStatus.Normal, e.GetStatus(Side.Left), "左初始状态");
        Check.Equal(SideStatus.Normal, e.GetStatus(Side.Right), "右初始状态");
        Check.Near(0.0, e.GetSeconds(Side.Left), 0.001, "左初始秒数");
        Check.Near(0.0, e.GetSeconds(Side.Right), 0.001, "右初始秒数");
    }

    [Fact]
    public static void 首次读数_建立基线_不触发()
    {
        var e = NewEngine();
        int triggers = 0;
        e.Triggered += (_, _) => triggers++;
        e.Update(Side.Left, R(GridCount.G4, 4));
        Check.Equal(0, triggers, "首次读数不应触发");
        Check.Equal(SideStatus.Normal, e.GetStatus(Side.Left), "状态保持 Normal");
        Check.Near(0.0, e.GetSeconds(Side.Left), 0.001, "秒数保持 0");
    }

    [Fact]
    public static void 值恰好减1_触发15秒()
    {
        var e = NewEngine();
        e.Update(Side.Left, R(GridCount.G4, 4));
        e.Update(Side.Left, R(GridCount.G4, 3));
        Check.Equal(SideStatus.Counting, e.GetStatus(Side.Left), "进入倒计时状态");
        Check.Near(15.0, e.GetSeconds(Side.Left), 0.001, "秒数=15.00");
    }

    [Fact]
    public static void 倒计时中再次减1_重置为15秒_不叠加()
    {
        var e = NewEngine();
        e.Update(Side.Left, R(GridCount.G4, 4));
        e.Update(Side.Left, R(GridCount.G4, 3)); // 15.00
        e.Tick(5.0);                              // 10.00
        e.Update(Side.Left, R(GridCount.G4, 2));  // 重置
        Check.Near(15.0, e.GetSeconds(Side.Left), 0.001, "应重置为 15.00 而非累加");
        Check.Equal(SideStatus.Counting, e.GetStatus(Side.Left), "仍在倒计时");
    }

    [Fact]
    public static void 值不变_不动作_倒计时继续()
    {
        var e = NewEngine();
        e.Update(Side.Left, R(GridCount.G4, 4));
        e.Update(Side.Left, R(GridCount.G4, 3));
        e.Tick(2.0);
        e.Update(Side.Left, R(GridCount.G4, 3));
        Check.Near(13.0, e.GetSeconds(Side.Left), 0.001, "倒计时不被中断");
        Check.Equal(SideStatus.Counting, e.GetStatus(Side.Left), "状态保持 Counting");
    }

    [Fact]
    public static void 值增_不动作()
    {
        var e = NewEngine();
        int triggers = 0;
        e.Triggered += (_, _) => triggers++;
        e.Update(Side.Left, R(GridCount.G4, 1));
        e.Update(Side.Left, R(GridCount.G4, 2));
        Check.Equal(0, triggers, "值增不应触发");
        Check.Equal(SideStatus.Normal, e.GetStatus(Side.Left), "状态保持 Normal");
        Check.Near(0.0, e.GetSeconds(Side.Left), 0.001, "秒数保持 0");
    }

    [Fact]
    public static void 值减大于等于2_不动作_不打断()
    {
        var e = NewEngine();
        int triggers = 0;
        e.Triggered += (_, _) => triggers++;
        e.Update(Side.Left, R(GridCount.G4, 4));
        e.Update(Side.Left, R(GridCount.G4, 3)); // 触发
        e.Tick(4.0);                              // 11.00
        e.Update(Side.Left, R(GridCount.G4, 1));  // 减2
        Check.Equal(1, triggers, "减≥2 不应触发");
        Check.Near(11.0, e.GetSeconds(Side.Left), 0.001, "进行中倒计时不打断");
        Check.Equal(SideStatus.Counting, e.GetStatus(Side.Left), "状态保持 Counting");
    }

    [Fact]
    public static void 格数4到6_继承值_不触发()
    {
        var e = NewEngine();
        int triggers = 0;
        e.Triggered += (_, _) => triggers++;
        e.Update(Side.Left, R(GridCount.G4, 3));
        e.Update(Side.Left, R(GridCount.G6, 3)); // 格数变化
        Check.Equal(0, triggers, "格数变化不应触发");
        Check.Equal(SideStatus.Normal, e.GetStatus(Side.Left), "状态保持 Normal");
        Check.Near(0.0, e.GetSeconds(Side.Left), 0.001, "秒数保持 0");
    }

    [Fact]
    public static void 格数4到6_随后减1_触发()
    {
        var e = NewEngine();
        e.Update(Side.Left, R(GridCount.G4, 3));
        e.Update(Side.Left, R(GridCount.G6, 3)); // 继承值 3
        e.Update(Side.Left, R(GridCount.G6, 2)); // 3→2 减1
        Check.Equal(SideStatus.Counting, e.GetStatus(Side.Left), "格数变化后的减1应触发");
        Check.Near(15.0, e.GetSeconds(Side.Left), 0.001, "秒数=15.00");
    }

    [Fact]
    public static void 格数6到4_重置上一值_新值不超上限()
    {
        var e = NewEngine();
        int triggers = 0;
        e.Triggered += (_, _) => triggers++;
        e.Update(Side.Left, R(GridCount.G6, 5));
        e.Update(Side.Left, R(GridCount.G4, 4)); // 6→4，值钳制到 4
        Check.Equal(0, triggers, "6→4 不应触发");
        Check.Near(0.0, e.GetSeconds(Side.Left), 0.001, "秒数保持 0");
    }

    [Fact]
    public static void 格数6到4_随后减1_触发()
    {
        var e = NewEngine();
        e.Update(Side.Left, R(GridCount.G6, 5));
        e.Update(Side.Left, R(GridCount.G4, 4)); // 新基线 4
        e.Update(Side.Left, R(GridCount.G4, 3)); // 4→3 减1
        Check.Equal(SideStatus.Counting, e.GetStatus(Side.Left), "6→4 后的减1应触发");
        Check.Near(15.0, e.GetSeconds(Side.Left), 0.001, "秒数=15.00");
    }

    [Fact]
    public static void 格数变化_同值连续_不触发()
    {
        var e = NewEngine();
        int triggers = 0;
        e.Triggered += (_, _) => triggers++;
        e.Update(Side.Left, R(GridCount.G4, 3));
        e.Update(Side.Left, R(GridCount.G6, 3));
        e.Update(Side.Left, R(GridCount.G6, 3));
        Check.Equal(0, triggers, "同值不应触发");
    }

    [Fact]
    public static void 加载中_值变化被忽略_不触发不重置()
    {
        var e = NewEngine();
        int triggers = 0;
        e.Triggered += (_, _) => triggers++;
        e.Update(Side.Left, R(GridCount.G4, 3));
        e.Update(Side.Left, R(GridCount.G4, 3, loading: true));
        e.Update(Side.Left, R(GridCount.G4, 1, loading: true)); // 突变不计
        Check.Equal(0, triggers, "加载中不应触发");
        Check.Equal(SideStatus.Loading, e.GetStatus(Side.Left), "状态为 Loading");
        Check.Near(0.0, e.GetSeconds(Side.Left), 0.001, "秒数保持 0");
    }

    [Fact]
    public static void 加载中_倒计时继续运行_不被重置()
    {
        var e = NewEngine();
        e.Update(Side.Left, R(GridCount.G4, 4));
        e.Update(Side.Left, R(GridCount.G4, 3)); // 15.00
        e.Tick(3.0);                              // 12.00
        e.Update(Side.Left, R(GridCount.G4, 3, loading: true));
        e.Tick(2.0);                              // 10.00
        Check.Near(10.0, e.GetSeconds(Side.Left), 0.001, "加载中倒计时继续走");
        Check.Equal(SideStatus.Loading, e.GetStatus(Side.Left), "状态为 Loading");
    }

    [Fact]
    public static void 加载结束_立即置0_00()
    {
        var e = NewEngine();
        e.Update(Side.Left, R(GridCount.G4, 3));
        e.Update(Side.Left, R(GridCount.G4, 3, loading: true));
        e.Update(Side.Left, R(GridCount.G4, 2)); // 加载结束
        Check.Near(0.0, e.GetSeconds(Side.Left), 0.001, "加载结束应置 0.00");
        Check.Equal(SideStatus.Normal, e.GetStatus(Side.Left), "状态回到 Normal");
    }

    [Fact]
    public static void 加载前后值相同_置0_00()
    {
        var e = NewEngine();
        e.Update(Side.Left, R(GridCount.G4, 3));
        e.Update(Side.Left, R(GridCount.G4, 2)); // 触发 15.00
        e.Tick(5.0);                              // 10.00
        e.Update(Side.Left, R(GridCount.G4, 2, loading: true)); // 加载开始（值 2）
        e.Update(Side.Left, R(GridCount.G4, 2));                 // 加载结束（值仍 2）
        Check.Near(0.0, e.GetSeconds(Side.Left), 0.001, "前后值相同应置 0.00");
    }

    [Fact]
    public static void 加载结束_随后减1_正常触发()
    {
        var e = NewEngine();
        e.Update(Side.Left, R(GridCount.G4, 3, loading: true));
        e.Update(Side.Left, R(GridCount.G4, 2)); // 加载结束，基线 2
        e.Update(Side.Left, R(GridCount.G4, 1)); // 2→1 减1
        Check.Equal(SideStatus.Counting, e.GetStatus(Side.Left), "加载结束后的减1应触发");
        Check.Near(15.0, e.GetSeconds(Side.Left), 0.001, "秒数=15.00");
    }

    [Fact]
    public static void 倒计时归零_保持0_00()
    {
        var e = NewEngine();
        e.Update(Side.Left, R(GridCount.G4, 4));
        e.Update(Side.Left, R(GridCount.G4, 3));
        e.Tick(15.5);
        Check.Near(0.0, e.GetSeconds(Side.Left), 0.001, "归零后为 0.00");
        Check.Equal(SideStatus.Normal, e.GetStatus(Side.Left), "归零后状态 Normal");
        e.Tick(3.0);
        Check.Near(0.0, e.GetSeconds(Side.Left), 0.001, "保持 0.00");
    }

    [Fact]
    public static void 左右完全独立()
    {
        var e = NewEngine();
        e.Update(Side.Left, R(GridCount.G4, 4));
        e.Update(Side.Left, R(GridCount.G4, 3)); // 左触发
        Check.Equal(SideStatus.Counting, e.GetStatus(Side.Left), "左倒计时中");
        Check.Equal(SideStatus.Normal, e.GetStatus(Side.Right), "右不受影响");
        Check.Near(0.0, e.GetSeconds(Side.Right), 0.001, "右秒数 0");
    }

    [Fact]
    public static void 左右同时各自减1_各自触发()
    {
        var e = NewEngine();
        e.Update(Side.Left, R(GridCount.G4, 4));
        e.Update(Side.Right, R(GridCount.G4, 4));
        e.Update(Side.Left, R(GridCount.G4, 3));
        e.Update(Side.Right, R(GridCount.G4, 3));
        Check.Equal(SideStatus.Counting, e.GetStatus(Side.Left), "左倒计时中");
        Check.Equal(SideStatus.Counting, e.GetStatus(Side.Right), "右倒计时中");
        Check.Near(15.0, e.GetSeconds(Side.Left), 0.001, "左秒数");
        Check.Near(15.0, e.GetSeconds(Side.Right), 0.001, "右秒数");
    }

    [Fact]
    public static void 格数变化_倒计时不打断()
    {
        var e = NewEngine();
        e.Update(Side.Left, R(GridCount.G4, 4));
        e.Update(Side.Left, R(GridCount.G4, 3)); // 15.00
        e.Tick(2.0);                              // 13.00
        e.Update(Side.Left, R(GridCount.G6, 3));  // 格数变化：不重置倒计时
        Check.Near(13.0, e.GetSeconds(Side.Left), 0.001, "格数变化不应打断倒计时");
        Check.Equal(SideStatus.Counting, e.GetStatus(Side.Left), "状态保持 Counting");
    }

    [Fact]
    public static void 六格_减1触发()
    {
        var e = NewEngine();
        e.Update(Side.Left, R(GridCount.G6, 6));
        e.Update(Side.Left, R(GridCount.G6, 5));
        Check.Equal(SideStatus.Counting, e.GetStatus(Side.Left), "6→5 应触发");
        Check.Near(15.0, e.GetSeconds(Side.Left), 0.001, "秒数=15.00");
    }

    [Fact]
    public static void 六格_减2以上_不触发()
    {
        var e = NewEngine();
        int triggers = 0;
        e.Triggered += (_, _) => triggers++;
        e.Update(Side.Left, R(GridCount.G6, 6));
        e.Update(Side.Left, R(GridCount.G6, 3));
        Check.Equal(0, triggers, "6→3 不应触发");
        Check.Near(0.0, e.GetSeconds(Side.Left), 0.001, "秒数保持 0");
    }

    [Fact]
    public static void Reset_回到初始()
    {
        var e = NewEngine();
        e.Update(Side.Left, R(GridCount.G4, 4));
        e.Update(Side.Left, R(GridCount.G4, 3));
        e.Update(Side.Right, R(GridCount.G6, 6));
        e.Update(Side.Right, R(GridCount.G6, 5));
        e.Reset();
        Check.Equal(SideStatus.Normal, e.GetStatus(Side.Left), "左状态复位");
        Check.Equal(SideStatus.Normal, e.GetStatus(Side.Right), "右状态复位");
        Check.Near(0.0, e.GetSeconds(Side.Left), 0.001, "左秒数复位");
        Check.Near(0.0, e.GetSeconds(Side.Right), 0.001, "右秒数复位");
    }

    [Fact]
    public static void 构造函数_非法时长_抛异常()
    {
        Check.Throws<ArgumentOutOfRangeException>(() => _ = new CountdownEngine(0), "时长为 0 应抛异常");
        Check.Throws<ArgumentOutOfRangeException>(() => _ = new CountdownEngine(-1), "时长为负应抛异常");
    }

    [Fact]
    public static void 触发事件_携带旧值新值动作()
    {
        var e = NewEngine();
        TriggerEvent? captured = null;
        e.Triggered += (_, evt) => captured = evt;
        e.Update(Side.Left, R(GridCount.G4, 4));
        e.Update(Side.Left, R(GridCount.G4, 3));
        Check.True(captured != null, "应捕获触发事件");
        Check.Equal(Side.Left, captured!.Side, "侧别");
        Check.Equal(GridCount.G4, captured.GridCount, "格数");
        Check.Equal(4, captured.OldValue, "旧值");
        Check.Equal(3, captured.NewValue, "新值");
        Check.Equal("start", captured.Action, "动作");
    }
}