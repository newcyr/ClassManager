namespace ClassManager.Core;

/// <summary>定时网页（Smart URL）的重复规则与起止时间计算。</summary>
public static class SmartUrlEngine
{
    /// <summary>该条目在指定日期是否生效。</summary>
    public static bool MatchesDay(SmartUrlItem item, DateTime date)
    {
        if (!item.Enabled) return false;
        switch ((item.Repeat ?? "daily").ToLowerInvariant())
        {
            case "once":
                return DateTime.TryParse(item.Date, out var d) && d.Date == date.Date;
            case "weekly":
                var idx = ScheduleEngine.DayIndex(date); // 0 = 周一
                var s = Math.Clamp(item.WeekStart, 0, 6);
                var e = Math.Clamp(item.WeekEnd, 0, 6);
                return s <= e ? idx >= s && idx <= e : idx >= s || idx <= e;
            case "daily":
            default:
                return true;
        }
    }

    /// <summary>取指定日期的一次起止时间；结束早于开始视为跨天。</summary>
    public static (DateTime Start, DateTime End)? GetOccurrence(SmartUrlItem item, DateTime date)
    {
        if (!MatchesDay(item, date)) return null;
        var startTime = ConfigStore.ParseTime(item.StartTime);
        var endTime = ConfigStore.ParseTime(item.EndTime);
        if (startTime == null) return null;

        var start = date.Date + startTime.Value;
        var endSpan = endTime ?? startTime.Value.Add(TimeSpan.FromMinutes(45));
        var end = date.Date + endSpan;
        if (end <= start) end = end.AddDays(1);
        return (start, end);
    }

    /// <summary>今天剩余 / 明天的最近一次开始时间（用于状态栏）。</summary>
    public static (SmartUrlItem Item, DateTime Start)? GetNextStart(AppConfig cfg, DateTime now)
    {
        for (var i = 0; i < 8; i++)
        {
            var day = now.Date.AddDays(i);
            foreach (var item in cfg.SmartUrls.Where(x => x.Enabled))
            {
                var occ = GetOccurrence(item, day);
                if (occ == null) continue;
                if (occ.Value.Start > now)
                    return (item, occ.Value.Start);
            }
        }
        return null;
    }

    /// <summary>中文描述重复规则。</summary>
    public static string DescribeRepeat(SmartUrlItem item)
    {
        var days = ConfigStore.WeekdayNames;
        return (item.Repeat ?? "daily").ToLowerInvariant() switch
        {
            "once" => $"单次 {item.Date}",
            "weekly" => item.WeekStart == item.WeekEnd
                ? days[Math.Clamp(item.WeekStart, 0, 6)]
                : $"{days[Math.Clamp(item.WeekStart, 0, 6)]} - {days[Math.Clamp(item.WeekEnd, 0, 6)]}",
            _ => "每天"
        };
    }
}
