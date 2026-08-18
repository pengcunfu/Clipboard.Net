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
    }
}
