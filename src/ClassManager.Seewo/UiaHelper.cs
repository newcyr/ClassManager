using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace ClassManager.Seewo;

/// <summary>UI Automation 辅助方法（全部带兜底，异常不外抛）。</summary>
internal static class UiaHelper
{
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
    [DllImport("user32.dll")] internal static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] internal static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int Left, Top, Right, Bottom; }

    internal static string GetTitle(IntPtr hWnd)
    {
        var len = GetWindowTextLength(hWnd);
        if (len <= 0) return "";
        var sb = new System.Text.StringBuilder(len + 1);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    internal static List<IntPtr> GetVisibleWindows(int pid)
    {
        var list = new List<IntPtr>();
        try
        {
            EnumWindows((h, _) =>
            {
                GetWindowThreadProcessId(h, out var windowPid);
                if (windowPid == (uint)pid && IsWindowVisible(h) && GetWindowTextLength(h) > 0)
                    list.Add(h);
                return true;
            }, IntPtr.Zero);
        }
        catch
        {
            // 忽略
        }
        return list;
    }

    internal static AutomationElement? ElementFromHandle(IntPtr hWnd)
    {
        try { return AutomationElement.FromHandle(hWnd); }
        catch { return null; }
    }

    internal static IEnumerable<AutomationElement> Descendants(AutomationElement root, ControlType? type = null)
    {
        try
        {
            var cond = type == null ? Condition.TrueCondition : new PropertyCondition(AutomationElement.ControlTypeProperty, type);
            return root.FindAll(TreeScope.Descendants, cond).Cast<AutomationElement>();
        }
        catch
        {
            return Array.Empty<AutomationElement>();
        }
    }

    internal static AutomationElement? FindNamed(AutomationElement root, params string[] names)
    {
        foreach (var el in Descendants(root))
        {
            try
            {
                var name = el.Current.Name ?? "";
                if (names.Any(n => name.Contains(n, StringComparison.OrdinalIgnoreCase)))
                    return el;
            }
            catch { }
        }
        return null;
    }

    internal static AutomationElement? FindByAutomationId(AutomationElement root, string automationId)
    {
        try
        {
            var cond = new PropertyCondition(AutomationElement.AutomationIdProperty, automationId);
            return root.FindFirst(TreeScope.Descendants, cond);
        }
        catch { return null; }
    }

    internal static bool TryInvoke(AutomationElement el)
    {
        try
        {
            if (el.TryGetCurrentPattern(InvokePattern.Pattern, out var p))
            {
                ((InvokePattern)p).Invoke();
                return true;
            }
        }
        catch { }

        try
        {
            if (el.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var sp))
            {
                ((SelectionItemPattern)sp).Select();
                return true;
            }
        }
        catch { }

        try
        {
            // 兜底：点击元素中心点
            var rect = el.Current.BoundingRectangle;
            if (rect.IsEmpty || rect.Width <= 0) return false;
            var x = (int)(rect.Left + rect.Width / 2);
            var y = (int)(rect.Top + rect.Height / 2);
            SetCursorPos(x, y);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            return true;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;

    /// <summary>在屏幕绝对坐标处单击（供图像识别出的关键图标使用）。</summary>
    internal static void ClickAt(int x, int y)
    {
        try
        {
            SetCursorPos(x, y);
            Thread.Sleep(80);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        }
        catch { }
    }

    /// <summary>窗口客户区域的相对比例坐标 → 屏幕绝对坐标。</summary>
    internal static Point? RelativePoint(IntPtr hwnd, double fx, double fy)
    {
        try
        {
            if (!GetWindowRect(hwnd, out var rect)) return null;
            var w = rect.Right - rect.Left;
            var h = rect.Bottom - rect.Top;
            if (w < 100 || h < 100) return null;
            return new Point(rect.Left + (int)Math.Round(w * fx), rect.Top + (int)Math.Round(h * fy));
        }
        catch { return null; }
    }

    internal static bool TrySetValue(AutomationElement el, string value)
    {
        try
        {
            el.SetFocus();
            Thread.Sleep(120);
            if (el.TryGetCurrentPattern(ValuePattern.Pattern, out var p))
            {
                ((ValuePattern)p).SetValue(value);
                return true;
            }
        }
        catch { }

        try
        {
            el.SetFocus();
            Thread.Sleep(120);
            SendKeys.SendWait("^a");
            SendKeys.SendWait(Escape(value));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>SendKeys 特殊字符转义。</summary>
    internal static string Escape(string text)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in text)
        {
            if ("+^%~(){}[]".IndexOf(c) >= 0) sb.Append('{').Append(c).Append('}');
            else sb.Append(c);
        }
        return sb.ToString();
    }

    internal static void SendKey(byte vk)
    {
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, 2, UIntPtr.Zero);
    }
}
