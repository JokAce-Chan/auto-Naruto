using System.Text.Json;
using System.Text.Json.Serialization;
using NaruttoTimer.Data;
using NaruttoTimer.Rules;

namespace NaruttoTimer.Tests;

/// <summary>P4 数据保存测试。</summary>
public static class DataStoreTests
{
    private static TriggerEvent Evt(DateTime ts, Side side, GridCount grid, int oldV, int newV, string action = "start") =>
        new(ts, side, grid, oldV, newV, action);

    [Fact]
    public static void 追加触发_写入按日期JSONL()
    {
        var dir = TestTemp.NewDir();
        try
        {
            var store = new DataStore(dir);
            var ts = new DateTime(2026, 9, 1, 12, 0, 1, DateTimeKind.Local);
            store.AppendTrigger(Evt(ts, Side.Left, GridCount.G4, 4, 3));
            var file = Path.Combine(dir, "2026-09-01", "events.jsonl");
            Check.True(File.Exists(file), "事件文件应存在");
            var lines = File.ReadAllLines(file);
            Check.Equal(1, lines.Length, "一行一个事件");
            Check.True(lines[0].Contains("Left"), "包含侧别");
            Check.True(lines[0].Contains("G4"), "包含格数");
            Check.True(lines[0].Contains("4"), "包含旧值");
        }
        finally { TestTemp.Delete(dir); }
    }

