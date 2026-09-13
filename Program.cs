using System.Windows;
using ClipboardApp.Services;

namespace ClipboardApp;

/// <summary>
/// 自定义入口：先分发 --updater 助手模式，再获取唯一实例锁并启动 WPF 应用。
/// </summary>
public static class Program
{
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    public static void Main(string[] args)
    {
        // 更新/回滚助手模式：作为独立进程运行（旧 exe 的第二个实例），
        // 不创建窗口、不抢单实例锁，避免与正在运行的主进程冲突。
        if (args.Length > 0 && string.Equals(args[0], "--updater", StringComparison.OrdinalIgnoreCase))
        {
            Environment.ExitCode = Updater.Run(args.Skip(1).ToArray());
            return;
        }

        StartupHooks.ParseArgs(args);

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

/// <summary>解析更新器重启携带的命令行参数，供 App.OnStartup 提示「已更新 / 已回滚」。</summary>
internal static class StartupHooks
{
    /// <summary>更新成功重启后携带的目标版本（--updated-to）。</summary>
    public static string? UpdatedTo { get; private set; }

    /// <summary>回滚后携带：回滚自哪个失败版本（--rollback-from）。</summary>
    public static string? RolledBackFrom { get; private set; }

    /// <summary>回滚后携带：回滚到了哪个版本（--rollback-to）。</summary>
    public static string? RolledBackTo { get; private set; }

    /// <summary>手动回滚重启后为 true（--manual-rollback），此时版本号未知，提示通用文案。</summary>
    public static bool ManualRollback { get; private set; }

    public static void ParseArgs(string[] args)
    {
        UpdatedTo = GetArg(args, "--updated-to");
        RolledBackFrom = GetArg(args, "--rollback-from");
        RolledBackTo = GetArg(args, "--rollback-to");
        ManualRollback = args.Contains("--manual-rollback", StringComparer.OrdinalIgnoreCase);
    }

    private static string? GetArg(string[] args, string key)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }
}
