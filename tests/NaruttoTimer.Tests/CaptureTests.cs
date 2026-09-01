using NaruttoTimer.Capture;

namespace NaruttoTimer.Tests;

/// <summary>P2 采集模块测试（不依赖设备在线）。</summary>
public static class CaptureTests
{
    // ── adb devices 解析 ──

    [Fact]
    public static void AdbDevices_ParsesDeviceLines()
    {
        var list = AdbCli.ParseDevices("List of devices attached\nemulator-7834\tdevice\n127.0.0.1:62001 device\n");
        Check.Equal(2, list.Count, "应解析出 2 个设备");
        Check.Equal("emulator-7834", list[0].Serial, "首个设备序列号");
        Check.Equal("device", list[0].State, "首个设备状态");
        Check.Equal("127.0.0.1:62001", list[1].Serial, "第二个设备序列号");
    }

    [Fact]
    public static void AdbDevices_SkipsHeaderBlankAndOffline()
    {
        var list = AdbCli.ParseDevices("List of devices attached\n\nemulator-7834\tdevice\nemulator-5554\toffline\n");
        Check.Equal(2, list.Count, "offline 设备也应保留");
        Check.Equal("offline", list[1].State, "离线状态保留");
    }

    [Fact]
    public static void AdbDevices_EmptyOutputReturnsEmpty()
    {
        Check.Equal(0, AdbCli.ParseDevices("").Count, "空输出应返回空列表");
        Check.Equal(0, AdbCli.ParseDevices("List of devices attached\n\n").Count, "仅有表头应返回空列表");
    }

    // ── FPS 计数器 ──

    [Fact]
    public static void FpsCounter_CountsFramesWithinWindow()
    {
        var fps = new FpsCounter();
        for (int i = 0; i < 10; i++) fps.Push();
        Check.Near(10.0, fps.Current, 0.0, "10 次 Push 后当前 FPS 应为 10");
        fps.Reset();
        Check.Near(0.0, fps.Current, 0.0, "Reset 后应为 0");
    }

    // ── 采集参数默认值 ──

    [Fact]
    public static void CaptureOptions_HasSafeDefaults()
    {
        var o = new CaptureOptions("adb", "emulator-7834");
        Check.Equal(1280, o.VideoWidth, "录制宽度默认 1280");
        Check.Equal(720, o.VideoHeight, "录制高度默认 720（全屏等比）");
        Check.Equal(150, o.CropTopRows, "顶部保留行数默认 150（对应原生 300 行）");
        Check.Equal(30, o.MaxFps, "目标帧率默认 30");
        Check.Equal(4_000_000L, o.BitRate, "码率默认 4Mbps");
        Check.Equal(180, o.TimeLimitSeconds, "单次录制时长默认 180s");
    }

    [Fact]
    public static void CapturedFrame_GetPixel_IndexesCorrectly()
    {
        var pixels = new byte[2 * 2 * 4];
        // 像素(0,0)=白, (1,0)=红, (0,1)=绿, (1,1)=蓝
        pixels[0] = 255; pixels[1] = 255; pixels[2] = 255; pixels[3] = 255;
        pixels[4] = 0; pixels[5] = 0; pixels[6] = 255; pixels[7] = 255;
        pixels[8] = 0; pixels[9] = 255; pixels[10] = 0; pixels[11] = 255;
        pixels[12] = 255; pixels[13] = 0; pixels[14] = 0; pixels[15] = 255;
        var frame = new CapturedFrame { Width = 2, Height = 2, Pixels = pixels, Timestamp = DateTime.UtcNow };
        frame.GetPixel(1, 0, out byte b, out byte g, out byte r, out byte _);
        Check.Equal(0, (int)b, "红像素 B 通道为 0");
        Check.Equal(0, (int)g, "红像素 G 通道为 0");
        Check.Equal(255, (int)r, "红像素 R 通道为 255");
    }

    // ── H.264 解码（真实样本，需要 FFmpeg DLL）──

    [Fact]
    public static void H264Sample_DecodesAtLeastOneFrame()
    {
        string sample = TestPaths.FindSampleH264();
        string ffDir = TestPaths.FindFfmpegDirectory();
        int frames = 0;
        int width = 0;
        int height = 0;
        bool bgraValid = true;

        using (var decoder = new H264StreamDecoder(ffDir))
        {
            decoder.FrameDecoded += (w, h, bgra, ts) =>
            {
                frames++;
                width = w;
                height = h;
                if (bgra.Length != w * h * 4) bgraValid = false;
                for (int i = 3; i < bgra.Length && bgraValid; i += 4)
                    if (bgra[i] != 255) bgraValid = false;
            };

            var bytes = File.ReadAllBytes(sample);
            const int chunkSize = 4096;
            for (int i = 0; i < bytes.Length; i += chunkSize)
            {
                int n = Math.Min(chunkSize, bytes.Length - i);
                decoder.Feed(bytes, i, n);
            }
        }

        Check.True(frames >= 1, $"样本 {sample} 应解出至少 1 帧，实际 {frames}");
        Check.True(width > 0 && height > 0, "帧尺寸应有效");
        Check.True(bgraValid, "BGRA 缓冲区长度应等于 w*h*4 且 alpha 恒为 255");
    }
}

/// <summary>测试资源定位（样本与 FFmpeg DLL 目录）。</summary>
public static class TestPaths
{
    public static string FindSampleH264()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("NARUTTO_TEST_H264"),
            Path.Combine(AppContext.BaseDirectory, "Assets", "screenrecord-test.h264"),
            Path.Combine(Environment.CurrentDirectory, "tests", "NaruttoTimer.Tests", "Assets", "screenrecord-test.h264"),
            Path.Combine(Environment.CurrentDirectory, "screenrecord-test.h264"),
        };
        foreach (var c in candidates)
        {
            if (!string.IsNullOrEmpty(c) && File.Exists(c)) return c;
        }
        throw new InvalidOperationException("找不到 H.264 测试样本，请设置 NARUTTO_TEST_H264 或放置 screenrecord-test.h264");
    }

    public static string FindFfmpegDirectory()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("NARUTTO_FFMPEG_DIR"),
            Path.Combine(AppContext.BaseDirectory, "vendor", "scrcpy"),
            Path.Combine(Environment.CurrentDirectory, "vendor", "scrcpy"),
            Path.Combine(Environment.CurrentDirectory, "scrcpy-win64-v4.1", "scrcpy-win64-v4.1"),
            @"E:\8-30文档\narutto\scrcpy-win64-v4.1\scrcpy-win64-v4.1",
        };
        foreach (var c in candidates)
        {
            if (!string.IsNullOrEmpty(c) && File.Exists(Path.Combine(c, "avcodec-62.dll")) && File.Exists(Path.Combine(c, "avutil-60.dll")))
                return c;
        }
        throw new InvalidOperationException("找不到 FFmpeg DLL（avcodec-62.dll / avutil-60.dll），请设置 NARUTTO_FFMPEG_DIR");
    }
}