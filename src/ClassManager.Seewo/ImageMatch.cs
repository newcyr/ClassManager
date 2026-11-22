using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using ClassManager.Core;

namespace ClassManager.Seewo;

/// <summary>模板匹配结果（坐标为截图像素，需加上截图窗口原点得到屏幕坐标）。</summary>
public sealed class TemplateMatch
{
    public bool Found { get; init; }
    public double Score { get; init; }
    public Rectangle Bounds { get; init; }
    public double Scale { get; init; }
    public Bitmap? Bitmap { get; init; }
    public int OffsetX { get; init; }
    public int OffsetY { get; init; }

    /// <summary>把模板内的相对比例点换算成屏幕坐标。</summary>
    public Point PointAt(double fx, double fy) =>
        new(OffsetX + Bounds.Left + (int)Math.Round(Bounds.Width * fx),
            OffsetY + Bounds.Top + (int)Math.Round(Bounds.Height * fy));
}

/// <summary>多尺度模板匹配：用真实界面上采集的"关键图标"定位，避免依赖绝对像素。</summary>
public static class ImageMatch
{
    private const int PW_RENDERFULLCONTENT = 0x2;

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    public static string TemplateDir => Path.Combine(AppPaths.BaseDir, "assets", "templates");

    private static readonly Dictionary<string, Bitmap> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheLock = new();