    [Fact]
    public static void 追加触发_不同日期_分文件()
    {
        var dir = TestTemp.NewDir();
        try
        {
            var store = new DataStore(dir);
            store.AppendTrigger(Evt(new DateTime(2026, 8, 31, 10, 0, 0, DateTimeKind.Local), Side.Left, GridCount.G4, 4, 3));
            store.AppendTrigger(Evt(new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Local), Side.Right, GridCount.G6, 6, 5));
            Check.True(File.Exists(Path.Combine(dir, "2026-08-31", "events.jsonl")), "8月31日文件");
            Check.True(File.Exists(Path.Combine(dir, "2026-09-01", "events.jsonl")), "9月1日文件");
            Check.Equal(2, store.GetTriggers().Count, "跨日共 2 条");
        }
        finally { TestTemp.Delete(dir); }
    }

    [Fact]
    public static void 查询_按时间范围过滤()
    {
        var dir = TestTemp.NewDir();
        try
        {
            var store = new DataStore(dir);
            store.AppendTrigger(Evt(new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Local), Side.Left, GridCount.G4, 4, 3));
            store.AppendTrigger(Evt(new DateTime(2026, 9, 1, 13, 0, 0, DateTimeKind.Local), Side.Right, GridCount.G4, 3, 2));
            store.AppendTrigger(Evt(new DateTime(2026, 9, 1, 14, 0, 0, DateTimeKind.Local), Side.Left, GridCount.G6, 6, 5));
            var from = new DateTime(2026, 9, 1, 12, 30, 0, DateTimeKind.Local);
            var to = new DateTime(2026, 9, 1, 13, 30, 0, DateTimeKind.Local);
            var filtered = store.GetTriggers(from, to);
            Check.Equal(1, filtered.Count, "范围内 1 条");
            Check.Equal(Side.Right, filtered[0].Side, "范围内为右侧事件");
        }
        finally { TestTemp.Delete(dir); }
    }

    [Fact]
    public static void 查询_无数据_返回空()
    {
        var dir = TestTemp.NewDir();
        try
        {
            var store = new DataStore(dir);
            Check.Equal(0, store.GetTriggers().Count, "空目录返回空");
        }
        finally { TestTemp.Delete(dir); }
    }

    [Fact]
    public static void 查询_损坏行_跳过()
    {
        var dir = TestTemp.NewDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "2026-09-01"));
            File.WriteAllLines(Path.Combine(dir, "2026-09-01", "events.jsonl"), new[]
            {
                "{broken",
                "{\"Timestamp\":\"2026-09-01T12:00:00\",\"Side\":\"Left\",\"GridCount\":\"G4\",\"OldValue\":4,\"NewValue\":3,\"Action\":\"start\"}",
            });
            var store = new DataStore(dir);
            Check.Equal(1, store.GetTriggers().Count, "损坏行应被跳过");
        }
        finally { TestTemp.Delete(dir); }
    }

    [Fact]
    public static void 导出CSV_表头与数据行()
    {
        var dir = TestTemp.NewDir();
        try
        {
            var store = new DataStore(dir);
            store.AppendTrigger(Evt(new DateTime(2026, 9, 1, 12, 0, 1, DateTimeKind.Local), Side.Left, GridCount.G4, 4, 3, "start,特殊"));
            var outPath = Path.Combine(dir, "export.csv");
            store.ExportCsv(outPath);
            Check.True(File.Exists(outPath), "CSV 文件应存在");
            var content = File.ReadAllText(outPath);
            Check.True(content.StartsWith("Timestamp,Side,GridCount,OldValue,NewValue,Action"), "含表头");
            Check.True(content.Contains("Left"), "含侧别");
            Check.True(content.Contains("\"start,特殊\""), "含逗号的字段应被引号包裹");
        }
        finally { TestTemp.Delete(dir); }
    }

    [Fact]
    public static void 导出JSON_可反序列化()
    {
        var dir = TestTemp.NewDir();
        try
        {
            var store = new DataStore(dir);
            store.AppendTrigger(Evt(new DateTime(2026, 9, 1, 12, 0, 1, DateTimeKind.Local), Side.Left, GridCount.G6, 6, 5));
            var outPath = Path.Combine(dir, "export.json");
            store.ExportJson(outPath);
            var json = File.ReadAllText(outPath);
            var list = JsonSerializer.Deserialize<List<TriggerEvent>>(json, new JsonSerializerOptions
            {
                Converters = { new JsonStringEnumConverter() },
            });
            Check.True(list != null && list.Count == 1, "JSON 应含 1 条");
            Check.Equal(Side.Left, list![0].Side, "侧别还原");
            Check.Equal(GridCount.G6, list[0].GridCount, "格数还原");
            Check.Equal(5, list[0].NewValue, "新值还原");
        }
        finally { TestTemp.Delete(dir); }
    }

    [Fact]
    public static void 设置_无文件返回默认()
    {
        var dir = TestTemp.NewDir();
        try
        {
            var store = new DataStore(dir);
            var s = store.LoadSettings();
            Check.Equal("emulator-7834", s.DeviceSerial, "默认设备号");
            Check.Near(15.0, s.CountdownSeconds, 0.001, "默认倒计时 15s");
        }
        finally { TestTemp.Delete(dir); }
    }

    [Fact]
    public static void 设置_保存后加载一致()
    {
        var dir = TestTemp.NewDir();
        try
        {
            var store = new DataStore(dir);
            var s = new AppSettings { DeviceSerial = "emulator-9999", CountdownSeconds = 20.0, DebounceFrames = 5 };
            store.SaveSettings(s);
            var loaded = store.LoadSettings();
            Check.Equal("emulator-9999", loaded.DeviceSerial, "设备号");
            Check.Near(20.0, loaded.CountdownSeconds, 0.001, "倒计时");
            Check.Equal(5, loaded.DebounceFrames, "防抖帧数");
        }
        finally { TestTemp.Delete(dir); }
    }

    [Fact]
    public static void 追加日志_写入日期文件()
    {
        var dir = TestTemp.NewDir();
        try
        {
            var store = new DataStore(dir);
            store.AppendLog("测试日志");
            var file = Path.Combine(dir, DateTime.Now.ToString("yyyy-MM-dd"), "log.txt");
            Check.True(File.Exists(file), "日志文件应存在");
            Check.True(File.ReadAllText(file).Contains("测试日志"), "日志内容写入");
        }
        finally { TestTemp.Delete(dir); }
    }
}