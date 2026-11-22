using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ClassManager.Core;
using WinForms = System.Windows.Forms;

namespace ClassManager.App;

/// <summary>
/// 全屏提醒遮罩（主屏）：最大化铺满屏幕、置顶、不可最小化、屏蔽 Alt+F4。
/// 点击后 3 秒倒计时关闭；保留 Ctrl+Alt+Shift+Q 紧急关闭热键。
/// </summary>
public partial class AlarmWindow : Window
{
    private bool _allowClose;
    private bool _counting;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int HotkeyId = 0xC1A5;
    private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004;
    private const uint VK_Q = 0x51;

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_SHOWWINDOW = 0x0040;

    public AlarmWindow(TriggerItem item)
    {
        InitializeComponent();
        SubjectRun.Text = string.IsNullOrWhiteSpace(item.Subject) ? item.Title : item.Subject;

        // 覆盖整个虚拟屏幕（DIP 单位，含任务栏区域，天然适配高 DPI 与多显示器）
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) =>
        {
            Topmost = true;
            Activate();
            Focus();
            CoverMonitor();

            // 铃声 15 秒结束后自动关闭提醒
            _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _autoCloseTimer.Tick += (_, _) =>
            {
                _autoCloseTimer?.Stop();
                App.Current.CloseAlarmOverlays();
            };
            _autoCloseTimer.Start();
        };
    }

    private DispatcherTimer? _autoCloseTimer;

    /// <summary>用物理像素铺满所在显示器（含任务栏区域），确保真正全屏霸占。</summary>
    private void CoverMonitor()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var monitor = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(monitor, ref info)) return;

            var width = info.rcMonitor.right - info.rcMonitor.left;
            var height = info.rcMonitor.bottom - info.rcMonitor.top;
            SetWindowPos(hwnd, HWND_TOPMOST, info.rcMonitor.left, info.rcMonitor.top, width, height, SWP_SHOWWINDOW);
            AppPaths.Log("alarm", $"遮罩覆盖显示器：{info.rcMonitor.left},{info.rcMonitor.top} {width}x{height}");
        }
        catch (Exception ex)
        {
            AppPaths.Log("alarm", "全屏覆盖失败：" + ex.Message);
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            RegisterHotKey(handle, HotkeyId, MOD_CONTROL | MOD_ALT | MOD_SHIFT, VK_Q);
            HwndSource.FromHwnd(handle)?.AddHook(HotkeyHook);
        }
        catch (Exception ex)
        {
            AppPaths.Log("alarm", "注册紧急热键失败：" + ex.Message);
        }
    }

    private IntPtr HotkeyHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            App.Current.CloseAlarmOverlays();
        }
        return IntPtr.Zero;
    }

    private void RootGrid_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_counting) return;
        _counting = true;
        HintText.Text = "正在关闭提醒…";
        var remaining = 3;
        CountdownText.Text = $"{remaining} 秒后关闭";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            remaining--;
            if (remaining <= 0)
            {
                timer.Stop();
                App.Current.CloseAlarmOverlays();
                return;
            }
            CountdownText.Text = $"{remaining} 秒后关闭";
        };
        timer.Start();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose && !App.IsExiting)
            e.Cancel = true; // 屏蔽 Alt+F4 / 系统关闭
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _autoCloseTimer?.Stop();
        _autoCloseTimer = null;
        try
        {
            UnregisterHotKey(new WindowInteropHelper(this).Handle, HotkeyId);
        }
        catch { }
        base.OnClosed(e);
    }

    /// <summary>由宿主 / 热键关闭（绕过 Alt+F4 屏蔽）。</summary>
    public void CloseFromHost()
    {
        _allowClose = true;
        Close();
    }
}

/// <summary>非主屏的纯色遮罩（无文案），保证多显示器时整体全屏。</summary>
public sealed class ScreenCoverWindow : Window
{
    private const uint SWP_SHOWWINDOW = 0x0040;
    private bool _allowClose;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    public ScreenCoverWindow(WinForms.Screen screen)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = System.Windows.Media.Brushes.Black;
        Left = 0;
        Top = 0;
        Width = 200;
        Height = 200;
        Loaded += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var b = screen.Bounds;
            SetWindowPos(hwnd, new IntPtr(-1), b.X, b.Y, b.Width, b.Height, SWP_SHOWWINDOW);
        };
        ShowActivated = false;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose && !App.IsExiting) e.Cancel = true;
        base.OnClosing(e);
    }

    public void CloseFromHost()
    {
        _allowClose = true;
        Close();
    }
}
