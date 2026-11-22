using System.Windows.Threading;
using ClassManager.Core;

namespace ClassManager.App;

/// <summary>定时网页调度：到开始时间用浏览器打开，到结束时间关闭。</summary>
public sealed class SmartUrlService
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<string, IBrowserSession> _open = new();
    private readonly HashSet<string> _fired = new();
    private AppConfig _config = new();
    private string _day = "";

    public int OpenCount => _open.Count;

    public void Start(AppConfig config)
    {
        _config = config;
        _timer.Tick += (_, _) => Check();
        _timer.Start();
    }

    public void UpdateConfig(AppConfig config) => _config = config;

    public void Stop() => _timer.Stop();

    private void Check()
    {
        try
        {
            var now = DateTime.Now;
            var day = now.ToString("yyyyMMdd");
            if (day != _day)
            {
                _day = day;
                _fired.Clear();
            }

            foreach (var item in _config.SmartUrls.Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.Url)))
            {
                foreach (var date in new[] { now.Date.AddDays(-1), now.Date })
                {
                    var occurrence = SmartUrlEngine.GetOccurrence(item, date);
                    if (occurrence == null) continue;

                    var startKey = $"s|{date:yyyyMMdd}|{item.Id}";
                    var endKey = $"e|{date:yyyyMMdd}|{item.Id}";

                    if (now >= occurrence.Value.Start && now - occurrence.Value.Start <= TimeSpan.FromMinutes(2) &&
                        _fired.Add(startKey))
                    {
                        OpenSession(item, startKey);
                    }

                    if (now >= occurrence.Value.End && now - occurrence.Value.End <= TimeSpan.FromMinutes(2) &&
                        _fired.Add(endKey))
                    {
                        CloseSession(startKey);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppPaths.Log("smarturl", "调度检查异常：" + ex.Message);
        }
    }

    private void OpenSession(SmartUrlItem item, string key)
    {
        CloseSession(key);
        try
        {
            AppPaths.Log("smarturl", $"开始：{item.Name}（{item.Url}）");
            var session = DefaultBrowserSession.Open(item.Url);
            _open[key] = session;
        }
        catch (Exception ex)
        {
            AppPaths.Log("smarturl", "打开网页失败：" + ex.Message);
        }
    }

    private void CloseSession(string key)
    {
        if (!_open.TryGetValue(key, out var session)) return;
        _open.Remove(key);
        try { session.Close(); } catch { }
    }

    /// <summary>界面上的"立即测试"。</summary>
    public void TriggerNow(SmartUrlItem item)
    {
        OpenSession(item, "test|" + item.Id + "|" + Guid.NewGuid().ToString("N"));
    }

    public void CloseAll()
    {
        foreach (var key in _open.Keys.ToList()) CloseSession(key);
    }
}
