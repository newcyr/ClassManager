using System.IO;
using ClassManager.Core;

namespace ClassManager.App;

/// <summary>内置提示音兜底：代码合成一段上课铃声（WAV），避免缺少音频文件时静音。</summary>
public static class ToneGenerator
{
    private const int SampleRate = 44100;

    public static string EnsureBuiltinWav()
    {
        var path = Path.Combine(AppPaths.DataDir, "ringtone-fallback.wav");
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length > 1024) return path;
            WriteWav(path);
        }
        catch (Exception ex)
        {
            AppPaths.Log("ring", "生成兜底铃声失败：" + ex.Message);
        }
        return path;
    }

    /// <summary>生成"叮—咚"三连音，约 6 秒，可循环播放。</summary>
    public static void WriteWav(string path)
    {
        const double seconds = 6.0;
        var samples = (int)(SampleRate * seconds);
        var buffer = new short[samples];

        var strikes = new[] { (time: 0.00, freq: 1046.5), (time: 1.05, freq: 783.99), (time: 2.30, freq: 1046.5) };
        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / SampleRate;
            double value = 0;
            foreach (var strike in strikes)
            {
                if (t < strike.time) continue;
                var dt = t - strike.time;
                if (dt > 3.0) continue;
                var env = Math.Exp(-1.9 * dt);
                // 基频 + 三个泛音，模拟钟声
                value += env * (0.60 * Math.Sin(2 * Math.PI * strike.freq * dt)
                                + 0.25 * Math.Sin(2 * Math.PI * strike.freq * 2.01 * dt)
                                + 0.12 * Math.Sin(2 * Math.PI * strike.freq * 3.02 * dt)
                                + 0.06 * Math.Sin(2 * Math.PI * strike.freq * 4.05 * dt));
            }
            value *= 0.55;
            if (value > 1) value = 1;
            if (value < -1) value = -1;
            buffer[i] = (short)(value * short.MaxValue);
        }

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);
        int dataSize = buffer.Length * 2;
        bw.Write(new[] { 'R', 'I', 'F', 'F' });
        bw.Write(36 + dataSize);
        bw.Write(new[] { 'W', 'A', 'V', 'E' });
        bw.Write(new[] { 'f', 'm', 't', ' ' });
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)1);
        bw.Write(SampleRate);
        bw.Write(SampleRate * 2);
        bw.Write((short)2);
        bw.Write((short)16);
        bw.Write(new[] { 'd', 'a', 't', 'a' });
        bw.Write(dataSize);
        foreach (var s in buffer) bw.Write(s);
    }
}
