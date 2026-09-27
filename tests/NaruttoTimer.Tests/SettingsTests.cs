using NaruttoTimer.App;
using NaruttoTimer.Data;
using NaruttoTimer.Rules;

namespace NaruttoTimer.Tests;

/// <summary>设置持久化测试（对齐源项目「六道带土计时器2.0」默认值）。</summary>
public static class SettingsTests
{
    private static string NewPath() =>
        Path.Combine(Path.GetTempPath(), $"nt-settings-{Guid.NewGuid():N}.json");

    [Fact]
    public static void 默认设置_对齐源项目()
    {
        var s = new AppSettings();
        Check.Equal("emulator-7834", s.DeviceSerial, "默认设备号");
        Check.Near(14.5, s.CountdownSeconds, 0.001, "源项目 cdSeconds = 14.5");
        Check.Equal(3, s.StableFrames, "源项目 StableValueTracker(3)");
        Check.Equal(125, s.InferenceIntervalMs, "源项目 8FPS → 125ms");
        Check.Near(0.7, s.ConfidenceThreshold, 0.001, "源项目置信度 0.7");
        Check.Near(0.25, s.NmsThreshold, 0.001, "源项目 NMS 0.25");
        Check.Equal(EnergyJudgement.Value, s.Judgement, "默认判定方式：值判定");
        Check.Equal(1280, s.VideoWidth, "整帧宽");
        Check.Equal(720, s.VideoHeight, "整帧高（不再裁切顶部条带）");
    }

    [Fact]
    public static void 默认设置可序列化往返()
    {
        var path = NewPath();
        try
        {
            var store = new JsonSettingsStore(path);
            var def = new AppSettings();
            store.Save(def);
            var loaded = store.Load();
            Check.Equal(def.DeviceSerial, loaded.DeviceSerial, "设备号");
            Check.Near(def.CountdownSeconds, loaded.CountdownSeconds, 0.0001, "倒计时秒数");
            Check.Equal(def.StableFrames, loaded.StableFrames, "稳定帧数");
            Check.Equal(def.InferenceIntervalMs, loaded.InferenceIntervalMs, "推理间隔");
            Check.Near(def.ConfidenceThreshold, loaded.ConfidenceThreshold, 0.0001, "置信度");
            Check.Near(def.NmsThreshold, loaded.NmsThreshold, 0.0001, "NMS");
            Check.Equal(def.Judgement, loaded.Judgement, "判定方式");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public static void 修改后的设置往返保持()
    {
        var path = NewPath();
        try
        {
            var store = new JsonSettingsStore(path);
            var s = new AppSettings
            {
                DeviceSerial = "emulator-test",
                MaxFps = 60,
                OverlayOpacityPercent = 70,
                OverlayTextOpacityPercent = 40,
                CountdownSeconds = 13.0,
                StableFrames = 5,
                Judgement = EnergyJudgement.EmptyCount,
            };
            s.Label.EnergyBar.Left = new LabelRect(0.1, 0.05, 0.22, 0.19);
            store.Save(s);

            var loaded = store.Load();
            Check.Equal("emulator-test", loaded.DeviceSerial, "设备号");
            Check.Equal(60, loaded.MaxFps, "帧率");
            Check.Equal(70, loaded.OverlayOpacityPercent, "背景透明度");
            Check.Equal(40, loaded.OverlayTextOpacityPercent, "数字透明度");
            Check.Near(13.0, loaded.CountdownSeconds, 0.0001, "倒计时");
            Check.Equal(5, loaded.StableFrames, "稳定帧数");
            Check.Equal(EnergyJudgement.EmptyCount, loaded.Judgement, "判定方式");
            Check.Near(0.22, loaded.Label.EnergyBar.Left.RightBottom.X, 0.0001, "左条右边界");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public static void 文件不存在返回默认设置()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nt-missing-{Guid.NewGuid():N}.json");
        var store = new JsonSettingsStore(path);
        Check.Equal("emulator-7834", store.Load().DeviceSerial, "默认设备号");
    }

    [Fact]
    public static void 损坏文件返回默认设置()
    {
        var path = NewPath();
        try
        {
            File.WriteAllText(path, "{{{ not json");
            var store = new JsonSettingsStore(path);
            Check.Equal("emulator-7834", store.Load().DeviceSerial, "损坏时回退默认");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public static void Normalize_修正非法值()
    {
        var s = new AppSettings
        {
            VideoWidth = 0,
            VideoHeight = -5,
            CountdownSeconds = 0,
            StableFrames = 0,
            InferenceIntervalMs = 1,
        }.Normalize();

        Check.Equal(1280, s.VideoWidth, "宽度回退默认");
        Check.Equal(720, s.VideoHeight, "高度回退默认");
        Check.Near(14.5, s.CountdownSeconds, 0.001, "倒计时回退 14.5");
        Check.Equal(1, s.StableFrames, "稳定帧数至少 1");
        Check.Equal(16, s.InferenceIntervalMs, "推理间隔至少 16ms");
    }

    [Fact]
    public static void Normalize_补齐空的标注配置()
    {
        var s = new AppSettings { Label = null! }.Normalize();
        Check.True(s.Label != null, "标注配置不应为空");
        Check.True(s.Label.EnergyBar != null, "能量条配置不应为空");
        Check.Equal(null, s.Label.Validate(), "补齐后应校验通过");
    }

    [Fact]
    public static void 界面记忆_默认宽300_夹取250_550()
    {
        var s = new AppSettings();
        Check.Equal(300, s.SidePanelWidth, "右侧栏默认宽 300");
        Check.False(s.SidePanelCollapsed, "默认不折叠");
        Check.Equal(0, s.GroupOpen.Count, "默认无分组记忆");
        Check.Equal(0, s.UiIcons.Count, "默认无图标记忆");

        Check.Equal(550, new AppSettings { SidePanelWidth = 900 }.Normalize().SidePanelWidth, "宽度上限 550");
        Check.Equal(250, new AppSettings { SidePanelWidth = 100 }.Normalize().SidePanelWidth, "宽度下限 250");

        var dirty = new AppSettings { GroupOpen = null!, UiIcons = null! }.Normalize();
        Check.True(dirty.GroupOpen != null, "分组记忆不应为空");
        Check.True(dirty.UiIcons != null, "图标记忆不应为空");
    }

    [Fact]
    public static void 界面记忆_往返保持()
    {
        var path = NewPath();
        try
        {
            var store = new JsonSettingsStore(path);
            var s = new AppSettings { SidePanelWidth = 420, SidePanelCollapsed = true };
            s.GroupOpen["timer"] = true;
            s.GroupOpen["debug"] = false;
            s.UiIcons[UiIcons.ItemData] = "\uE710";
            store.Save(s);

            var loaded = store.Load();
            Check.Equal(420, loaded.SidePanelWidth, "栏宽");
            Check.True(loaded.SidePanelCollapsed, "折叠态");
            Check.True(loaded.GroupOpen["timer"], "分组开合");
            Check.False(loaded.GroupOpen["debug"], "分组开合（关）");
            Check.Equal("\uE710", loaded.UiIcons[UiIcons.ItemData], "图标记忆");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
