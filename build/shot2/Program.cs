using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

internal static class Program
{
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private const uint LEFTDOWN = 0x0002;
    private const uint LEFTUP = 0x0004;

    private static void Main(string[] args)
    {
        SetProcessDPIAware();
        if (args.Length == 0) return;

        if (args[0] == "click" && args.Length >= 3)
        {
            int x = int.Parse(args[1]), y = int.Parse(args[2]);
            SetCursorPos(x, y);
            Thread.Sleep(120);
            mouse_event(LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(LEFTUP, 0, 0, 0, UIntPtr.Zero);
            Console.WriteLine($"click {x},{y}");
            return;
        }

        var path = args.Length > 1 ? args[1] : "shot.png";
        Rectangle rect = Screen.PrimaryScreen.Bounds;
        foreach (var p in Process.GetProcessesByName("EasiNote"))
        {
            try
            {
                p.Refresh();
                var h = p.MainWindowHandle;
                if (h == IntPtr.Zero) continue;
                if (!GetWindowRect(h, out var r)) continue;
                var w = r.Right - r.Left;
                var hh = r.Bottom - r.Top;
                if (w > 300 && hh > 200)
                {
                    rect = new Rectangle(r.Left, r.Top, w, hh);
                    Console.WriteLine($"window {h} rect {rect.Left},{rect.Top} {rect.Width}x{rect.Height}");
                    break;
                }
            }
            catch { }
        }
        using var bmp = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
            g.CopyFromScreen(rect.Left, rect.Top, 0, 0, rect.Size, CopyPixelOperation.SourceCopy);
        bmp.Save(path, ImageFormat.Png);
        Console.WriteLine("saved " + path);
    }
}
