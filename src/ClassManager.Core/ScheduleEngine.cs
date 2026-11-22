namespace ClassManager.Core;

/// <summary>按"周几 → 课程表"映射计算某一天的提醒时间点。</summary>
public static class ScheduleEngine
{
    public static int DayIndex(DateTime date) => ((int)date.DayOfWeek + 6) % 7; // 0 = 周一

    public static Timetable? GetTimetableForDate(AppConfig cfg, DateTime date)
    {
        var idx = DayIndex(date);
        if (cfg.WeekMapping.Count <= idx) return null;
        var id = cfg.WeekMapping[idx];
        return cfg.Timetables.FirstOrDefault(t => t.Id == id);
    }

    /// <summary>计算某天全部触发点（已扣除提前分钟数）。</summary>
    public static List<TriggerItem> GetTriggersForDate(AppConfig cfg, DateTime date)
    {
        var result = new List<TriggerItem>();
        var tt = GetTimetableForDate(cfg, date);
        if (tt != null)
        {
            for (var i = 0; i < tt.Entries.Count; i++)
            {
                var entry = tt.Entries[i];
                var time = ConfigStore.ParseTime(entry.Time);
                if (time == null) continue;
                var at = date.Date + time.Value - TimeSpan.FromMinutes(entry.AdvanceMinutes);

                // 连堂：下一节仍是同一科目的课且间隔不超过 60 分钟 → 同一老师连续授课
                var continuous = false;
                if (i + 1 < tt.Entries.Count)
                {
                    var next = tt.Entries[i + 1];
                    var nextTime = ConfigStore.ParseTime(next.Time);
                    if (nextTime != null &&
                        !string.IsNullOrWhiteSpace(entry.Subject) &&
                        string.Equals(entry.Subject.Trim(), (next.Subject ?? "").Trim(), StringComparison.Ordinal))
                    {
                        var gap = nextTime.Value - time.Value;
                        continuous = gap > TimeSpan.Zero && gap <= TimeSpan.FromMinutes(60);
                    }
                }

                result.Add(new TriggerItem
                {
                    FireAt = at,
                    Subject = entry.Subject,
                    Title = entry.Subject,
                    IsCourse = true,
                    IsContinuous = continuous,
                    SourceKey = $"{date:yyyyMMdd}|{tt.Id}|{entry.Time}|{entry.Subject}"
                });
            }
        }

        foreach (var alarm in cfg.Alarms.Where(a => a.Enabled))
        {
            var time = ConfigStore.ParseTime(alarm.Time);
            if (time == null) continue;
            var at = date.Date + time.Value - TimeSpan.FromMinutes(alarm.AdvanceMinutes);
            result.Add(new TriggerItem
            {
                FireAt = at,
                Subject = alarm.Label,
                Title = alarm.Label,
                IsCourse = false,
                SourceKey = $"{date:yyyyMMdd}|alarm|{alarm.Id}"
            });
        }

        return result.OrderBy(t => t.FireAt).ToList();
    }

    /// <summary>今天剩余 / 明天起的最近一条提醒。</summary>
    public static TriggerItem? GetNextTrigger(AppConfig cfg, DateTime now)
    {
        for (int i = 0; i < 8; i++)
        {
            var day = now.Date.AddDays(i);
            var item = GetTriggersForDate(cfg, day).FirstOrDefault(t => t.FireAt > now);
            if (item != null) return item;
        }
        return null;
    }
}
