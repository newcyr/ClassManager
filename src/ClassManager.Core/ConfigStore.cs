using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ClassManager.Core;

/// <summary>课程表 / 设置 JSON 的读写、导入导出与规范化。</summary>
public static class ConfigStore
{
    /// <summary>当前 JSON 结构版本。</summary>
    public const int SchemaVersion = 2;

    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Serialize(AppConfig config) => JsonSerializer.Serialize(config, Options);

    public static AppConfig? Deserialize(string json) => JsonSerializer.Deserialize<AppConfig>(json, Options);

    /// <summary>读取本地配置；不存在或损坏时生成默认配置。</summary>
    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(AppPaths.ConfigFile))
            {
                var cfg = Deserialize(File.ReadAllText(AppPaths.ConfigFile));
                if (cfg != null)
                {
                    foreach (var acc in cfg.SeewoAccounts)
                        acc.Password = CredentialProtector.Unprotect(acc.Password);
                    Normalize(cfg);
                    return cfg;
                }
            }
        }
        catch (Exception ex)
        {
            AppPaths.Log("config", "读取配置失败，改用默认配置：" + ex.Message);
        }

        var def = CreateDefault();
        Save(def);
        return def;
    }

    /// <summary>保存到本地（密码以 DPAPI 密文落盘）。</summary>
    public static void Save(AppConfig config)
    {
        Normalize(config);
        var copy = config.Clone();
        foreach (var acc in copy.SeewoAccounts)
            acc.Password = CredentialProtector.Protect(acc.Password);

        var json = Serialize(copy);
        var tmp = AppPaths.ConfigFile + ".tmp";
        File.WriteAllText(tmp, json);
        File.WriteAllText(AppPaths.ConfigFile, json);
        try { File.Delete(tmp); } catch { }
    }

    /// <summary>导出为可分享的 JSON（默认不包含密码明文）。</summary>
    public static void Export(AppConfig config, string path, bool includePasswords)
    {
        var copy = config.Clone();
        foreach (var acc in copy.SeewoAccounts)
            acc.Password = includePasswords ? acc.Password : "";
        File.WriteAllText(path, Serialize(copy));
    }

    /// <summary>导入 JSON 课程表，返回规范化后的配置。</summary>
    public static AppConfig Import(string path)
    {
        var cfg = Deserialize(File.ReadAllText(path)) ?? throw new InvalidDataException("JSON 内容为空或格式不正确");
        if (cfg.SchemaVersion <= 0) cfg.SchemaVersion = 1;
        if (cfg.SchemaVersion > SchemaVersion)
            throw new InvalidDataException($"schemaVersion={cfg.SchemaVersion} 高于当前程序支持的版本（{SchemaVersion}），请升级应用。");
        foreach (var acc in cfg.SeewoAccounts)
            acc.Password = CredentialProtector.Unprotect(acc.Password);
        Normalize(cfg);
        return cfg;
    }

    public static void Normalize(AppConfig cfg)
    {
        cfg.SchemaVersion = SchemaVersion;
        if (cfg.Subjects == null) cfg.Subjects = new List<string>();
        cfg.Subjects = cfg.Subjects.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct().ToList();
        if (cfg.Subjects.Count == 0) cfg.Subjects.AddRange(DefaultSubjects);

        if (cfg.Timetables == null) cfg.Timetables = new List<Timetable>();
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tt in cfg.Timetables)
        {
            if (string.IsNullOrWhiteSpace(tt.Id) || !usedIds.Add(tt.Id))
            {
                tt.Id = "tt-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                usedIds.Add(tt.Id);
            }
            if (string.IsNullOrWhiteSpace(tt.Name)) tt.Name = "课程表";
            tt.Entries ??= new List<CourseEntry>();
            tt.Entries = tt.Entries.OrderBy(e => ParseTime(e.Time) ?? TimeSpan.MaxValue).ToList();
            foreach (var e in tt.Entries)
            {
                e.Time = NormalizeTime(e.Time);
                e.Subject = (e.Subject ?? "").Trim();
                e.AdvanceMinutes = Math.Clamp(e.AdvanceMinutes, 0, 180);
                e.Note ??= "";
                if (e.Subject.Length > 0 && !cfg.Subjects.Contains(e.Subject)) cfg.Subjects.Add(e.Subject);
            }
        }
        if (cfg.Timetables.Count == 0)
            cfg.Timetables.Add(new Timetable { Id = "tt-a", Name = "课程表A" });

        if (cfg.WeekMapping == null) cfg.WeekMapping = new List<string>();
        while (cfg.WeekMapping.Count < 7) cfg.WeekMapping.Add("");
        if (cfg.WeekMapping.Count > 7) cfg.WeekMapping = cfg.WeekMapping.Take(7).ToList();
        for (int i = 0; i < 7; i++)
        {
            if (!cfg.Timetables.Any(t => t.Id == cfg.WeekMapping[i]))
                cfg.WeekMapping[i] = cfg.Timetables[0].Id;
        }

        cfg.Alarms ??= new List<AlarmItem>();
        foreach (var a in cfg.Alarms)
        {
            if (string.IsNullOrWhiteSpace(a.Id)) a.Id = "alarm-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            a.Time = NormalizeTime(a.Time);
            a.Label = string.IsNullOrWhiteSpace(a.Label) ? "闹钟" : a.Label.Trim();
            a.AdvanceMinutes = Math.Clamp(a.AdvanceMinutes, 0, 180);
        }

        cfg.SeewoAccounts ??= new List<SeewoAccount>();
        foreach (var acc in cfg.SeewoAccounts)
        {
            acc.Subject = (acc.Subject ?? "").Trim();
            acc.Account = (acc.Account ?? "").Trim();
            acc.Password ??= "";
        }
        // 每个科目都保证有一行账号输入框，避免导入后设置页“消失”
        foreach (var subject in cfg.Subjects)
        {
            if (!cfg.SeewoAccounts.Any(a => a.Subject == subject))
                cfg.SeewoAccounts.Add(new SeewoAccount { Subject = subject, Account = "" });
        }

        cfg.DefaultAdvanceMinutes = Math.Clamp(cfg.DefaultAdvanceMinutes, 0, 180);

        cfg.SmartUrls ??= new List<SmartUrlItem>();
        var usedSmartIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in cfg.SmartUrls)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || !usedSmartIds.Add(item.Id))
            {
                item.Id = "url-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                usedSmartIds.Add(item.Id);
            }
            item.Name = string.IsNullOrWhiteSpace(item.Name) ? "定时网页" : item.Name.Trim();
            item.Url = (item.Url ?? "").Trim();
            item.StartTime = NormalizeTime(item.StartTime);
            item.EndTime = NormalizeTime(item.EndTime);
            item.Repeat = item.Repeat is "once" or "weekly" or "daily" ? item.Repeat : "daily";
            item.Date = (item.Date ?? "").Trim();
            item.WeekStart = Math.Clamp(item.WeekStart, 0, 6);
            item.WeekEnd = Math.Clamp(item.WeekEnd, 0, 6);
        }
    }

    public static TimeSpan? ParseTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Trim().Split(':', '：');
        if (parts.Length != 2) return null;
        if (!int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m)) return null;
        if (h < 0 || h > 23 || m < 0 || m > 59) return null;
        return new TimeSpan(h, m, 0);
    }

    public static string NormalizeTime(string? text)
    {
        var t = ParseTime(text);
        return t.HasValue ? $"{t.Value.Hours:00}:{t.Value.Minutes:00}" : "08:00";
    }

    public static readonly string[] DefaultSubjects = { "语文", "数学", "英语", "德法", "地理", "政治", "生物" };

    public static readonly string[] WeekdayNames = { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };

    public static AppConfig CreateDefault()
    {
        var a = new Timetable { Id = "tt-a", Name = "课程表A（周一至周六）" };
        a.Entries.Add(new CourseEntry { Time = "08:00", Subject = "语文", AdvanceMinutes = 1 });
        a.Entries.Add(new CourseEntry { Time = "08:55", Subject = "数学", AdvanceMinutes = 1 });
        a.Entries.Add(new CourseEntry { Time = "09:50", Subject = "英语", AdvanceMinutes = 1 });
        a.Entries.Add(new CourseEntry { Time = "10:45", Subject = "德法", AdvanceMinutes = 1 });
        a.Entries.Add(new CourseEntry { Time = "11:40", Subject = "地理", AdvanceMinutes = 1 });
        a.Entries.Add(new CourseEntry { Time = "14:00", Subject = "政治", AdvanceMinutes = 1 });
        a.Entries.Add(new CourseEntry { Time = "14:55", Subject = "生物", AdvanceMinutes = 1 });

        var b = new Timetable { Id = "tt-b", Name = "课程表B（周日）" };
        b.Entries.Add(new CourseEntry { Time = "08:30", Subject = "数学", AdvanceMinutes = 1 });
        b.Entries.Add(new CourseEntry { Time = "09:40", Subject = "英语", AdvanceMinutes = 1 });
        b.Entries.Add(new CourseEntry { Time = "10:50", Subject = "语文", AdvanceMinutes = 1 });

        var cfg = new AppConfig
        {
            SchemaVersion = 1,
            Subjects = new List<string>(DefaultSubjects),
            DefaultAdvanceMinutes = 1,
            RingtonePath = "",
            Timetables = new List<Timetable> { a, b },
            WeekMapping = new List<string> { "tt-a", "tt-a", "tt-a", "tt-a", "tt-a", "tt-a", "tt-b" },
            SeewoLoginEnabled = true,
            StartWithWindows = true
        };
        cfg.Alarms.Add(new AlarmItem { Id = "alarm-1", Time = "07:20", Label = "到校提醒", Enabled = false, AdvanceMinutes = 0 });
        foreach (var s in DefaultSubjects)
            cfg.SeewoAccounts.Add(new SeewoAccount { Subject = s, Account = "" });
        return cfg;
    }
}
