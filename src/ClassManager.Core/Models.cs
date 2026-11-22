using System.Text.Json.Serialization;

namespace ClassManager.Core;

/// <summary>
/// 课程表 JSON 的根节点（schemaVersion = 1），与设置界面字段一一对应。
/// </summary>
public sealed class AppConfig
{
    /// <summary>JSON 结构版本号，便于后续兼容升级。</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>科目列表（可在设置页增删改）。</summary>
    public List<string> Subjects { get; set; } = new();

    /// <summary>上课条目默认提前响铃分钟数。</summary>
    public int DefaultAdvanceMinutes { get; set; } = 1;

    /// <summary>自定义铃声（.mp3 / .wav）绝对路径，为空时使用内置提示音。</summary>
    public string RingtonePath { get; set; } = "";

    /// <summary>多张课程表。</summary>
    public List<Timetable> Timetables { get; set; } = new();

    /// <summary>周几 → 课程表 id 映射，固定 7 项，下标 0 表示周一。</summary>
    public List<string> WeekMapping { get; set; } = new();

    /// <summary>独立闹钟（非课程条目）。</summary>
    public List<AlarmItem> Alarms { get; set; } = new();

    /// <summary>各科老师的希沃账号（password 以 DPAPI 密文落盘）。</summary>
    public List<SeewoAccount> SeewoAccounts { get; set; } = new();

    /// <summary>闹钟触发后是否自动执行希沃登录。</summary>
    public bool SeewoLoginEnabled { get; set; } = true;

    /// <summary>是否启用上课/独立闹钟（托盘可随时开关，响铃与希沃均受此控制）。</summary>
    public bool AlarmEnabled { get; set; } = true;

    /// <summary>定时打开网页（Smart URL）。</summary>
    public List<SmartUrlItem> SmartUrls { get; set; } = new();

    /// <summary>是否开机自启动（写入 HKCU Run）。</summary>
    public bool StartWithWindows { get; set; } = true;

    public AppConfig Clone() => ConfigStore.Deserialize(ConfigStore.Serialize(this))!;
}

/// <summary>一张课程表，可被多个星期几复用。</summary>
public sealed class Timetable
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<CourseEntry> Entries { get; set; } = new();
}

/// <summary>上课条目（只有上课时间，没有下课时间）。</summary>
public sealed class CourseEntry
{
    /// <summary>上课时间，24 小时制 "HH:mm"。</summary>
    public string Time { get; set; } = "08:00";
    public string Subject { get; set; } = "";
    public int AdvanceMinutes { get; set; } = 1;
    public string Note { get; set; } = "";
}

/// <summary>独立闹钟。</summary>
public sealed class AlarmItem
{
    public string Id { get; set; } = "";
    public string Time { get; set; } = "07:30";
    public string Label { get; set; } = "闹钟";
    public bool Enabled { get; set; } = true;
    public int AdvanceMinutes { get; set; }
}

/// <summary>希沃账号（按科目）。</summary>
public sealed class SeewoAccount
{
    public string Subject { get; set; } = "";
    public string Account { get; set; } = "";

    /// <summary>内存中为明文；保存到磁盘时自动加密为 "dpapi:..."。</summary>
    public string Password { get; set; } = "";

    [JsonIgnore]
    public bool HasPassword => !string.IsNullOrEmpty(Password);
}

/// <summary>定时打开网页：到开始时间用系统默认浏览器打开，到结束时间关闭。</summary>
public sealed class SmartUrlItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";

    /// <summary>开始时间 "HH:mm"。</summary>
    public string StartTime { get; set; } = "08:00";

    /// <summary>结束时间 "HH:mm"（早于开始时间表示跨天）。</summary>
    public string EndTime { get; set; } = "08:45";

    /// <summary>重复方式：once / weekly / daily。</summary>
    public string Repeat { get; set; } = "daily";

    /// <summary>单次执行的日期 "yyyy-MM-dd"（Repeat=once 时有效）。</summary>
    public string Date { get; set; } = "";

    /// <summary>每周重复的起始星期（0=周一 … 6=周日）。</summary>
    public int WeekStart { get; set; } = 0;

    /// <summary>每周重复的结束星期（可小于起始，表示跨周，如周五→周一）。</summary>
    public int WeekEnd { get; set; } = 4;

    public bool Enabled { get; set; } = true;
}

/// <summary>调度器计算出的一个即将触发的提醒。</summary>
public sealed class TriggerItem
{
    public DateTime FireAt { get; set; }
    public string Subject { get; set; } = "";
    public string Title { get; set; } = "";
    public bool IsCourse { get; set; }
    public string SourceKey { get; set; } = "";

    /// <summary>连堂（同一老师连续两节课）：仍响铃，但不改动希沃登录状态。</summary>
    public bool IsContinuous { get; set; }
}

/// <summary>应用内部状态（不属于用户课程表 JSON）。</summary>
public sealed class AppState
{
    public bool GuideOpened { get; set; }
    public string LastFiredKey { get; set; } = "";
    public string LastTrayTipShown { get; set; } = "";
}
