using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace NaruttoTimer.Overlay;

public readonly record struct EmulatorWindowBounds(IntPtr Handle, int X, int Y, int Width, int Height)
{
    public bool IsValid => Handle != IntPtr.Zero && Width > 0 && Height > 0;
}

/// <summary>定位雷电模拟器的可见 RenderWindow，供范围覆盖层绑定。</summary>
public static class EmulatorWindowTracker
{
    private const string MainWindowClass = "LDPlayerMainFrame";
    private const string RenderWindowClass = "RenderWindow";

    public static bool TryFindRenderWindow(
        int instanceIndex,
        string? adbPath,
        out EmulatorWindowBounds bounds)
    {
        bounds = default;
        int processId = TryGetProcessId(instanceIndex, adbPath);
        if (processId <= 0)
        {
            processId = Process.GetProcessesByName("dnplayer")
                .Select(p => p.Id)
                .FirstOrDefault();
        }
        if (processId <= 0) return false;

        IntPtr frame = FindLargestWindow(processId, MainWindowClass);
        if (frame == IntPtr.Zero) return false;
        if (IsIconic(frame) || !IsWindowVisible(frame)) return false;

        IntPtr render = FindLargestChildWindow(frame, RenderWindowClass);
        if (render == IntPtr.Zero) render = frame;
        IntPtr root = GetAncestor(render, 2);
        if (root != IntPtr.Zero && (IsIconic(root) || !IsWindowVisible(root))) return false;
        if (!GetWindowRect(render, out NativeRect rect)) return false;

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0) return false;
        bounds = new EmulatorWindowBounds(render, rect.Left, rect.Top, width, height);
        return true;
    }

    public static bool TryGetBounds(IntPtr handle, out EmulatorWindowBounds bounds)
    {
        bounds = default;
        if (handle == IntPtr.Zero || !IsWindow(handle)) return false;
        if (!IsWindowVisible(handle)) return false;
        IntPtr root = GetAncestor(handle, 2);
        if (root != IntPtr.Zero && (IsIconic(root) || !IsWindowVisible(root))) return false;
        if (!GetWindowRect(handle, out NativeRect rect)) return false;
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0) return false;
        bounds = new EmulatorWindowBounds(handle, rect.Left, rect.Top, width, height);
        return true;
    }

    private static int TryGetProcessId(int instanceIndex, string? adbPath)
    {
        string? consolePath = ResolveConsolePath(adbPath);
        if (consolePath == null) return 0;

        try
        {
            var start = new ProcessStartInfo(consolePath, "list2")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            using var process = Process.Start(start);
            if (process == null) return 0;
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);
            foreach (string line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = line.Split(',', StringSplitOptions.TrimEntries);
                if (parts.Length < 6) continue;
                if (!int.TryParse(parts[0], out int index) || index != instanceIndex) continue;
                return int.TryParse(parts[5], out int pid) ? pid : 0;
            }
        }
        catch
        {
            return 0;
        }
        return 0;
    }

    private static string? ResolveConsolePath(string? adbPath)
    {
        if (!string.IsNullOrWhiteSpace(adbPath))
        {
            string? directory = Path.GetDirectoryName(adbPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                string candidate = Path.Combine(directory, "ldconsole.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }

        foreach (var process in Process.GetProcessesByName("dnplayer"))
        {
            try
            {
                string? executable = process.MainModule?.FileName;
                string? directory = Path.GetDirectoryName(executable);
                if (string.IsNullOrWhiteSpace(directory)) continue;
                string candidate = Path.Combine(directory, "ldconsole.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch
            {
                // 某些进程可能拒绝读取模块路径，继续尝试下一个实例。
            }
        }
        return null;
    }

    private static IntPtr FindLargestWindow(int processId, string className)
    {
        IntPtr best = IntPtr.Zero;
        long bestArea = 0;
        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out uint pid);
            if (pid != processId || !IsWindowVisible(handle)) return true;
            if (!ClassEquals(handle, className)) return true;
            if (!GetWindowRect(handle, out NativeRect rect)) return true;
            long area = Math.Max(0, rect.Right - rect.Left) * (long)Math.Max(0, rect.Bottom - rect.Top);
            if (area <= bestArea) return true;
            best = handle;
            bestArea = area;
            return true;
        }, IntPtr.Zero);
        return best;
    }

    private static IntPtr FindLargestChildWindow(IntPtr parent, string className)
    {
        IntPtr best = IntPtr.Zero;
        long bestArea = 0;
        EnumChildWindows(parent, (handle, _) =>
        {
            if (!IsWindowVisible(handle) || !ClassEquals(handle, className)) return true;
            if (!GetWindowRect(handle, out NativeRect rect)) return true;
            long area = Math.Max(0, rect.Right - rect.Left) * (long)Math.Max(0, rect.Bottom - rect.Top);
            if (area <= bestArea) return true;
            best = handle;
            bestArea = area;
            return true;
        }, IntPtr.Zero);
        return best;
    }

    private static bool ClassEquals(IntPtr handle, string expected)
    {
        var buffer = new StringBuilder(256);
        GetClassName(handle, buffer, buffer.Capacity);
        return string.Equals(buffer.ToString(), expected, StringComparison.Ordinal);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool EnumWindowProc(IntPtr handle, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr handle, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr handle, uint flags);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);
}
