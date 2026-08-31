using System.Windows;
using ClipboardApp.Services;

namespace ClipboardApp;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppPaths.MigrateLegacyData();

        if (!string.IsNullOrEmpty(StartupHooks.UpdatedTo))
        {
            MessageBox.Show(
                string.Format(UiText.UpdatedTo, StartupHooks.UpdatedTo),
                UiText.Tip,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }
}
