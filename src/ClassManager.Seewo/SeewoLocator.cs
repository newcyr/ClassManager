using System.IO;
using Microsoft.Win32;

namespace ClassManager.Seewo;

/// <summary>在本机定位希沃（Seewo）客户端可执行文件。</summary>
public static class SeewoLocator
{
    public static readonly string[] KnownExeNames =
    {
        "EasiNote5.exe", "EasiNote.exe", "SeewoWhiteboard.exe", "SeewoLink.exe", "Seewo.exe", "SeewoService.exe"
    };

    /// <summary>返回希沃主程序路径；未安装时返回 null，不抛异常。</summary>
    public static string? FindExecutable()
    {
        try
        {
            var fromRegistry = FindFromRegistry();
            if (fromRegistry != null) return fromRegistry;

            var roots = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Seewo"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Seewo"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Seewo")
            };
            foreach (var root in roots.Where(Directory.Exists))
            {
                var hit = SearchDirectory(root, 4);
                if (hit != null) return hit;
            }
        }
        catch
        {
            // 静默
        }
        return null;
    }

    private static string? FindFromRegistry()
    {
        var roots = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\Seewo"
        };
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var root in roots)
            {
                try
                {
                    using var key = hive.OpenSubKey(root);
                    if (key == null) continue;
                    foreach (var sub in key.GetSubKeyNames())
                    {
                        if (!root.EndsWith(@"\Seewo", StringComparison.OrdinalIgnoreCase) && sub.Length == 0) continue;
                        using var item = key.OpenSubKey(sub);
                        var display = item?.GetValue("DisplayName") as string;
                        var location = item?.GetValue("InstallLocation") as string;
                        var icon = item?.GetValue("DisplayIcon") as string;
                        if (string.IsNullOrEmpty(location) && string.IsNullOrEmpty(display)) continue;
                        if (display != null && !(display.Contains("希沃") || display.Contains("Seewo", StringComparison.OrdinalIgnoreCase)))
                            continue;
                        if (!string.IsNullOrEmpty(location) && Directory.Exists(location))
                        {
                            var hit = SearchDirectory(location, 3);
                            if (hit != null) return hit;
                        }
                        if (!string.IsNullOrEmpty(icon))
                        {
                            var exe = icon.Trim('"').Split(',')[0];
                            if (File.Exists(exe) && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                                ExeNameMatches(exe))
                                return exe;
                        }
                    }
                }
                catch
                {
                    // 忽略单个键的访问异常
                }
            }
        }
        return null;
    }

    private static bool ExeNameMatches(string path)
    {
        var name = Path.GetFileName(path);
        return KnownExeNames.Any(n => name.Equals(n, StringComparison.OrdinalIgnoreCase)) ||
               name.Contains("EasiNote", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Seewo", StringComparison.OrdinalIgnoreCase);
    }

    private static string? SearchDirectory(string dir, int depth)
    {
        if (depth < 0) return null;
        try
        {
            foreach (var name in KnownExeNames)
            {
                var direct = Path.Combine(dir, name);
                if (File.Exists(direct)) return direct;
            }
            foreach (var file in Directory.EnumerateFiles(dir, "*.exe"))
            {
                var n = Path.GetFileName(file);
                if (n.Contains("EasiNote", StringComparison.OrdinalIgnoreCase) && n.IndexOf("update", StringComparison.OrdinalIgnoreCase) < 0)
                    return file;
            }
            foreach (var child in Directory.EnumerateDirectories(dir))
            {
                var hit = SearchDirectory(child, depth - 1);
                if (hit != null) return hit;
            }
        }
        catch
        {
            // 无权限目录直接跳过
        }
        return null;
    }
}
