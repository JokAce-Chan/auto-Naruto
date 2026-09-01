using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NaruttoTimer.Rules;

namespace NaruttoTimer.Data;

/// <summary>
/// 数据保存：触发事件按日期写入 data/yyyy-MM-dd/events.jsonl，
/// 支持过滤查询、CSV/JSON 导出；配置经 ISettingsStore 读写。
/// </summary>
public sealed class DataStore : IDataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _dataDirectory;
    private readonly ISettingsStore _settings;
    private readonly object _lock = new();

    public DataStore(string dataDirectory, ISettingsStore? settings = null)
    {
        _dataDirectory = dataDirectory;
        _settings = settings ?? new JsonSettingsStore(Path.Combine(dataDirectory, "settings.json"));
    }

    public void AppendTrigger(TriggerEvent evt)
    {
        lock (_lock)
        {
            var file = EventsFile(evt.Timestamp);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.AppendAllText(file, JsonSerializer.Serialize(evt, JsonOptions) + Environment.NewLine);
        }
    }

    public void AppendLog(string message)
    {
        lock (_lock)
        {
            var file = Path.Combine(_dataDirectory, DateTime.Now.ToString("yyyy-MM-dd"), "log.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.AppendAllText(file, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
    }

    public IReadOnlyList<TriggerEvent> GetTriggers(DateTime? from = null, DateTime? to = null)
    {
        lock (_lock)
        {
            var result = new List<TriggerEvent>();
            if (!Directory.Exists(_dataDirectory)) return result;
            foreach (var file in Directory.EnumerateFiles(_dataDirectory, "events.jsonl", SearchOption.AllDirectories))
            {
                foreach (var line in File.ReadLines(file))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var evt = JsonSerializer.Deserialize<TriggerEvent>(line, JsonOptions);
                        if (evt == null) continue;
                        if (from.HasValue && evt.Timestamp < from.Value) continue;
                        if (to.HasValue && evt.Timestamp > to.Value) continue;
                        result.Add(evt);
                    }
                    catch (JsonException)
                    {
                        // 跳过损坏行
                    }
                }
            }
            result.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            return result;
        }
    }

    public AppSettings LoadSettings() => _settings.Load();

    public void SaveSettings(AppSettings settings) => _settings.Save(settings);

    public void ExportCsv(string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Timestamp,Side,GridCount,OldValue,NewValue,Action");
        foreach (var evt in GetTriggers())
        {
            sb.AppendLine(string.Join(",",
                evt.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                evt.Side,
                evt.GridCount,
                evt.OldValue,
                evt.NewValue,
                CsvEscape(evt.Action)));
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    public void ExportJson(string path)
    {
        var json = JsonSerializer.Serialize(GetTriggers(), new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } });
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, json, Encoding.UTF8);
    }

    private string EventsFile(DateTime date) =>
        Path.Combine(_dataDirectory, date.ToString("yyyy-MM-dd"), "events.jsonl");

    private static string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}