    /// <summary>各模板裁剪时的来源窗口宽度（用于按当前窗口宽度推算缩放比）。</summary>
    private static readonly Dictionary<string, int> SourceWidths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["login-panel.png"] = 2264,
        ["privacy-box.png"] = 2264,
        ["account-menu.png"] = 2588,
        ["logout-item.png"] = 2588,
        ["dialog-ok.png"] = 2588
    };

    /// <summary>抓取窗口内容（即使被遮挡也能抓到 CEF 渲染内容）。</summary>
    public static Bitmap? CaptureWindow(IntPtr hwnd, out int offsetX, out int offsetY)
    {
        offsetX = offsetY = 0;
        try
        {
            if (!GetWindowRect(hwnd, out var rect)) return null;
            var w = Math.Max(1, rect.Right - rect.Left);
            var h = Math.Max(1, rect.Bottom - rect.Top);
            if (w < 100 || h < 100) return null;

            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                var hdc = g.GetHdc();
                var ok = PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT);
                g.ReleaseHdc(hdc);
                if (!ok)
                {
                    g.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(w, h));
                }
            }
            offsetX = rect.Left;
            offsetY = rect.Top;
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>诊断：输出 NCC 中间量。</summary>
    public static string DebugNccVerbose(Bitmap screen, string templateName, int x, int y, int step = 15)
    {
        var template = LoadTemplate(templateName);
        if (template == null) return "no-template";
        var gs = GrayImage.From(screen);
        var gt = GrayImage.From(template);
        var samples = gt.Sample(step);
        return gs.NccVerbose(x, y, samples);
    }

    /// <summary>诊断：在指定位置计算与模板的相关系数。</summary>
    public static double DebugNcc(Bitmap screen, string templateName, int x, int y, int step = 15)
    {
        var template = LoadTemplate(templateName);
        if (template == null) return -1;
        var gs = GrayImage.From(screen);
        var gt = GrayImage.From(template);
        return gs.NccAt(x, y, gt.Sample(step));
    }

    public static Bitmap? LoadTemplate(string name)
    {
        lock (CacheLock)
        {
            if (Cache.TryGetValue(name, out var cached)) return cached;
            try
            {
                var path = Path.Combine(TemplateDir, name);
                if (!File.Exists(path)) return null;
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var bmp = new Bitmap(fs);
                Cache[name] = bmp;
                return bmp;
            }
            catch { return null; }
        }
    }

    /// <summary>在截图中查找模板；模板不存在时返回未命中。</summary>
    public static TemplateMatch Find(Bitmap screen, string templateName, double minScore = 0.72, int offsetX = 0, int offsetY = 0)
    {
        var template = LoadTemplate(templateName);
        if (template == null)
            return new TemplateMatch { Found = false, Bitmap = screen, OffsetX = offsetX, OffsetY = offsetY };

        var gray = GrayImage.From(screen);

        // 已知模板的采样来源宽度 → 按窗口宽度推算缩放比，只在 ±4% 内搜索（更稳、更快）
        var expectedScale = 1.0;
        var hasExpected = SourceWidths.TryGetValue(templateName, out var sourceWidth);
        if (hasExpected) expectedScale = gray.Width / (double)sourceWidth;

        var baseScale = Math.Clamp(110.0 / template.Width, 0.18, 0.75);
        if (baseScale * gray.Width < 300) baseScale = Math.Min(0.75, 300.0 / gray.Width);

        var templateGray = GrayImage.From(template);
        var coarse = gray.Resize(baseScale);
        var scales = hasExpected
            ? new[] { expectedScale * 0.96, expectedScale, expectedScale * 1.04 }
            : new[] { 0.72, 0.82, 0.92, 1.0, 1.09, 1.20, 1.33, 1.47, 1.63 };

        var candidates = new List<(double Score, int X, int Y, double Scale)>();
        foreach (var scale in scales)
        {
            var tw = (int)Math.Round(template.Width * scale * baseScale);
            var th = (int)Math.Round(template.Height * scale * baseScale);
            if (tw < 8 || th < 8 || tw >= coarse.Width || th >= coarse.Height) continue;

            var tpl = templateGray.Resize((double)tw / template.Width);
            foreach (var (score, x, y) in CoarseSearch(coarse, tpl, 5))
                candidates.Add((score, x, y, scale));
        }

        if (candidates.Count == 0)
            return new TemplateMatch { Found = false, Bitmap = screen, OffsetX = offsetX, OffsetY = offsetY };

        // 粗搜索结果只用于选候选，最终得分以原始分辨率细化结果为准
        var bestScore = double.MinValue;
        var bestBounds = Rectangle.Empty;
        var bestScaleUsed = 1.0;
        foreach (var c in candidates.OrderByDescending(c => c.Score).Take(24))
        {
            var fw = (int)Math.Round(template.Width * c.Scale);
            var fh = (int)Math.Round(template.Height * c.Scale);
            var cx = (int)Math.Round(c.X / baseScale);
            var cy = (int)Math.Round(c.Y / baseScale);
            var refine = Refine(gray, templateGray.Resize(c.Scale), cx, cy, 45);
            if (refine.Score > bestScore)
            {
                bestScore = refine.Score;
                bestScaleUsed = c.Scale;
                bestBounds = new Rectangle(Math.Max(0, refine.X), Math.Max(0, refine.Y), fw, fh);
            }
        }

        return new TemplateMatch
        {
            Found = bestScore >= minScore,
            Score = bestScore,
            Bounds = bestBounds,
            Scale = bestScaleUsed,
            Bitmap = screen,
            OffsetX = offsetX,
            OffsetY = offsetY
        };
    }

    /// <summary>粗搜索：返回前 N 个互不邻近的候选位置（供后续原分辨率细化）。</summary>
    private static List<(double Score, int X, int Y)> CoarseSearch(GrayImage screen, GrayImage tpl, int topN)
    {
        var step = Math.Max(3, tpl.Width / 40);
        var sampleStep = Math.Max(1, (int)Math.Sqrt((double)tpl.Width * tpl.Height / 400));
        var samples = tpl.Sample(sampleStep);
        var result = new List<(double, int, int)>();
        if (samples.Count < 24) return result;

        var all = new List<(double Score, int X, int Y)>();
        for (var y = 0; y <= screen.Height - tpl.Height; y += step)
        {
            for (var x = 0; x <= screen.Width - tpl.Width; x += step)
            {
                var s = screen.NccAt(x, y, samples);
                all.Add((s, x, y));
            }
        }

        var minDist = Math.Max(6, tpl.Width / 2);
        foreach (var c in all.OrderByDescending(v => v.Item1))
        {
            if (result.Count >= topN) break;
            if (result.Any(r => Math.Abs(r.Item2 - c.X) < minDist && Math.Abs(r.Item3 - c.Y) < minDist))
                continue;
            result.Add(c);
        }
        return result;
    }

    private static (double Score, int X, int Y) Refine(GrayImage screen, GrayImage tpl, int cx, int cy, int radius)
    {
        var samples = tpl.Sample(Math.Max(1, (int)Math.Sqrt((double)tpl.Width * tpl.Height / 800)));
        if (samples.Count < 24) return (double.MinValue, cx, cy);

        var best = double.MinValue;
        var bx = cx;
        var by = cy;
        for (var y = Math.Max(0, cy - radius); y <= Math.Min(screen.Height - tpl.Height, cy + radius); y++)
        {
            for (var x = Math.Max(0, cx - radius); x <= Math.Min(screen.Width - tpl.Width, cx + radius); x++)
            {
                var s = screen.NccAt(x, y, samples);
                if (s > best) { best = s; bx = x; by = y; }
            }
        }
        return (best, bx, by);
    }

    private readonly struct Sample
    {
        public readonly int Dx, Dy, V;
        public Sample(int dx, int dy, int v) { Dx = dx; Dy = dy; V = v; }
    }

    private sealed class GrayImage
    {
        public int Width { get; }
        public int Height { get; }
        private readonly byte[] _data;

        private GrayImage(int w, int h, byte[] data) { Width = w; Height = h; _data = data; }

        public static GrayImage From(Bitmap bmp)
        {
            var w = bmp.Width;
            var h = bmp.Height;
            var data = new byte[w * h];
            var rect = new Rectangle(0, 0, w, h);
            var bd = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var stride = bd.Stride;
                var buf = new byte[stride * h];
                Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
                for (var y = 0; y < h; y++)
                {
                    var row = y * stride;
                    for (var x = 0; x < w; x++)
                    {
                        var i = row + x * 4;
                        data[y * w + x] = (byte)((buf[i + 2] * 299 + buf[i + 1] * 587 + buf[i + 0] * 114) / 1000);
                    }
                }
            }
            finally { bmp.UnlockBits(bd); }
            return new GrayImage(w, h, data);
        }

        /// <summary>等比例缩放（双线性）。</summary>
        public GrayImage Resize(double scale)
        {
            var w = Math.Max(1, (int)Math.Round(Width * scale));
            var h = Math.Max(1, (int)Math.Round(Height * scale));
            if (w == Width && h == Height) return this;

            var data = new byte[w * h];
            for (var y = 0; y < h; y++)
            {
                var sy = y * Height / (double)h;
                var y0 = (int)sy;
                var y1 = Math.Min(Height - 1, y0 + 1);
                var fy = sy - y0;
                for (var x = 0; x < w; x++)
                {
                    var sx = x * Width / (double)w;
                    var x0 = (int)sx;
                    var x1 = Math.Min(Width - 1, x0 + 1);
                    var fx = sx - x0;
                    var v00 = _data[y0 * Width + x0];
                    var v10 = _data[y0 * Width + x1];
                    var v01 = _data[y1 * Width + x0];
                    var v11 = _data[y1 * Width + x1];
                    var top = v00 + (v10 - v00) * fx;
                    var bottom = v01 + (v11 - v01) * fx;
                    data[y * w + x] = (byte)Math.Clamp(top + (bottom - top) * fy, 0, 255);
                }
            }
            return new GrayImage(w, h, data);
        }

        public List<Sample> Sample(int step)
        {
            var list = new List<Sample>();
            for (var y = 0; y < Height; y += step)
                for (var x = 0; x < Width; x += step)
                    list.Add(new Sample(x, y, _data[y * Width + x]));
            return list;
        }

        public string NccVerbose(int ox, int oy, List<Sample> samples)
        {
            long sumA = 0, sumB = 0;
            foreach (var s in samples)
            {
                sumA += _data[(oy + s.Dy) * Width + ox + s.Dx];
                sumB += s.V;
            }
            var n = samples.Count;
            var meanA = sumA / (double)n;
            var meanB = sumB / (double)n;
            double num = 0, da = 0, db = 0;
            foreach (var s in samples)
            {
                var a = _data[(oy + s.Dy) * Width + ox + s.Dx] - meanA;
                var b = s.V - meanB;
                num += a * b; da += a * a; db += b * b;
            }
            return $"n={n} meanA={meanA:0.0} meanB={meanB:0.0} num={num:0} da={da:0} db={db:0} ncc={num / Math.Sqrt(da * db):0.000}";
        }

        /// <summary>采样点上的归一化互相关（NCC）。</summary>
        public double NccAt(int ox, int oy, List<Sample> samples)
        {
            long sumA = 0, sumB = 0;
            foreach (var s in samples)
            {
                sumA += _data[(oy + s.Dy) * Width + ox + s.Dx];
                sumB += s.V;
            }
            var n = samples.Count;
            var meanA = sumA / (double)n;
            var meanB = sumB / (double)n;

            double num = 0, da = 0, db = 0;
            foreach (var s in samples)
            {
                var a = _data[(oy + s.Dy) * Width + ox + s.Dx] - meanA;
                var b = s.V - meanB;
                num += a * b;
                da += a * a;
                db += b * b;
            }
            if (da <= 1e-6 || db <= 1e-6) return 0;
            return num / Math.Sqrt(da * db);
        }
    }
}
