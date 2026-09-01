using NaruttoTimer.Data;

namespace NaruttoTimer.Tests;

public static class SettingsTests
{
    [Fact]
    public static void 默认设置可序列化往返()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nt-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonSettingsStore(path);
            var def = new AppSettings();
            store.Save(def);
            var loaded = store.Load();
            Check.Equal(def.DeviceSerial, loaded.DeviceSerial, "设备号");
            Check.Equal(def.CountdownSeconds, loaded.CountdownSeconds, "倒计时秒数");
            Check.Equal(def.BrightRange, loaded.BrightRange, "亮阈值");
            Check.Equal(def.DarkRange, loaded.DarkRange, "暗阈值");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public static void 修改后的设置往返保持()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nt-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonSettingsStore(path);
            var s = new AppSettings
            {
                DeviceSerial = "emulator-test",
                MaxFps = 60,
                OverlayOpacityPercent = 70,
                LeftRoi = new RoiConfig(96, 34, 180, 52),
                RightRoi = new RoiConfig(1844, 34, 180, 52)
            };
            store.Save(s);
            var loaded = store.Load();
            Check.Equal("emulator-test", loaded.DeviceSerial, "设备号");
            Check.Equal(60, loaded.MaxFps, "帧率");
            Check.Equal(70, loaded.OverlayOpacityPercent, "透明度");
            Check.Equal(s.LeftRoi, loaded.LeftRoi, "左区域");
            Check.Equal(s.RightRoi, loaded.RightRoi, "右区域");
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
        var s = store.Load();
        Check.Equal("emulator-7834", s.DeviceSerial, "默认设备号");
    }

    [Fact]
    public static void 损坏文件返回默认设置()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nt-broken-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{{{ not json");
            var store = new JsonSettingsStore(path);
            var s = store.Load();
            Check.Equal("emulator-7834", s.DeviceSerial, "损坏时回退默认");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public static void 默认ROI已预置且落在帧内()
    {
        var s = new AppSettings();
        Check.True(s.LeftRoi != null, "默认左区不应为空");
        Check.True(s.RightRoi != null, "默认右区不应为空");
        var l = s.LeftRoi!; var r = s.RightRoi!;
        Check.True(l.X >= 0 && l.X + l.Width <= 1280, "左区X在帧宽内");
        Check.True(l.Y >= 0 && l.Y + l.Height <= 150, "左区Y在帧高内");
        Check.True(r.X >= 0 && r.X + r.Width <= 1280, "右区X在帧宽内");
        Check.True(r.Y >= 0 && r.Y + r.Height <= 150, "右区Y在帧高内");
        Check.Equal(AppSettings.DefaultLeftRoi, s.LeftRoi, "左区等于默认");
        Check.Equal(AppSettings.DefaultRightRoi, s.RightRoi, "右区等于默认");
    }
}
