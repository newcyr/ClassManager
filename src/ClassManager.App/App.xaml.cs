using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using ClassManager.Core;
using ClassManager.Seewo;
using WinForms = System.Windows.Forms;

namespace ClassManager.App;

public partial class App : Application
{
    private Mutex? _mutex;
    private WinForms.NotifyIcon? _tray;
    private MainWindow? _mainWindow;
    private AlarmScheduler? _scheduler;
    private readonly List<AlarmWindow> _overlays = new();
    private readonly List<ScreenCoverWindow> _covers = new();

    public static new App Current => (App)Application.Current;
    public AppConfig Config { get; private set; } = new();
    public AppState State { get; private set; } = new();
    public SmartUrlService SmartUrls { get; private set; } = null!;
    public static bool IsExiting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, @"Global\ClassManager.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            // 已有实例常驻托盘：通知它打开设置窗口，避免"双击没反应"
            InstanceSignal.TryActivate();
            Shutdown();
            return;
        }
        InstanceSignal.StartServer(ShowSettings);

        DispatcherUnhandledException += (_, args) =>
        {
            AppPaths.Log("app", "未处理异常：" + args.Exception);
            args.Handled = true;
        };

        Config = ConfigStore.Load();
        State = LoadState();
        AutoStart.Apply(Config.StartWithWindows);

        _scheduler = new AlarmScheduler();
        _scheduler.AlarmDue += OnAlarmDue;
        _scheduler.Start(Config);

        SmartUrls = new SmartUrlService();
        SmartUrls.Start(Config);

        SetupTray();

        // 首次启动自动打开使用说明页
        if (!State.GuideOpened)
        {
            OpenGuide();
            State.GuideOpened = true;
            SaveState();
        }

        // 开机自启带 --tray 时静默驻留托盘；用户双击启动则直接打开设置窗口
        var silent = e.Args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
        if (!silent || e.Args.Any(a => a.Equals("--settings", StringComparison.OrdinalIgnoreCase)))
            ShowSettings();
    }

    public void ShowSettings()
    {
        if (_mainWindow == null)
        {
            _mainWindow = new MainWindow();
            _mainWindow.Closed += (_, _) => _mainWindow = null;
        }
        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
        _mainWindow.Focus();
    }

    public void ReloadConfig(AppConfig config)
    {
        Config = config;
        _scheduler?.UpdateConfig(config);
        SmartUrls?.UpdateConfig(config);
        AutoStart.Apply(config.StartWithWindows);
        UpdateTrayState();
        _mainWindow?.RefreshStatus();
    }

    /// <summary>托盘开关：启用/关闭全部闹钟（响铃与希沃登录）。</summary>
    public void ToggleAlarmEnabled()
    {
        Config.AlarmEnabled = !Config.AlarmEnabled;
        ConfigStore.Save(Config);
        UpdateTrayState();
        _mainWindow?.RefreshStatus();
        AppPaths.Log("app", Config.AlarmEnabled ? "闹钟已启用。" : "闹钟已关闭。");
    }

    /// <summary>托盘开关：启用/关闭希沃自动登录。</summary>
    public void ToggleSeewoLogin()
    {
        Config.SeewoLoginEnabled = !Config.SeewoLoginEnabled;
        ConfigStore.Save(Config);
        UpdateTrayState();
        _mainWindow?.RefreshStatus();
        AppPaths.Log("app", Config.SeewoLoginEnabled ? "希沃登录已启用。" : "希沃登录已关闭。");
    }

    public void ShowTestAlarm(string subject)
    {
        OnAlarmDue(new TriggerItem
        {
            Subject = subject,
            Title = subject,
            IsCourse = true,
            FireAt = DateTime.Now
        });
    }

    private static void OpenGuide()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppPaths.GuideUrl)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppPaths.Log("app", "打开说明页失败：" + ex.Message);
        }
    }

    private void SetupTray()
    {
        _tray = new WinForms.NotifyIcon
        {
            Visible = true,
            Text = "班级管理小师（后台常驻）"
        };
        try
        {
            _tray.Icon = File.Exists(AppPaths.AppIcon)
                ? new System.Drawing.Icon(AppPaths.AppIcon)
                : System.Drawing.SystemIcons.Information;
        }
        catch
        {
            _tray.Icon = System.Drawing.SystemIcons.Information;
        }

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("打开设置", null, (_, _) => ShowSettings());
        menu.Items.Add("立即测试全屏提醒", null, (_, _) =>
            OnAlarmDue(new TriggerItem { Subject = "语文", IsCourse = true, Title = "语文", FireAt = DateTime.Now }));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        _miAlarm = (WinForms.ToolStripMenuItem)menu.Items.Add("关闭闹钟", null, (_, _) => ToggleAlarmEnabled());
        _miSeewo = (WinForms.ToolStripMenuItem)menu.Items.Add("关闭登录", null, (_, _) => ToggleSeewoLogin());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("关闭应用（退出全部）", null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowSettings();
        _tray.MouseUp += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left)
                menu.Show(WinForms.Cursor.Position);
        };
        UpdateTrayState();
    }

    private WinForms.ToolStripMenuItem? _miAlarm;
    private WinForms.ToolStripMenuItem? _miSeewo;

    private void UpdateTrayState()
    {
        if (_miAlarm != null)
            _miAlarm.Text = Config.AlarmEnabled ? "关闭闹钟" : "启用闹钟";
        if (_miSeewo != null)
            _miSeewo.Text = Config.SeewoLoginEnabled ? "关闭登录" : "启用登录";
        if (_tray != null)
            _tray.Text = $"班级管理小师（闹钟{(Config.AlarmEnabled ? "开" : "关")} · 登录{(Config.SeewoLoginEnabled ? "开" : "关")}）";
    }

    public void ExitApp()
    {
        IsExiting = true;
        CloseAlarmOverlays();
        _scheduler?.Stop();
        SmartUrls?.CloseAll();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
        SaveState();
        Shutdown();
    }

    private void OnAlarmDue(TriggerItem item)
    {
        try
        {
            if (!Config.AlarmEnabled)
            {
                AppPaths.Log("alarm", $"闹钟已关闭，跳过：{item.Subject}");
                return;
            }

            CloseAlarmOverlays();
            var win = new AlarmWindow(item);
            _overlays.Add(win);
            win.Show();

            // 其余显示器补一层纯色遮罩，避免副屏仍显示桌面
            var primary = WinForms.Screen.PrimaryScreen;
            foreach (var screen in WinForms.Screen.AllScreens)
            {
                if (primary != null && screen.DeviceName == primary.DeviceName) continue;
                var cover = new ScreenCoverWindow(screen);
                _covers.Add(cover);
                cover.Show();
            }

            var ring = new RingPlayer();
            ring.Play(Config.RingtonePath, TimeSpan.FromSeconds(15));
            _activeRing = ring;

            VoiceIntro.Speak("上课时间到了");

            // 连堂（同一老师连续两节）：照常响铃，但不动希沃登录状态
            if (item.IsContinuous)
            {
                AppPaths.Log("seewo", $"科目「{item.Subject}」为连堂，保持希沃当前登录状态，不执行任何操作。");
            }
            else if (Config.SeewoLoginEnabled && item.IsCourse)
            {
                var account = Config.SeewoAccounts.FirstOrDefault(a =>
                    string.Equals(a.Subject, item.Subject, StringComparison.Ordinal) && a.HasPassword);
                if (account != null)
                {
                    // 响铃同时开始登录（铃声与登录并行）
                    var service = new SeewoLoginService();
                    _ = service.LoginAsync(account.Account, account.Password);
                }
                else
                {
                    AppPaths.Log("seewo", $"科目「{item.Subject}」未配置账号密码，跳过自动登录。");
                }
            }
        }
        catch (Exception ex)
        {
            AppPaths.Log("app", "触发闹钟失败：" + ex.Message);
        }
    }

    private RingPlayer? _activeRing;

    /// <summary>关闭全部全屏遮罩并停止铃声。</summary>
    public void CloseAlarmOverlays()
    {
        foreach (var win in _overlays.ToList())
        {
            try
            {
                win.CloseFromHost();
            }
            catch { }
        }
        _overlays.Clear();

        foreach (var cover in _covers.ToList())
        {
            try
            {
                cover.CloseFromHost();
            }
            catch { }
        }
        _covers.Clear();
        _activeRing?.Stop();
        _activeRing = null;
    }

    private AppState LoadState()
    {
        try
        {
            if (File.Exists(AppPaths.StateFile))
                return JsonSerializer.Deserialize<AppState>(File.ReadAllText(AppPaths.StateFile)) ?? new AppState();
        }
        catch { }
        return new AppState();
    }

    private void SaveState()
    {
        try
        {
            File.WriteAllText(AppPaths.StateFile, JsonSerializer.Serialize(State, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
