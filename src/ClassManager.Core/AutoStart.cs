using Microsoft.Win32;

namespace ClassManager.Core;

/// <summary>开机自启动（HKCU\Software\Microsoft\Windows\CurrentVersion\Run）。</summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClassManager";

    public static string ExePath
    {
        get
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrEmpty(path))
                path = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
            return path;
        }
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            var value = key?.GetValue(ValueName) as string;
            return !string.IsNullOrEmpty(value) && value.Contains("ClassManager", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (key == null) return;
            if (enabled)
                key.SetValue(ValueName, "\"" + ExePath + "\" --tray");
            else
                key.DeleteValue(ValueName, false);
        }
        catch (Exception ex)
        {
            AppPaths.Log("autostart", "写入开机自启失败：" + ex.Message);
        }
    }
}
