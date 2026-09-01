using System.Diagnostics;

namespace NaruttoTimer.Capture;

/// <summary>ADB 命令行封装：设备发现、adb exec-out 原始流。</summary>
public sealed class AdbCli
{
    private readonly string _adbPath;

    public AdbCli(string adbPath) => _adbPath = adbPath;

    public string AdbPath => _adbPath;

    public static string FindAdbPath(string? configured = null)
    {
        if (!string.IsNullOrEmpty(configured) && File.Exists(configured)) return configured;
        // 默认查找顺序：输出目录 → vendor/scrcpy → scrcpy 安装目录 → PATH
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "adb.exe"),
            Path.Combine(AppContext.BaseDirectory, "vendor", "scrcpy", "adb.exe"),
            Path.Combine(Environment.CurrentDirectory, "vendor", "scrcpy", "adb.exe"),
            @"E:\8-30文档\narutto\scrcpy-win64-v4.1\scrcpy-win64-v4.1\adb.exe",
            "adb",
        };
        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }
        return "adb";
    }

    /// <summary>解析 adb devices 输出（测试用，纯函数）。</summary>
    public static List<(string Serial, string State)> ParseDevices(string output)
    {
        var result = new List<(string, string)>();
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("List of devices")) continue;
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) result.Add((parts[0], parts[1]));
        }
        return result;
    }

    public async Task<List<(string Serial, string State)>> DevicesAsync(CancellationToken ct)
    {
        var (stdout, _) = await RunAsync(new[] { "devices" }, ct).ConfigureAwait(false);
        return ParseDevices(stdout);
    }

    public async Task<(string Stdout, string Stderr)> RunAsync(string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _adbPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("adb 启动失败");
        var outTask = proc.StandardOutput.ReadToEndAsync(ct);
        var errTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        return (await outTask.ConfigureAwait(false), await errTask.ConfigureAwait(false));
    }

    /// <summary>启动 adb exec-out 并返回标准输出流（原始字节）。</summary>
    public Process StartExecOut(string[] args, out Stream stdout)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _adbPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        var proc = Process.Start(psi) ?? throw new InvalidOperationException("adb exec-out 启动失败");
        stdout = proc.StandardOutput.BaseStream;
        return proc;
    }
}