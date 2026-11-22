using ClassManager.Core;

namespace ClassManager.App;

/// <summary>使用系统 SAPI 语音合成播报"上课时间到了"，缺失语音引擎时静默跳过。</summary>
public static class VoiceIntro
{
    public static void Speak(string text)
    {
        try
        {
            Task.Run(() =>
            {
                try
                {
                    var type = Type.GetTypeFromProgID("SAPI.SpVoice");
                    if (type == null) return;
                    dynamic voice = Activator.CreateInstance(type)!;
                    voice.Volume = 90;
                    voice.Rate = 0;
                    voice.Speak(text, 0);
                }
                catch (Exception ex)
                {
                    AppPaths.Log("voice", "语音播报失败（已忽略）：" + ex.Message);
                }
            });
        }
        catch { }
    }
}
