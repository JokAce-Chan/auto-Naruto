namespace NaruttoTimer.Capture;

/// <summary>定位 FFmpeg DLL（avcodec-62.dll / avutil-60.dll）所在目录。</summary>
public static class FfmpegLocator
{
    public static string FindDirectory(string? configured = null)
    {
        if (!string.IsNullOrEmpty(configured) && File.Exists(Path.Combine(configured, "avcodec-62.dll")))
            return configured;

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "vendor", "scrcpy"),
            Path.Combine(Environment.CurrentDirectory, "vendor", "scrcpy"),
            Path.Combine(Environment.CurrentDirectory, "scrcpy-win64-v4.1", "scrcpy-win64-v4.1"),
            @"E:\8-30文档\narutto\scrcpy-win64-v4.1\scrcpy-win64-v4.1",
        };
        foreach (var c in candidates)
        {
            if (File.Exists(Path.Combine(c, "avcodec-62.dll")) && File.Exists(Path.Combine(c, "avutil-60.dll")))
                return c;
        }
        throw new InvalidOperationException("找不到 FFmpeg DLL（avcodec-62.dll / avutil-60.dll），请在设置中配置 scrcpy 目录");
    }
}