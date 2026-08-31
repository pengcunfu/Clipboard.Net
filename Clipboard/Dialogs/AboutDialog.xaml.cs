using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using ClipboardApp.Services;

namespace ClipboardApp.Dialogs;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
        LoadContent();
    }

    private void LoadContent()
    {
        AppNameLabel.Text = UiText.AppName;
        VersionLabel.Text = UiText.Version + ": " + VersionInfo.Version;

        // Copyright
        var copyrightRun = new Run(UiText.Copyright);
        CopyrightPara.Inlines.Add(copyrightRun);

        // Website with clickable link
        var websiteLabel = new Run("官网: ");
        WebsitePara.Inlines.Add(websiteLabel);

        var link = new Hyperlink(new Run("https://fireneb.com"))
        {
            NavigateUri = new Uri("https://fireneb.com")
        };
        link.RequestNavigate += (_, e) =>
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        };
        WebsitePara.Inlines.Add(link);

        // About body
        var bodyRun = new Run(UiText.AboutBody);
        AboutBodyPara.Inlines.Add(bodyRun);
    }

    private void OkButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private async void CheckUpdate_OnClick(object sender, RoutedEventArgs e)
    {
        var updater = new UpdateService();
        if (!updater.IsUpdateCapable)
        {
            MessageBox.Show(UiText.UpdateNotInstalled, UiText.Tip, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        UpdateProgressDialog? progressDlg = null;
        try
        {
            var info = await updater.CheckForUpdatesAsync();
            if (info is null)
            {
                MessageBox.Show(UiText.UpdateUpToDate, UiText.Tip, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var current = updater.CurrentVersion?.ToNormalizedString() ?? VersionInfo.Version;
            var ask = MessageBox.Show(
                string.Format(UiText.UpdateAvailable, info.TargetFullRelease.Version.ToNormalizedString(), current),
                UiText.Tip,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (ask != MessageBoxResult.Yes)
                return;

            progressDlg = new UpdateProgressDialog { Owner = this };
            var downloadTask = updater.DownloadUpdatesAsync(info, new Progress<int>(p => progressDlg.Report(p)));
            progressDlg.Show();
            await downloadTask;
            progressDlg.Close();

            updater.RestartAndApply(info);
        }
        catch (Exception ex)
        {
            progressDlg?.Close();
            MessageBox.Show(UiText.UpdateCheckFailed + ex.Message, UiText.UpdateError, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
