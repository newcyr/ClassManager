using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ClassManager.Core;

/// <summary>应用运行路径与日志。</summary>
public static class AppPaths
{
    public static string DataDir
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClassManager");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string ConfigFile => Path.Combine(DataDir, "config.json");
    public static string StateFile => Path.Combine(DataDir, "state.json");
    public static string LogDir
    {
        get
        {
            var dir = Path.Combine(DataDir, "logs");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string BaseDir => AppContext.BaseDirectory;
    public static string BuiltinRingtone => Path.Combine(BaseDir, "assets", "ringtone.wav");
    public static string AppIcon => Path.Combine(BaseDir, "assets", "icon.ico");
    public static string GuideUrl => "https://newcyr.github.io/class/kj.html";

    public static void Log(string category, string message)
    {
        try
        {
            var file = Path.Combine(LogDir, category + ".log");
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
            File.AppendAllText(file, line);
        }
        catch
        {
            // 日志失败绝不影响主流程
        }
    }
}
