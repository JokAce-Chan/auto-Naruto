namespace NaruttoTimer.App;

/// <summary>可自定义的界面图标位（组头 / 组内条目 / 栏底设置）。</summary>
public sealed record UiIconTarget(string Key, string Name, string Section, string DefaultGlyph);

/// <summary>图标候选（Segoe MDL2 Assets 字形）。</summary>
public sealed record UiIconCandidate(string Glyph, string Name);

/// <summary>
/// 界面图标表：候选字形 + 目标位 + 记忆解析（对齐主界面验收稿 v6 的「图标设置」组）。
/// 字形取自 Segoe MDL2 Assets 私用区，直接用字符存放，写进 settings.json 的 UiIcons。
/// </summary>
public static class UiIcons
{
    // ── 目标位键 ──
    public const string GroupTimer = "grp-timer";
    public const string GroupArena = "grp-arena";
    public const string GroupDebug = "grp-debug";
    public const string GroupIcons = "grp-icons";
    public const string PinnedSettings = "pin-settings";
    public const string ItemStatus = "item-status";
    public const string ItemPin = "item-pin";
    public const string ItemRoi = "item-roi";
    public const string ItemParams = "item-params";
    public const string ItemRules = "item-rules";
    public const string ItemScope = "item-scope";
    public const string ItemFrame = "item-frame";
    public const string ItemData = "item-data";

    /// <summary>分组名（组头）。</summary>
    public const string GroupTimerName = "timer";
    public const string GroupArenaName = "arena";
    public const string GroupDebugName = "debug";
    public const string GroupIconsName = "icons";

    // ── 分区标题 ──
    public const string SectionHead = "组头";
    public const string SectionItem = "组内条目";

    public static readonly IReadOnlyList<UiIconTarget> Targets = new[]
    {
        new UiIconTarget(GroupTimer, "替身计时", SectionHead, "\uE823"),
        new UiIconTarget(GroupArena, "决斗场范围显示", SectionHead, "\uE7B3"),
        new UiIconTarget(GroupDebug, "调试", SectionHead, "\uE90F"),
        new UiIconTarget(GroupIcons, "图标设置", SectionHead, "\uE9E9"),
        new UiIconTarget(PinnedSettings, "设置（栏底）", SectionHead, "\uE713"),
        new UiIconTarget(ItemStatus, "识别状态", SectionItem, "\uE946"),
        new UiIconTarget(ItemPin, "置顶框设置", SectionItem, "\uE718"),
        new UiIconTarget(ItemRoi, "区域标注", SectionItem, "\uE71E"),
        new UiIconTarget(ItemParams, "识别与参数", SectionItem, "\uE713"),
        new UiIconTarget(ItemRules, "触发规则说明", SectionItem, "\uE7C3"),
        new UiIconTarget(ItemScope, "范围显示", SectionItem, "\uE7B3"),
        new UiIconTarget(ItemFrame, "导出当前帧", SectionItem, "\uE91B"),
        new UiIconTarget(ItemData, "数据保存/导出", SectionItem, "\uE74E"),
    };

    public static readonly IReadOnlyList<UiIconCandidate> Candidates = new[]
    {
        new UiIconCandidate("\uE823", "时钟"),
        new UiIconCandidate("\uE81C", "历史"),
        new UiIconCandidate("\uE7B3", "眼睛"),
        new UiIconCandidate("\uE721", "搜索"),
        new UiIconCandidate("\uE713", "齿轮"),
        new UiIconCandidate("\uE718", "图钉"),
        new UiIconCandidate("\uE7C1", "旗标"),
        new UiIconCandidate("\uE7C3", "文档"),
        new UiIconCandidate("\uE946", "信息"),
        new UiIconCandidate("\uE8F1", "库"),
        new UiIconCandidate("\uE91B", "图片"),
        new UiIconCandidate("\uE734", "星标"),
        new UiIconCandidate("\uE71E", "放大"),
        new UiIconCandidate("\uE74D", "删除"),
        new UiIconCandidate("\uE74E", "保存"),
        new UiIconCandidate("\uE896", "下载"),
        new UiIconCandidate("\uE72E", "锁定"),
        new UiIconCandidate("\uE8C8", "复制"),
        new UiIconCandidate("\uE710", "新增"),
        new UiIconCandidate("\uE72C", "刷新"),
        new UiIconCandidate("\uE90F", "扳手"),
        new UiIconCandidate("\uE9E9", "调节"),
    };

    /// <summary>取某分区（组头 / 组内条目）的目标位。</summary>
    public static IReadOnlyList<UiIconTarget> TargetsIn(string section) =>
        Targets.Where(t => t.Section == section).ToList();

    /// <summary>目标位的默认字形；未知键返回空串。</summary>
    public static string DefaultOf(string key) =>
        Targets.FirstOrDefault(t => t.Key == key)?.DefaultGlyph ?? "";

    /// <summary>默认的全套图标（用于缺失补齐 / 重置）。</summary>
    public static Dictionary<string, string> Defaults() =>
        Targets.ToDictionary(t => t.Key, t => t.DefaultGlyph);

    /// <summary>解析记忆中的图标；缺键或空值回退默认。</summary>
    public static string Resolve(IReadOnlyDictionary<string, string>? map, string key)
    {
        if (map != null && map.TryGetValue(key, out var glyph) && !string.IsNullOrWhiteSpace(glyph)) return glyph;
        return DefaultOf(key);
    }

    /// <summary>取候选字形对应的中文名；未知字形返回「自定义」。</summary>
    public static string NameOf(string glyph) =>
        Candidates.FirstOrDefault(c => c.Glyph == glyph)?.Name ?? "自定义";
}
