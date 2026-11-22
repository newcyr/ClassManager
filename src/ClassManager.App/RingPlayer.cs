using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using ClassManager.Core;

namespace ClassManager.App;

/// <summary>铃声播放：优先用户自定义 .mp3/.wav，其次内置音频，缺失时合成提示音兜底。</summary>
public sealed class RingPlayer : IDisposable
{
    private readonly MediaPlayer _player = new();
    private DispatcherTimer? _stopTimer;

    public void Play(string? customPath, TimeSpan duration)
    {
        try
        {
            var path = ResolveAudioPath(customPath);
            if (path == null) return;

            _player.Open(new Uri(path, UriKind.Absolute));
            _player.Volume = 1.0;
            _player.MediaEnded += (_, _) =>
            {
                try
                {
                    _player.Position = TimeSpan.Zero;
                    _player.Play();
                }
                catch { }
            };
            _player.MediaFailed += (_, args) =>
            {
                AppPaths.Log("ring", "播放失败：" + args.ErrorException?.Message);
                PlayFallback();
            };
            _player.Play();

            _stopTimer = new DispatcherTimer { Interval = duration };
            _stopTimer.Tick += (_, _) => Stop();
            _stopTimer.Start();
        }
        catch (Exception ex)
        {
            AppPaths.Log("ring", "播放异常：" + ex.Message);
        }
    }

    private void PlayFallback()
    {
        try
        {
            var wav = ToneGenerator.EnsureBuiltinWav();
            _player.Open(new Uri(wav, UriKind.Absolute));
            _player.Play();
        }
        catch { }
    }

    private static string? ResolveAudioPath(string? customPath)
    {
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
            return Path.GetFullPath(customPath);
        if (File.Exists(AppPaths.BuiltinRingtone))
            return AppPaths.BuiltinRingtone;
        return ToneGenerator.EnsureBuiltinWav();
    }

    public void Stop()
    {
        try
        {
            _stopTimer?.Stop();
            _stopTimer = null;
            _player.Stop();
        }
        catch { }
    }

    public void Dispose()
    {
        Stop();
        _player.Close();
    }
}
