using System.Threading;

namespace AgentSignalBar.Windows;

static class Program
{
    // 命名 Mutex 单实例锁：保证同一 Windows 会话内只有一个托盘在运行，
    // 杜绝「登录自启 + 手动再开 / Debug+Release 双 exe」导致的多实例叠跑。
    private const string SingletonMutexName = @"Local\AgentSignalBarSingleton";

    [STAThread]
    static void Main()
    {
        using var singleton = new Mutex(true, SingletonMutexName, out bool createdNew);
        if (!createdNew)
        {
            // 已有实例在运行：直接退出，不再产生第二个托盘。
            return;
        }

        try
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new TrayApplicationContext());
        }
        finally
        {
            singleton.ReleaseMutex();
        }
    }
}
