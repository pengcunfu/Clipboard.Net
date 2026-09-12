using System.Windows;

namespace ClipboardApp;

/// <summary>
/// 自定义入口：先获取唯一实例锁，再启动 WPF 应用。
/// </summary>
public static class Program
{
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    public static void Main(string[] args)
    {
        if (!TryAcquireSingleInstance())
            return;

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
