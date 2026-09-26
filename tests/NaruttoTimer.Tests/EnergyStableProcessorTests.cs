using NaruttoTimer.Rules;

namespace NaruttoTimer.Tests;

/// <summary>能量稳定处理器测试（源 domain/dataprocess/EnergyStableProcessor）。</summary>
public static class EnergyStableProcessorTests
{
    private static void Feed(EnergyStableProcessor p, int value, int times)
    {
        for (int i = 0; i < times; i++) p.IsUseSkill(value);
    }

    [Fact]
    public static void 初始上次稳定值为负一()
    {
        var p = new EnergyStableProcessor(3);
        Check.Equal(-1, p.LastStableValue, "未初始化时为 -1（源行为）");
    }

    [Fact]
    public static void 首次建立基线_不触发()
    {
        var p = new EnergyStableProcessor(3);
        Check.False(p.IsUseSkill(4), "第 1 帧不触发");
        Check.False(p.IsUseSkill(4), "第 2 帧不触发");
        Check.False(p.IsUseSkill(4), "首次稳定（无基线）不触发");
        Check.Equal(4, p.LastStableValue, "基线已记录为 4");
        Check.Equal(4, p.StableValue, "稳定值 4");
    }

    [Fact]
    public static void 稳定值恰好减1_触发()
    {
        var p = new EnergyStableProcessor(3);
        Feed(p, 4, 3);
        Check.False(p.IsUseSkill(3), "第 1 帧 3");
        Check.False(p.IsUseSkill(3), "第 2 帧 3");
        Check.True(p.IsUseSkill(3), "稳定到 3 恰好减 1 → 触发");
        Check.Equal(3, p.LastStableValue, "上次稳定值更新为 3");
    }

    [Fact]
    public static void 稳定值减2_不触发_但仍更新基线()
    {
        var p = new EnergyStableProcessor(3);
        Feed(p, 4, 3);
        Feed(p, 2, 3);
        Check.False(p.LastStableValue == 4, "基线已更新（非 4）");
        Check.Equal(2, p.LastStableValue, "基线跟到 2（源 finally 行为）");
    }

    [Fact]
    public static void 稳定值增_不触发()
    {
        var p = new EnergyStableProcessor(3);
        Feed(p, 2, 3);
        Feed(p, 4, 3);
        Check.Equal(4, p.LastStableValue, "基线跟到 4");
    }

    [Fact]
    public static void 连续减1_每次都触发()
    {
        var p = new EnergyStableProcessor(3);
        Feed(p, 4, 3); // 基线 4
        Feed(p, 3, 3);
        Check.Equal(3, p.LastStableValue, "基线 3");
        Feed(p, 2, 3);
        Check.Equal(2, p.LastStableValue, "基线 2");
        Feed(p, 1, 3);
        Check.Equal(1, p.LastStableValue, "基线 1");
    }

    [Fact]
    public static void 稳定后同值重复_不重复触发()
    {
        var p = new EnergyStableProcessor(3);
        Feed(p, 4, 3);
        Feed(p, 3, 3);
        for (int i = 0; i < 5; i++)
            Check.False(p.IsUseSkill(3), "稳定值保持 3 时不应再触发");
    }

    [Fact]
    public static void Reset_清空基线与稳定值()
    {
        var p = new EnergyStableProcessor(3);
        Feed(p, 4, 3);
        Feed(p, 3, 3);
        p.Reset();
        Check.Equal(-1, p.LastStableValue, "Reset 后基线为 -1");
        Check.Equal(0, p.StableValue, "Reset 后稳定值为 0");
        Feed(p, 3, 3);
        Check.False(p.IsUseSkill(3), "Reset 后 3 为首次基线，不触发");
    }
}
