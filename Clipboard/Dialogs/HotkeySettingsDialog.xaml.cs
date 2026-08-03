using System.Windows;
using System.Windows.Input;
using ClipboardApp.Services;
using WpfKey = System.Windows.Input.Key;

namespace ClipboardApp.Dialogs;

public partial class HotkeySettingsDialog : Window
{
    public List<string> Modifiers { get; private set; }
    public string HotkeyKey { get; private set; }

    public HotkeySettingsDialog(IEnumerable<string> currentModifiers, string currentKey)
    {
        InitializeComponent();
        Modifiers = currentModifiers.ToList();
        HotkeyKey = currentKey;
        UpdatePreview();
        Loaded += (_, _) => HotkeyBox.Focus();
    }

    private void HotkeyBox_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;

        var key = e.Key == WpfKey.System ? e.SystemKey : e.Key;
        if (key is WpfKey.LeftCtrl or WpfKey.RightCtrl or WpfKey.LeftAlt or WpfKey.RightAlt
            or WpfKey.LeftShift or WpfKey.RightShift or WpfKey.LWin or WpfKey.RWin
            or WpfKey.None or WpfKey.Tab)
            return;

        var modifiers = new List<string>();
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            modifiers.Add("ctrl");
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
            modifiers.Add("alt");
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            modifiers.Add("shift");
        if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0)
            modifiers.Add("win");

        var keyName = key switch
        {
            WpfKey.Space => "space",
            >= WpfKey.A and <= WpfKey.Z => key.ToString().ToLowerInvariant(),
            >= WpfKey.D0 and <= WpfKey.D9 => ((char)('0' + (key - WpfKey.D0))).ToString(),
            >= WpfKey.NumPad0 and <= WpfKey.NumPad9 => ((char)('0' + (key - WpfKey.NumPad0))).ToString(),
            >= WpfKey.F1 and <= WpfKey.F12 => $"f{key - WpfKey.F1 + 1}",
            _ => string.Empty,
        };

        if (modifiers.Count == 0 || string.IsNullOrEmpty(keyName))
            return;

        Modifiers = modifiers;
        HotkeyKey = keyName;
        UpdatePreview();
    }

    private void ClearButton_OnClick(object sender, RoutedEventArgs e)
    {
        Modifiers = [];
        HotkeyKey = string.Empty;
        UpdatePreview();
        HotkeyBox.Focus();
    }

    private void OkButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (Modifiers.Count == 0 || string.IsNullOrWhiteSpace(HotkeyKey))
        {
            System.Windows.MessageBox.Show(this, "请设置一个快捷键！", "警告",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void UpdatePreview()
    {
        if (Modifiers.Count == 0 || string.IsNullOrWhiteSpace(HotkeyKey))
        {
            HotkeyBox.Text = string.Empty;
            PreviewLabel.Text = "未设置快捷键";
            PreviewLabel.Foreground = System.Windows.Media.Brushes.Gray;
            return;
        }

        var text = HotkeyService.FormatHotkey(Modifiers, HotkeyKey);
        HotkeyBox.Text = text;
        PreviewLabel.Text = $"当前快捷键：{text}";
        PreviewLabel.Foreground = System.Windows.Media.Brushes.Green;
        PreviewLabel.FontWeight = FontWeights.Bold;
    }
}
