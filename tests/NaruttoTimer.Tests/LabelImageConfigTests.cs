using System.Text.Json;
using NaruttoTimer.Data;

namespace NaruttoTimer.Tests;

/// <summary>标注配置测试（对齐源项目 assets/label_image_config.json）。</summary>
public static class LabelImageConfigTests
{
    [Fact]
    public static void 默认配置_有效()
    {
        var c = LabelImageConfig.CreateDefault();
        Check.Equal(null, c.Validate(), "默认配置应校验通过");
    }

    [Fact]
    public static void 默认矩形_落在归一化范围内且左条在左_右条在右()
    {
        var c = LabelImageConfig.CreateDefault();
        foreach (var (name, rect) in new[]
                 {
                     ("left", c.EnergyBar.Left),
                     ("right", c.EnergyBar.Right),
                 })
        {
            Check.True(rect.LeftTop.X >= 0 && rect.RightBottom.X <= 1, $"{name} x 在 0~1");
            Check.True(rect.LeftTop.Y >= 0 && rect.RightBottom.Y <= 1, $"{name} y 在 0~1");
            Check.True(rect.Width > 0 && rect.Height > 0, $"{name} 宽高为正");
        }

        Check.True(c.EnergyBar.Left.RightBottom.X < c.EnergyBar.Right.LeftTop.X, "左条在右条左侧");
        Check.Near(c.EnergyBar.Left.Height, c.EnergyBar.Right.Height, 0.0001, "左右条等高（源项目同高联动）");
        Check.Near(c.EnergyBar.Left.LeftTop.Y, c.EnergyBar.Right.LeftTop.Y, 0.0001, "左右条顶边对齐");
    }

    [Fact]
    public static void Validate_不合法矩形应报错()
    {
        var badLeft = LabelImageConfig.CreateDefault();
        badLeft.EnergyBar.Left = new LabelRect(0.2, 0.1, 0.2, 0.3);
        Check.True(badLeft.Validate() != null, "左条零宽度应报错");
    }

    [Fact]
    public static void Clone_深拷贝_互不影响()
    {
        var origin = LabelImageConfig.CreateDefault();
        var copy = origin.Clone();

        copy.EnergyBar.Left.LeftTop.X = 0.123;
        copy.EnergyBar.Left.RightBottom.Y = 0.987;

        Check.NotEqual(0.123, origin.EnergyBar.Left.LeftTop.X, "原对象左条不应被修改");
        Check.NotEqual(0.987, origin.EnergyBar.Left.RightBottom.Y, "原对象左条不应被修改");
    }

    [Fact]
    public static void JSON往返_保留属性名与矩形()
    {
        var origin = LabelImageConfig.CreateDefault();
        origin.EnergyBar.Left = new LabelRect(0.11, 0.22, 0.33, 0.44);
        var json = JsonSerializer.Serialize(origin);

        Check.True(json.Contains("energy_bar"), "含 energy_bar 字段名");

        var back = JsonSerializer.Deserialize<LabelImageConfig>(json)!;
        Check.Near(origin.EnergyBar.Left.RightBottom.X, back.EnergyBar.Left.RightBottom.X, 0.0001, "左条还原");
        Check.Near(origin.EnergyBar.Right.LeftTop.X, back.EnergyBar.Right.LeftTop.X, 0.0001, "右条还原");
    }
}
