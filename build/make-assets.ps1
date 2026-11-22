# 生成 assets/icon.ico 与 assets/ringtone.wav（构建期一次性资源生成脚本）
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root "assets"
New-Item -ItemType Directory -Force -Path $assets | Out-Null

# ---------- 1. 生成 ICO（256x256 PNG 载荷） ----------
$size = 256
$bmp = New-Object System.Drawing.Bitmap($size, $size)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)

$rect = [System.Drawing.Rectangle]::new(8, 8, ($size - 16), ($size - 16))
$brush = [System.Drawing.Drawing2D.LinearGradientBrush]::new($rect, [System.Drawing.Color]::FromArgb(255, 47, 111, 237), [System.Drawing.Color]::FromArgb(255, 24, 62, 156), 45)
$path = [System.Drawing.Drawing2D.GraphicsPath]::new()
$radius = 46
$path.AddArc(8, 8, $radius, $radius, 180, 90)
$path.AddArc(($size - 8 - $radius), 8, $radius, $radius, 270, 90)
$path.AddArc(($size - 8 - $radius), ($size - 8 - $radius), $radius, $radius, 0, 90)
$path.AddArc(8, ($size - 8 - $radius), $radius, $radius, 90, 90)
$path.CloseFigure()
$g.FillPath($brush, $path)
$g.DrawPath([System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(90, 255, 255, 255), 5), $path)

$font = [System.Drawing.Font]::new("Microsoft YaHei UI", 120, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$fmt = [System.Drawing.StringFormat]::new()
$fmt.Alignment = [System.Drawing.StringAlignment]::Center
$fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
$g.DrawString([string][char]0x73ED, $font, [System.Drawing.Brushes]::White, [System.Drawing.RectangleF]::new(0, 4, $size, $size), $fmt)

# 顶部小圆点（铃声提示）
$g.FillEllipse([System.Drawing.Brushes]::Gold, 176, 44, 34, 34)
$g.Dispose()

$pngStream = New-Object System.IO.MemoryStream
$bmp.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
$pngBytes = $pngStream.ToArray()
$bmp.Dispose()

$icoPath = Join-Path $assets "icon.ico"
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)      # reserved
$bw.Write([UInt16]1)      # type icon
$bw.Write([UInt16]1)      # count
$bw.Write([Byte]0)        # width 0 = 256
$bw.Write([Byte]0)        # height 0 = 256
$bw.Write([Byte]0)        # colors
$bw.Write([Byte]0)        # reserved
$bw.Write([UInt16]1)      # planes
$bw.Write([UInt16]32)     # bpp
$bw.Write([UInt32]$pngBytes.Length)
$bw.Write([UInt32]22)     # offset
$bw.Write($pngBytes)
$bw.Close()
Write-Output "icon -> $icoPath"

# ---------- 2. 生成铃声 ringtone.wav（约 6 秒三连钟声，可循环） ----------
$gen = @'
using System;
using System.IO;
public static class ToneGen
{
    public static void Write(string path)
    {
        const int rate = 44100;
        const double seconds = 6.0;
        int n = (int)(rate * seconds);
        short[] buf = new short[n];
        double[][] strikes = { new double[]{0.00, 1046.5}, new double[]{1.05, 783.99}, new double[]{2.30, 1046.5} };
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / rate;
            double v = 0;
            foreach (var s in strikes)
            {
                if (t < s[0]) continue;
                double dt = t - s[0];
                if (dt > 3.0) continue;
                double env = Math.Exp(-1.9 * dt);
                v += env * (0.60 * Math.Sin(2 * Math.PI * s[1] * dt)
                          + 0.25 * Math.Sin(2 * Math.PI * s[1] * 2.01 * dt)
                          + 0.12 * Math.Sin(2 * Math.PI * s[1] * 3.02 * dt)
                          + 0.06 * Math.Sin(2 * Math.PI * s[1] * 4.05 * dt));
            }
            v *= 0.55;
            if (v > 1) v = 1;
            if (v < -1) v = -1;
            buf[i] = (short)(v * short.MaxValue);
        }
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var bw = new BinaryWriter(fs))
        {
            int data = buf.Length * 2;
            bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + data);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            bw.Write(16);
            bw.Write((short)1);
            bw.Write((short)1);
            bw.Write(rate);
            bw.Write(rate * 2);
            bw.Write((short)2);
            bw.Write((short)16);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            bw.Write(data);
            foreach (var s in buf) bw.Write(s);
        }
    }
}
'@
Add-Type -TypeDefinition $gen -Language CSharp
$wavPath = Join-Path $assets "ringtone.wav"
[ToneGen]::Write($wavPath)
Write-Output "wav -> $wavPath"
