using NaruttoTimer.Data;
using NaruttoTimer.Rules;

namespace NaruttoTimer.Tests;

public static class ModelsTests
{
    [Fact]
    public static void GridCount_枚举值正确()
    {
        Check.Equal(4, (int)GridCount.G4, "G4 应为 4");
        Check.Equal(6, (int)GridCount.G6, "G6 应为 6");
    }

    [Fact]
    public static void SideValue_合法值不抛异常()
    {
        foreach (var grid in new[] { GridCount.G4, GridCount.G6 })
            for (int v = 0; v <= (int)grid; v++)
                _ = new SideValue(grid, v).Validate();
    }

    [Fact]
    public static void SideValue_越界抛异常()
    {
        Check.Throws<ArgumentOutOfRangeException>(() => new SideValue(GridCount.G4, 5).Validate(), "4格值不能为5");
        Check.Throws<ArgumentOutOfRangeException>(() => new SideValue(GridCount.G6, 7).Validate(), "6格值不能为7");
        Check.Throws<ArgumentOutOfRangeException>(() => new SideValue(GridCount.G4, -1).Validate(), "值不能为负");
    }

    [Fact]
    public static void CountdownSnapshot_按侧取值()
    {
        var snap = new CountdownSnapshot(15.0, 8.32, SideStatus.Counting, SideStatus.Normal);
        Check.Equal(15.0, snap.GetSeconds(Side.Left), "左秒数");
        Check.Equal(8.32, snap.GetSeconds(Side.Right), "右秒数");
        Check.Equal(SideStatus.Counting, snap.GetStatus(Side.Left), "左状态");
        Check.Equal(SideStatus.Normal, snap.GetStatus(Side.Right), "右状态");
    }

    [Fact]
    public static void ColorRange_边界判定()
    {
        var range = new ColorRange(0, 60, 0, 60, 0, 90);
        Check.True(range.Contains(0, 0, 0), "最小值应包含");
        Check.True(range.Contains(60, 60, 90), "最大值应包含");
        Check.False(range.Contains(100, 100, 100), "超范围不应包含");
        Check.False(range.Contains(61, 0, 0), "R 超界不应包含");
    }

    [Fact]
    public static void RecognitionReading_记录加载标志()
    {
        var r = new RecognitionReading(GridCount.G4, 3, false);
        Check.Equal(3, r.Value, "值");
        Check.False(r.InLoading, "非加载");
        var l = new RecognitionReading(GridCount.G4, 0, true);
        Check.True(l.InLoading, "加载中");
    }
}
