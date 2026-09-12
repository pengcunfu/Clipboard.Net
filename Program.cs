using System.Windows;
using Velopack;

namespace ClipboardApp;

/// <summary>
/// 自定义入口：先处理 Velopack 钩子（首次安装 / 已更新 / 启动时应用待安装更新），
/// 再启动 WPF 应用。
/// </summary>
public static class Program
{
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    public static void Main(string[] args)
    {
        if (!TryAcquireSingleInstance())
            return;

        VelopackApp.Build()
            .SetArgs(args)
            .OnFirstRun(_ => StartupHooks.FirstRun = true)
            .OnRestarted(v => StartupHooks.UpdatedTo = v.ToNormalizedString())
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();

        _singleInstanceMutex?.Dispose();
    }

    /// <summary>唯一实例锁：确保程序同一时间只运行一个实例。</summary>
    private static bool TryAcquireSingleInstance()
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, @"Local\ClipboardApp_Fireneb", out bool createdNew);
        if (createdNew)
            return true;

        MessageBox.Show(UiText.AlreadyRunning, UiText.Tip, MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }
}

/// <summary>从 Program.Main 向 App.OnStartup 传递 Velopack 钩子结果（首次安装 / 已更新提示用）。</summary>
internal static class StartupHooks
{
    public static bool FirstRun { get; set; }
    public static string? UpdatedTo { get; set; }
}
