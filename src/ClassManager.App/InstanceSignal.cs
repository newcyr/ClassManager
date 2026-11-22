using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace ClassManager.App;

/// <summary>已存在实例的唤醒通道：再次双击程序时打开设置窗口，而不是静默退出。</summary>
internal static class InstanceSignal
{
    private const string PipeName = "ClassManager.Activate";

    /// <summary>在首个实例中监听唤醒请求。</summary>
    public static void StartServer(Action onActivate)
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None);
                    server.WaitForConnection();
                    using var reader = new StreamReader(server);
                    if (reader.ReadLine() == "show")
                    {
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(onActivate);
                    }
                }
                catch
                {
                    // 无人连接或管道被占用时短暂等待后重试
                    Thread.Sleep(300);
                }
            }
        })
        {
            IsBackground = true,
            Name = "ClassManager.Activate"
        };
        thread.Start();
    }

    /// <summary>通知已存在的实例显示设置窗口。</summary>
    public static bool TryActivate(int timeoutMs = 1500)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeoutMs);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine("show");
            return true;
        }
        catch
        {
            return false;
        }
    }
}
