using NaruttoTimer.Data;

namespace NaruttoTimer.Tests;

public static class ArenaOverlaySettingsTests
{
    private static string NewPath() =>
        Path.Combine(Path.GetTempPath(), $"nt-arena-{Guid.NewGuid():N}.json");

    [Fact]
    public static void 默认配置_三条横线()
    {
        var arena = new AppSettings().ArenaOverlay;
        Check.False(arena.Enabled, "默认关闭覆盖层");
        Check.Equal(0, arena.EmulatorInstance, "默认雷电实例 0");
        Check.Equal("image", arena.ContentMode, "默认参考图模式");
        Check.Equal(3, arena.Lines.Count, "默认三条横线");
        Check.Near(0.665, arena.Lines[0].Y, 0.0001, "横线 1 Y");
        Check.Near(0.763, arena.Lines[1].Y, 0.0001, "横线 2 Y");
        Check.Near(0.861, arena.Lines[2].Y, 0.0001, "横线 3 Y");
    }

    [Fact]
    public static void Normalize_修正非法配置()
    {
        var arena = new ArenaOverlaySettings
        {
            EmulatorInstance = 999,
            ContentMode = "unknown",
            ImageOpacityPercent = 500,
            Lines = null!,
        }.Normalize();

        Check.Equal(99, arena.EmulatorInstance, "实例上限");
        Check.Equal("image", arena.ContentMode, "未知内容模式回退参考图");
        Check.Equal(100, arena.ImageOpacityPercent, "透明上限");
        Check.Equal(3, arena.Lines.Count, "空横线列表回退默认");

        var line = arena.Lines[0];
        line.Y = 4;
        line.Thickness = 0;
        line.OpacityPercent = -1;
        line.Color = "not-a-color";
        arena.Normalize();
        Check.Near(1.0, line.Y, 0.0001, "Y 上限");
        Check.Near(1.0, line.Thickness, 0.0001, "线宽下限");
        Check.Equal(0, line.OpacityPercent, "透明下限");
        Check.Equal("#FFE65A", line.Color, "非法颜色回退");
    }

    [Fact]
    public static void Json往返_保持横线和实例()
    {
        string path = NewPath();
        try
        {
            var store = new JsonSettingsStore(path);
            var settings = new AppSettings();
            settings.ArenaOverlay.Enabled = true;
            settings.ArenaOverlay.EmulatorInstance = 2;
            settings.ArenaOverlay.ContentMode = "lines";
            settings.ArenaOverlay.EditMode = true;
            settings.ArenaOverlay.Lines[0].Y = 0.42;
            settings.ArenaOverlay.Lines[0].Color = "#00AEEF";
            store.Save(settings);

            var arena = store.Load().Normalize().ArenaOverlay;
            Check.True(arena.Enabled, "启用态");
            Check.Equal(2, arena.EmulatorInstance, "实例 2");
            Check.Equal("lines", arena.ContentMode, "自定义横线模式");
            Check.True(arena.EditMode, "编辑态");
            Check.Near(0.42, arena.Lines[0].Y, 0.0001, "横线位置");
            Check.Equal("#00AEEF", arena.Lines[0].Color, "横线颜色");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
