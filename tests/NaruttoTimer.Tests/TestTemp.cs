namespace NaruttoTimer.Tests;

/// <summary>临时目录工具（测试结束后清理）。</summary>
public static class TestTemp
{
    public static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "narutto-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void Delete(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch
        {
            // 清理失败不影响测试结论
        }
    }
}

/// <summary>测试资源定位（识别 assets：best.onnx）。</summary>
public static class TestAssets
{
    /// <summary>定位识别资源目录（优先输出目录 assets，其次仓库源码目录）。</summary>
    public static string FindRecognizerAssets()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("NARUTTO_ASSETS_DIR"),
            Path.Combine(AppContext.BaseDirectory, "assets"),
            Path.Combine(Environment.CurrentDirectory, "src", "NaruttoTimer.Recognition", "assets"),
        };
        foreach (var c in candidates)
        {
            if (!string.IsNullOrEmpty(c) && File.Exists(Path.Combine(c, "best.onnx"))) return c!;
        }
        throw new InvalidOperationException("找不到识别资源目录（best.onnx），请设置 NARUTTO_ASSETS_DIR");
    }

    public static string ModelPath => Path.Combine(FindRecognizerAssets(), "best.onnx");
}
