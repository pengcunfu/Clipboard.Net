using System.Windows;

namespace ClipboardApp.Dialogs;

/// <summary>下载更新时的轻量进度对话框。</summary>
public partial class UpdateProgressDialog : Window
{
    public UpdateProgressDialog()
    {
        InitializeComponent();
    }

    public void Report(int percent)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var clamped = Math.Clamp(percent, 0, 100);
            Progress.Value = clamped;
            StatusText.Text = string.Format(UiText.UpdateDownloading, clamped);
        });
    }
}
