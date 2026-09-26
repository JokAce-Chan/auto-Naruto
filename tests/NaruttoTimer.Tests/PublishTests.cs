using System.IO;

namespace NaruttoTimer.Tests;

/// <summary>P7 交付物测试：图标、发布脚本、使用说明。</summary>
public static class PublishTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "NaruttoTimer.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("找不到仓库根目录（NaruttoTimer.sln）");
    }

    [Fact]
    public static void 图标_存在且为合法ICO()
    {
        var icoPath = Path.Combine(FindRepoRoot(), "assets", "app.ico");
        Check.True(File.Exists(icoPath), $"图标应存在: {icoPath}");
        var b = File.ReadAllBytes(icoPath);
        Check.True(b.Length > 22, "ICO 大小应大于头部");
        Check.Equal(0, (int)b[0], "reserved=0");
        Check.Equal(0, (int)b[1], "reserved=0");
        Check.Equal(1, (int)b[2], "type=1 (icon)");
        Check.Equal(0, (int)b[3], "type high");
        Check.True(b[5] == 0 && b[4] >= 1, "至少一个图像条目");
        int planes = b[10] | (b[11] << 8);
        int bpp = b[12] | (b[13] << 8);
        Check.Equal(1, planes, "planes=1");
        Check.Equal(32, bpp, "bpp=32");
        int entrySize = b[14] | (b[15] << 8) | (b[16] << 16) | (b[17] << 24);
        int offset = b[18] | (b[19] << 8) | (b[20] << 16) | (b[21] << 24);
        Check.True(offset + entrySize <= b.Length, "条目应在文件范围内");
    }

    [Fact]
    public static void 图标_用于应用_工程已配置()
    {
        var csproj = Path.Combine(FindRepoRoot(), "src", "NaruttoTimer.App", "NaruttoTimer.App.csproj");
        var text = File.ReadAllText(csproj);
        Check.True(text.Contains("<ApplicationIcon>"), "App 工程应配置 ApplicationIcon");
    }

    [Fact]
    public static void 发布脚本_存在且含单文件关键参数()
    {
        var ps1 = Path.Combine(FindRepoRoot(), "tools", "publish.ps1");
        Check.True(File.Exists(ps1), "publish.ps1 应存在");
        var text = File.ReadAllText(ps1);
        Check.True(text.Contains("PublishSingleFile"), "应含单文件参数");
        Check.True(text.Contains("vendor\\scrcpy") || text.Contains("vendor/scrcpy"), "应拷贝 vendor/scrcpy");
    }

    [Fact]
    public static void 使用说明_存在且覆盖关键章节()
    {
        var doc = Path.Combine(FindRepoRoot(), "docs", "使用说明.md");
        Check.True(File.Exists(doc), "使用说明应存在");
        var text = File.ReadAllText(doc);
        Check.True(text.Contains("安装与启动"), "含安装与启动");
        Check.True(text.Contains("区域标注"), "含区域标注（拖拽/输入框）");
        Check.True(text.Contains("识别模式"), "含识别模式（AI / 传统）");
        Check.False(text.Contains("取色校准"), "不应再提及已删除的取色校准");
        Check.True(text.Contains("置顶框设置"), "含置顶框设置");
        Check.True(text.Contains("数据保存"), "含数据保存");
    }
}
