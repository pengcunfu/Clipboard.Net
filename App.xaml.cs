using System.Windows;
using ClipboardApp.Services;

namespace ClipboardApp;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppPaths.MigrateLegacyData();

        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // 更新器看门狗靠 .update_ok 标记判定新版本是否正常启动：
        // 走到这里说明主窗口已成功创建，写入标记告知「本版本可正常运行」。
        if (!string.IsNullOrEmpty(StartupHooks.UpdatedTo))
            WriteUpdateOkMarker();

        if (!string.IsNullOrEmpty(StartupHooks.UpdatedTo))
        {
            MessageBox.Show(
                string.Format(UiText.UpdatedTo, StartupHooks.UpdatedTo),
                UiText.Tip,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        else if (!string.IsNullOrEmpty(StartupHooks.RolledBackTo))
        {
            MessageBox.Show(
                string.Format(UiText.UpdateRolledBack, StartupHooks.RolledBackFrom, StartupHooks.RolledBackTo),
                UiText.Tip,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        else if (StartupHooks.ManualRollback)
        {
            MessageBox.Show(UiText.RolledBack, UiText.Tip, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // 启动后后台静默检查更新：已最新 / 出错时不打扰，只有发现新版本才弹确认框
        _ = UpdateService.CheckAndApplyAsync(silent: true);
    }

    /// <summary>更新流程请求本进程干净退出（绕过主窗口「关闭隐藏到托盘」拦截）。</summary>
    public static void RequestExit()
    {
        if (Application.Current?.MainWindow is MainWindow w)
            w.SetReallyExit();
        Application.Current?.Shutdown();
    }

    /// <summary>写入更新器看门狗的就绪标记：{exe 目录}\.update_ok。</summary>
    private static void WriteUpdateOkMarker()
    {
        try
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, ".update_ok"), "ok");
        }
        catch { /* 标记写失败不阻断启动 */ }
    }
}
