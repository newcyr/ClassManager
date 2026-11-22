using System.Windows.Threading;
using ClassManager.Core;

namespace ClassManager.App;

/// <summary>每秒检查一次今日提醒，到点触发（提前分钟数已在调度计算中扣除）。</summary>
public sealed class AlarmScheduler
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly HashSet<string> _fired = new();
    private AppConfig _config = new();
    private string _dayKey = "";

    public event Action<TriggerItem>? AlarmDue;

    public AlarmScheduler()
    {
        _timer.Tick += (_, _) => Check();
    }

    public void Start(AppConfig config)
    {
        _config = config;
        _timer.Start();
        Check();
    }

    public void UpdateConfig(AppConfig config)
    {
        _config = config;
    }

    public void Stop() => _timer.Stop();

    /// <summary>手动触发（用于"立即测试"）：真实触发一次，并记录已触发。</summary>
    public void FireNow(TriggerItem item) => AlarmDue?.Invoke(item);

    private void Check()
    {
        try
        {
            if (!_config.AlarmEnabled) return;

            var now = DateTime.Now;
            var dayKey = now.ToString("yyyyMMdd");
            if (dayKey != _dayKey)
            {
                _dayKey = dayKey;
                _fired.Clear();
            }

            foreach (var item in ScheduleEngine.GetTriggersForDate(_config, now.Date))
            {
                if (_fired.Contains(item.SourceKey)) continue;
                if (now < item.FireAt) continue;
                if (now - item.FireAt > TimeSpan.FromMinutes(2)) continue; // 过期太久（如睡眠唤醒）不补触发

                _fired.Add(item.SourceKey);
                AppPaths.Log("alarm", $"触发：{item.FireAt:yyyy-MM-dd HH:mm} {item.Subject}");
                AlarmDue?.Invoke(item);
            }
        }
        catch (Exception ex)
        {
            AppPaths.Log("alarm", "调度检查异常：" + ex.Message);
        }
    }
}
