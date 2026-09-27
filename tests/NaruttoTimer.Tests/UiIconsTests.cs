using NaruttoTimer.App;
using NaruttoTimer.Data;

namespace NaruttoTimer.Tests;

/// <summary>主界面图标设置（验收稿 v6）：目标位 / 候选字形 / 记忆解析。</summary>
public static class UiIconsTests
{
    [Fact]
    public static void 目标位键唯一且字形落在私用区()
    {
        var keys = UiIcons.Targets.Select(t => t.Key).ToList();
        Check.Equal(keys.Count, keys.Distinct().Count(), "目标位键不应重复");

        foreach (var target in UiIcons.Targets)
        {
            Check.Equal(1, target.DefaultGlyph.Length, $"{target.Key} 默认字形应为单字符");
            Check.True(target.DefaultGlyph[0] >= 0xE000 && target.DefaultGlyph[0] <= 0xF8FF,
                $"{target.Key} 应落在 Segoe MDL2 私用区");
            Check.True(target.Section == UiIcons.SectionHead || target.Section == UiIcons.SectionItem,
                $"{target.Key} 分区非法");
        }
    }

    [Fact]
    public static void 候选字形不重复且都有名称()
    {
        var glyphs = UiIcons.Candidates.Select(c => c.Glyph).ToList();
        Check.Equal(glyphs.Count, glyphs.Distinct().Count(), "候选字形不应重复");
        Check.True(UiIcons.Candidates.Count >= 20, "候选字形数量");

        foreach (var candidate in UiIcons.Candidates)
        {
            Check.Equal(1, candidate.Glyph.Length, "候选字形应为单字符");
            Check.True(candidate.Name.Length > 0, "候选字形应有名称");
        }
    }

    [Fact]
    public static void 分区划分_组头五个_条目八个()
    {
        Check.Equal(5, UiIcons.TargetsIn(UiIcons.SectionHead).Count, "组头 4 组 + 栏底设置");
        Check.Equal(8, UiIcons.TargetsIn(UiIcons.SectionItem).Count, "组内条目目标位");
    }

    [Fact]
    public static void 解析_空值回退默认_自定义优先()
    {
        var custom = new Dictionary<string, string> { [UiIcons.ItemData] = "\uE710" };

        Check.Equal(UiIcons.DefaultOf(UiIcons.ItemData), UiIcons.Resolve(null, UiIcons.ItemData), "无记忆回退默认");
        Check.Equal(UiIcons.DefaultOf(UiIcons.ItemData), UiIcons.Resolve(new Dictionary<string, string>(), UiIcons.ItemData), "空记忆回退默认");
        Check.Equal(UiIcons.DefaultOf(UiIcons.ItemData), UiIcons.Resolve(new Dictionary<string, string> { [UiIcons.ItemData] = "  " }, UiIcons.ItemData), "空白值回退默认");
        Check.Equal("\uE710", UiIcons.Resolve(custom, UiIcons.ItemData), "自定义优先");
        Check.Equal(UiIcons.DefaultOf(UiIcons.ItemPin), UiIcons.Resolve(custom, UiIcons.ItemPin), "未设置的键回退默认");

        Check.Equal("新增", UiIcons.NameOf("\uE710"), "候选名称查询");
        Check.Equal("自定义", UiIcons.NameOf("\uE9F5"), "未知字形");
    }

    [Fact]
    public static void 默认表覆盖全部目标位()
    {
        var defaults = UiIcons.Defaults();
        Check.Equal(UiIcons.Targets.Count, defaults.Count, "默认表条目数");
        foreach (var target in UiIcons.Targets)
            Check.Equal(target.DefaultGlyph, defaults[target.Key], target.Key);
    }

    [Fact]
    public static void 默认表能补齐缺失的图标记忆()
    {
        var settings = new AppSettings().Normalize();
        foreach (var target in UiIcons.Targets)
            Check.True(UiIcons.Resolve(settings.UiIcons, target.Key).Length == 1, $"{target.Key} 应解析出字形");
    }
}
