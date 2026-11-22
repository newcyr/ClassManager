using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ClassManager.Core;

namespace ClassManager.App;

/// <summary>一次"定时网页"打开的浏览器会话，负责在结束时间关闭。</summary>
public interface IBrowserSession
{
    void Close();
}

internal static class WindowUtil
{
    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_CLOSE = 0x0010;

    internal static readonly HashSet<string> BrowserProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "iexplore", "360se", "360chrome", "360ChromeX",
        "QQBrowser", "SogouExplorer", "opera", "brave", "vivaldi", "liebao", "Maxthon", "ucbrowser"
    };

    internal sealed record WindowInfo(IntPtr Handle, int Pid, string Title, string Process);

    internal static List<WindowInfo> VisibleWindows()
    {
        var list = new List<WindowInfo>();
        try
        {
            EnumWindows((h, _) =>
            {
                if (!IsWindowVisible(h) || GetWindowTextLength(h) <= 0) return true;
                GetWindowThreadProcessId(h, out var pid);
                var title = Title(h);
                var name = "";
                try { name = Process.GetProcessById((int)pid).ProcessName; } catch { }
                list.Add(new WindowInfo(h, (int)pid, title, name));
                return true;
            }, IntPtr.Zero);
        }
        catch { }
        return list;
    }

    private static string Title(IntPtr h)
    {
        var len = GetWindowTextLength(h);
        if (len <= 0) return "";
        var sb = new StringBuilder(len + 2);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    internal static void CloseWindow(IntPtr h)
    {
        try { PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); } catch { }
    }
}

/// <summary>识别系统默认浏览器，并用"新窗口"方式启动，便于结束后精确关闭。</summary>
internal static class BrowserLauncher
{
    internal static string? DetectDefaultBrowser()
    {
        try
        {
            const string key = @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice";
            var progId = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key)?.GetValue("ProgId") as string;
            if (string.IsNullOrEmpty(progId)) return null;
            if (progId.StartsWith("MSEdge", StringComparison.OrdinalIgnoreCase)) return "edge";
            if (progId.StartsWith("ChromeHTML", StringComparison.OrdinalIgnoreCase)) return "chrome";
            if (progId.StartsWith("FirefoxURL", StringComparison.OrdinalIgnoreCase)) return "firefox";
        }
        catch { }
        return null;
    }

    internal static bool TryOpenNewWindow(string kind, string url)
    {
        string? exe = kind switch
        {
            "edge" => FirstExisting(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe")),
            "chrome" => FirstExisting(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe")),
            "firefox" => FirstExisting(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Mozilla Firefox\firefox.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Mozilla Firefox\firefox.exe")),
            _ => null
        };
        if (exe == null) return false;

        var args = kind == "firefox" ? $"-new-window \"{url}\"" : $"--new-window \"{url}\"";
        Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false });
        return true;
    }

    private static string? FirstExisting(params string[] paths) => paths.FirstOrDefault(File.Exists);
}

/// <summary>用系统默认浏览器打开网页；记录新出现的浏览器窗口，结束时关闭它们。</summary>
public sealed class DefaultBrowserSession : IBrowserSession
{
    private readonly List<IntPtr> _handles = new();
    private readonly List<int> _pids = new();
    private readonly DateTime _openedAt = DateTime.Now;
    private readonly object _lock = new();
    private bool _closed;

    public static DefaultBrowserSession Open(string url)
    {
        var session = new DefaultBrowserSession();
        var before = WindowUtil.VisibleWindows()
            .Where(w => WindowUtil.BrowserProcesses.Contains(w.Process))
            .Select(w => w.Handle)
            .ToHashSet();

        try
        {
            // 优先按默认浏览器强制"新窗口"打开：既保持默认浏览器，又能可靠地在结束时关闭
            var kind = BrowserLauncher.DetectDefaultBrowser();
            if (kind != null && BrowserLauncher.TryOpenNewWindow(kind, url))
                AppPaths.Log("smarturl", $"已用默认浏览器（新窗口，{kind}）打开：{url}");
            else
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                AppPaths.Log("smarturl", "已用默认浏览器打开：" + url);
            }
        }
        catch (Exception ex)
        {
            AppPaths.Log("smarturl", "打开浏览器失败：" + ex.Message);
            return session;
        }

        _ = Task.Run(async () =>
        {
            for (var i = 0; i < 15; i++)
            {
                await Task.Delay(1000);
                lock (session._lock)
                {
                    if (session._closed) return;
                    foreach (var w in WindowUtil.VisibleWindows())
                    {
                        if (!WindowUtil.BrowserProcesses.Contains(w.Process)) continue;
                        if (before.Contains(w.Handle) || session._handles.Contains(w.Handle)) continue;
                        session._handles.Add(w.Handle);
                        if (!session._pids.Contains(w.Pid)) session._pids.Add(w.Pid);
                        AppPaths.Log("smarturl", $"记录浏览器窗口：{w.Process}「{w.Title}」");
                    }
                    if (session._handles.Count > 0 && i >= 5) return;
                }
            }
            lock (session._lock)
            {
                if (!session._closed && session._handles.Count == 0)
                    AppPaths.Log("smarturl", "未检测到新的浏览器窗口（网页可能作为标签页打开），结束时无法自动关闭。");
            }
        });

        return session;
    }

    public void Close()
    {
        List<IntPtr> handles;
        List<int> pids;
        lock (_lock)
        {
            if (_closed) return;
            _closed = true;
            handles = _handles.ToList();
            pids = _pids.ToList();
        }

        foreach (var h in handles) WindowUtil.CloseWindow(h);
        AppPaths.Log("smarturl", $"结束时间到，已关闭 {handles.Count} 个浏览器窗口。");

        _ = Task.Run(async () =>
        {
            await Task.Delay(2500);
            foreach (var pid in pids)
            {
                try
                {
                    var p = Process.GetProcessById(pid);
                    if (p.HasExited) continue;
                    // 仅结束开始时间之后启动、且确实没有窗口残留的浏览器进程
                    if (p.StartTime < _openedAt.AddSeconds(-2)) continue;
                    p.Kill(true);
                    AppPaths.Log("smarturl", $"已结束后台浏览器进程 {pid}。");
                }
                catch { }
            }
        });
    }
}
