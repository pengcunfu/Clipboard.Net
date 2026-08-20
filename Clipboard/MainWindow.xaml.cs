using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClipboardApp.Dialogs;
using ClipboardApp.Models;
using ClipboardApp.Services;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace ClipboardApp;

public partial class MainWindow : Window
{
    private readonly ConfigService _config = new();
    private readonly HistoryService _history = new();
    private readonly HotkeyService _hotkey;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _clipboardTimer;

    private Forms.NotifyIcon? _trayIcon;
    private Forms.ToolStripMenuItem? _trayToggleItem;
    private bool _monitoringEnabled = true;
    private bool _reallyExit;
    private bool _suppressClipboard;
    private string? _lastClipboardSignature;
    private DateTime _lastImageCapture = DateTime.MinValue;
    private ClipboardEntry? _currentImageEntry;
    private ClipboardEntry? _currentTextEntry;
    private string? _currentDetectedLanguage;
    private bool _showHighlighted = true;

    public MainWindow()
    {
        InitializeComponent();
        _hotkey = new HotkeyService(this);

        AppPaths.MigrateLegacyData();
        TrySetWindowIcon();

        HistoryList.ItemsSource = _history.Entries;
        _history.Load();
        UpdateSearchPlaceholder();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _saveTimer.Tick += (_, _) => _history.Save();
        _saveTimer.Start();

        _clipboardTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _clipboardTimer.Tick += (_, _) => PollClipboard();
        _clipboardTimer.Start();

        SetupTray();
        AutostartMenuItem.IsChecked = AutostartService.IsEnabled();

        Loaded += MainWindow_OnLoaded;
        Closed += MainWindow_OnClosed;

        InputBindings.Add(new KeyBinding(
            new RelayCommand(QuitApp),
            new KeyGesture(Key.Q, ModifierKeys.Control)));
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        _hotkey.HotkeyPressed += ShowMainWindow;
        var modifiers = _config.Config.HotkeyModifiers;
        var key = _config.Config.HotkeyKey;
        if (!_hotkey.Register(modifiers, key))
            System.Diagnostics.Debug.WriteLine("Failed to register hotkey " + HotkeyService.FormatHotkey(modifiers, key));
    }

    private void MainWindow_OnClosed(object? sender, EventArgs e)
    {
        _hotkey.Dispose();
        _saveTimer.Stop();
        _clipboardTimer.Stop();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
    }

    private void TrySetWindowIcon()
    {
        try
        {
            var png = AppPaths.ResourcePath(Path.Combine("Assets", "icon.png"));
            if (!File.Exists(png))
                png = AppPaths.ResourcePath("icon.png");
            if (!File.Exists(png))
                return;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(png, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            Icon = bitmap;
        }
        catch
        {
        }
    }

    private void SetupTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(UiText.ShowMainWindow, null, (_, _) => ShowMainWindow());
        _trayToggleItem = new Forms.ToolStripMenuItem(UiText.StopListening, null, (_, _) =>
        {
            MonitorButton.IsChecked = !MonitorButton.IsChecked;
            ApplyMonitoringState();
        });
        menu.Items.Add(_trayToggleItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(UiText.Exit, null, (_, _) => QuitApp());

        _trayIcon = new Forms.NotifyIcon
        {
            Text = UiText.AppName,
            Visible = true,
            ContextMenuStrip = menu,
        };

        try
        {
            var ico = AppPaths.ResourcePath(Path.Combine("Assets", "icon.ico"));
            var png = AppPaths.ResourcePath(Path.Combine("Assets", "icon.png"));
            if (File.Exists(ico))
                _trayIcon.Icon = new Drawing.Icon(ico);
            else if (File.Exists(png))
            {
                using var bmp = new Drawing.Bitmap(png);
                _trayIcon.Icon = Drawing.Icon.FromHandle(bmp.GetHicon());
            }
            else
                _trayIcon.Icon = SystemIcons.Application;
        }
        catch
        {
            _trayIcon.Icon = SystemIcons.Application;
        }

        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                ShowMainWindow();
        };
    }

    private void PollClipboard()
    {
        if (!_monitoringEnabled || _suppressClipboard)
            return;

        try
        {
            if (System.Windows.Clipboard.ContainsImage())
            {
                var image = System.Windows.Clipboard.GetImage();
                if (image is null)
                    return;

                if (_lastClipboardSignature?.StartsWith("img:", StringComparison.Ordinal) == true
                    && (DateTime.Now - _lastImageCapture).TotalMilliseconds < 800)
                    return;

                CaptureImage(image);
                _lastClipboardSignature = $"img:{image.PixelWidth}x{image.PixelHeight}";
                return;
            }

            if (System.Windows.Clipboard.ContainsText())
            {
                var text = System.Windows.Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(text))
                    return;

                var signature = "text:" + text;
                if (signature == _lastClipboardSignature)
                    return;

                _lastClipboardSignature = signature;
                CaptureText(text);
            }
        }
        catch
        {
        }
    }

    private void CaptureText(string text)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var entry = new ClipboardEntry
        {
            Timestamp = timestamp,
            Type = "text",
            Text = text,
        };
        var top = _history.Insert(entry);
        HistoryList.SelectedItem = top;
        HistoryList.ScrollIntoView(top);
        ApplyFilter();
    }

    private void CaptureImage(BitmapSource image)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var fileName = $"{timestamp.Replace(':', '-').Replace(' ', '_')}.png";
        var path = Path.Combine(AppPaths.ImagesDir, fileName);

        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var fs = File.Create(path);
            encoder.Save(fs);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Save image failed: " + ex.Message);
            return;
        }

        _lastImageCapture = DateTime.Now;
        var entry = new ClipboardEntry
        {
            Timestamp = timestamp,
            Type = "image",
            ImagePath = AppPaths.ImagePathForStorage(path),
        };
        var top = _history.Insert(entry);
        HistoryList.SelectedItem = top;
        HistoryList.ScrollIntoView(top);
        ApplyFilter();
    }

    private void ShowMainWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        CenterOnScreen();
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void CenterOnScreen()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var screen = handle == IntPtr.Zero
            ? Forms.Screen.PrimaryScreen
            : Forms.Screen.FromHandle(handle);
        var area = screen.WorkingArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + (area.Height - Height) / 2;
    }

    private void QuitApp()
    {
        _reallyExit = true;
        _history.Save();
        Application.Current.Shutdown();
    }

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_reallyExit)
            return;

        e.Cancel = true;
        Hide();
    }

    private void MainWindow_OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
            Hide();
    }

    private void MonitorButton_OnClick(object sender, RoutedEventArgs e) => ApplyMonitoringState();

    private void ApplyMonitoringState()
    {
        _monitoringEnabled = MonitorButton.IsChecked == true;
        if (_monitoringEnabled)
        {
            MonitorButton.Content = UiText.StopListening;
            StatusLabel.Text = UiText.StatusListening;
            StatusLabel.Foreground = System.Windows.Media.Brushes.ForestGreen;
            if (_trayToggleItem is not null)
                _trayToggleItem.Text = UiText.StopListening;
        }
        else
        {
            MonitorButton.Content = UiText.StartListening;
            StatusLabel.Text = UiText.StatusIdle;
            StatusLabel.Foreground = System.Windows.Media.Brushes.Gray;
            if (_trayToggleItem is not null)
                _trayToggleItem.Text = UiText.StartListening;
        }
    }

    private void HistoryList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryList.SelectedItem is not ClipboardEntry entry)
        {
            PreviewText.Visibility = Visibility.Collapsed;
            PreviewCode.Visibility = Visibility.Collapsed;
            ImageScroll.Visibility = Visibility.Collapsed;
            _currentImageEntry = null;
            _currentTextEntry = null;
            _currentDetectedLanguage = null;
            PreviewLangLabel.Text = string.Empty;
            ToggleHighlightButton.Visibility = Visibility.Collapsed;
            return;
        }

        if (entry.IsImage)
        {
            var path = AppPaths.ResolveImagePath(entry.ImagePath);
            if (File.Exists(path))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                PreviewImage.Source = bitmap;
            }
            else
            {
                PreviewImage.Source = null;
            }

            PreviewText.Visibility = Visibility.Collapsed;
            PreviewCode.Visibility = Visibility.Collapsed;
            ImageScroll.Visibility = Visibility.Visible;
            _currentImageEntry = entry;
            _currentTextEntry = null;
            _currentDetectedLanguage = null;
            PreviewLangLabel.Text = string.Empty;
            ToggleHighlightButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            var text = entry.Text ?? string.Empty;
            _currentTextEntry = entry;
            _currentImageEntry = null;
            _currentDetectedLanguage = CodeLanguageDetector.Detect(text);
            ShowTextPreview(text, _currentDetectedLanguage);
        }
    }

    private void ShowTextPreview(string text, string? language)
    {
        ImageScroll.Visibility = Visibility.Collapsed;
        _currentImageEntry = null;

        if (!string.IsNullOrEmpty(language) && _showHighlighted)
        {
            PreviewCode.Document = CodeHighlighter.Highlight(text, language);
            PreviewCode.Visibility = Visibility.Visible;
            PreviewText.Visibility = Visibility.Collapsed;
            PreviewLangLabel.Text = $"已识别: {language}";
            ToggleHighlightButton.Visibility = Visibility.Visible;
            ToggleHighlightButton.Content = "原始文本";
        }
        else
        {
            PreviewText.Text = text;
            PreviewText.Visibility = Visibility.Visible;
            PreviewCode.Visibility = Visibility.Collapsed;
            if (!string.IsNullOrEmpty(language))
            {
                PreviewLangLabel.Text = $"已识别: {language}";
                ToggleHighlightButton.Visibility = Visibility.Visible;
                ToggleHighlightButton.Content = "代码高亮";
            }
            else
            {
                PreviewLangLabel.Text = string.Empty;
                ToggleHighlightButton.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void ToggleHighlightButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_currentTextEntry is null) return;
        _showHighlighted = !_showHighlighted;
        ShowTextPreview(_currentTextEntry.Text ?? string.Empty, _currentDetectedLanguage);
    }

    private void HistoryList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        => CopySelected();

    private void HistoryList_OnPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var item = ItemsControl.ContainerFromElement(HistoryList, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (item?.DataContext is not ClipboardEntry entry)
            return;

        HistoryList.SelectedItem = entry;
        var menu = new ContextMenu();
        var copy = new MenuItem { Header = UiText.Copy };
        copy.Click += (_, _) => CopySelected();
        var delete = new MenuItem { Header = UiText.Delete };
        delete.Click += (_, _) => DeleteEntry(entry);
        menu.Items.Add(copy);
        menu.Items.Add(new Separator());
        menu.Items.Add(delete);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void PreviewImage_OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_currentImageEntry is null)
            return;

        var menu = new ContextMenu();
        var copy = new MenuItem { Header = UiText.CopyImage };
        copy.Click += (_, _) => CopySelected();
        var save = new MenuItem { Header = UiText.SaveAs };
        save.Click += (_, _) => SaveCurrentImage();
        menu.Items.Add(copy);
        menu.Items.Add(new Separator());
        menu.Items.Add(save);
        menu.IsOpen = true;
    }

    private void Copy_OnClick(object sender, RoutedEventArgs e) => CopySelected();

    private void CopySelected()
    {
        if (HistoryList.SelectedItem is not ClipboardEntry entry)
            return;

        try
        {
            _suppressClipboard = true;
            if (entry.IsImage)
            {
                var path = AppPaths.ResolveImagePath(entry.ImagePath);
                if (!File.Exists(path))
                    return;
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                System.Windows.Clipboard.SetImage(bitmap);
                _lastClipboardSignature = $"img:self:{entry.ImagePath}";
                _lastImageCapture = DateTime.Now;
            }
            else
            {
                System.Windows.Clipboard.SetText(entry.Text ?? string.Empty);
                _lastClipboardSignature = "text:" + (entry.Text ?? string.Empty);
            }
        }
        finally
        {
            Dispatcher.BeginInvoke(() => _suppressClipboard = false, DispatcherPriority.Background);
        }
    }

    private void SaveCurrentImage()
    {
        if (_currentImageEntry is null)
            return;

        var path = AppPaths.ResolveImagePath(_currentImageEntry.ImagePath);
        if (!File.Exists(path))
        {
            MessageBox.Show(this, UiText.ImageMissing, UiText.Tip, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = UiText.SaveImage,
            FileName = Path.GetFileName(path),
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            File.Copy(path, dialog.FileName, true);
            MessageBox.Show(this, UiText.ImageSaved + "\n" + dialog.FileName, UiText.SaveSuccess,
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, UiText.SaveFailed + "\n" + ex.Message, UiText.SaveFailedTitle,
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteEntry(ClipboardEntry entry)
    {
        var result = MessageBox.Show(this, UiText.ConfirmDelete, UiText.ConfirmDeleteTitle,
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
            return;

        _history.Delete(entry);
        if (_history.Entries.Count == 0)
        {
            PreviewText.Clear();
            PreviewCode.Document = null;
            PreviewImage.Source = null;
            _currentImageEntry = null;
            _currentTextEntry = null;
            _currentDetectedLanguage = null;
            PreviewLangLabel.Text = string.Empty;
            ToggleHighlightButton.Visibility = Visibility.Collapsed;
        }
    }

    private void ClearButton_OnClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        void Add(string header, string mode)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => ClearByRange(mode);
            menu.Items.Add(item);
        }

        Add(UiText.ClearToday, "today");
        Add(UiText.ClearWeek, "week");
        Add(UiText.ClearMonth, "month");
        menu.Items.Add(new Separator());
        Add(UiText.ClearAll, "all");
        menu.PlacementTarget = ClearButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void ClearByRange(string mode)
    {
        var labels = new Dictionary<string, string>
        {
            ["today"] = UiText.RangeToday,
            ["week"] = UiText.RangeWeek,
            ["month"] = UiText.RangeMonth,
            ["all"] = UiText.RangeAll,
        };
        var label = labels.GetValueOrDefault(mode, UiText.RangeSelected);
        var count = _history.Entries.Count(entry => mode == "all" || MatchesRange(entry, mode));
        if (count == 0)
        {
            MessageBox.Show(this, string.Format(UiText.NothingToClear, label), UiText.Tip,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            this,
            string.Format(UiText.ConfirmClear, label, count),
            UiText.ConfirmClearTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
            return;

        var removed = _history.ClearByRange(mode);
        if (_history.Entries.Count == 0)
        {
            PreviewText.Clear();
            PreviewCode.Document = null;
            PreviewImage.Source = null;
            _currentImageEntry = null;
            _currentTextEntry = null;
            _currentDetectedLanguage = null;
            PreviewLangLabel.Text = string.Empty;
            ToggleHighlightButton.Visibility = Visibility.Collapsed;
        }

        MessageBox.Show(this, string.Format(UiText.Cleared, label, removed), UiText.Tip,
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static bool MatchesRange(ClipboardEntry entry, string mode)
    {
        if (!DateTime.TryParseExact(
                entry.Timestamp,
                "yyyy-MM-dd HH:mm:ss",
                null,
                System.Globalization.DateTimeStyles.None,
                out var dt))
            return false;

        var now = DateTime.Now;
        return mode switch
        {
            "today" => dt.Date == now.Date,
            "week" => dt >= now.AddDays(-7),
            "month" => dt >= now.AddDays(-30),
            _ => false,
        };
    }

    private void Export_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = UiText.ExportHistory,
            FileName = "clipboard_history.txt",
            Filter = "Text Files (*.txt)|*.txt",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            using var writer = new StreamWriter(dialog.FileName);
            foreach (var entry in _history.Entries)
            {
                if (!entry.IsImage)
                    writer.WriteLine(entry.Text);
                writer.WriteLine();
            }

            MessageBox.Show(this, UiText.ExportSuccess, UiText.ExportSuccessTitle,
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, UiText.ExportFailed + ": " + ex.Message, UiText.ExportFailedTitle,
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Filter_OnChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSearchPlaceholder();
        ApplyFilter();
    }

    private void Filter_OnChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();

    private void UpdateSearchPlaceholder()
    {
        if (SearchBox is null || SearchPlaceholder is null)
            return;
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ApplyFilter()
    {
        if (!IsLoaded || HistoryList?.ItemsSource is null || SearchBox is null || CategoryCombo is null)
            return;

        var view = CollectionViewSource.GetDefaultView(HistoryList.ItemsSource);
        if (view is null)
            return;

        var search = SearchBox.Text.Trim().ToLowerInvariant();
        var category = (CategoryCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? UiText.CategoryAll;

        view.Filter = obj =>
        {
            if (obj is not ClipboardEntry entry)
                return false;

            var typeMatch = true;
            if (category == UiText.CategoryText)
                typeMatch = !entry.IsImage;
            else if (category == UiText.CategoryImage)
                typeMatch = entry.IsImage;

            if (!typeMatch)
                return false;

            if (string.IsNullOrEmpty(search))
                return true;

            if (entry.IsImage)
            {
                var name = Path.GetFileName(AppPaths.ResolveImagePath(entry.ImagePath)).ToLowerInvariant();
                return entry.Timestamp.ToLowerInvariant().Contains(search) || name.Contains(search);
            }

            return entry.Timestamp.ToLowerInvariant().Contains(search)
                   || (entry.Text ?? string.Empty).ToLowerInvariant().Contains(search);
        };
    }

    private void About_OnClick(object sender, RoutedEventArgs e)
    {
        var build = string.IsNullOrEmpty(VersionInfo.BuildVersion)
            ? string.Empty
            : "\n" + UiText.BuildVersion + ": " + VersionInfo.BuildVersion;
        var builtAt = string.IsNullOrEmpty(VersionInfo.BuiltAt)
            ? string.Empty
            : "\n" + UiText.BuiltAt + ": " + VersionInfo.BuiltAt;

        MessageBox.Show(
            this,
            UiText.AppName + "\n" + UiText.Version + ": " + VersionInfo.Version + build + builtAt +
            "\n\n" + UiText.Copyright + "\n\n" + UiText.AboutBody,
            UiText.About,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void HotkeySettings_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new HotkeySettingsDialog(_config.Config.HotkeyModifiers, _config.Config.HotkeyKey)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true)
            return;

        if (dialog.Modifiers.Count == 0)
        {
            MessageBox.Show(this, UiText.NeedModifier, UiText.Error,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _config.Config.HotkeyModifiers = dialog.Modifiers;
        _config.Config.HotkeyKey = dialog.HotkeyKey;
        _config.Save();

        if (_hotkey.Register(dialog.Modifiers, dialog.HotkeyKey))
        {
            MessageBox.Show(this,
                UiText.HotkeySet + HotkeyService.FormatHotkey(dialog.Modifiers, dialog.HotkeyKey),
                UiText.Success, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(this,
                UiText.HotkeyRegisterFailed + HotkeyService.FormatHotkey(dialog.Modifiers, dialog.HotkeyKey),
                UiText.Warning, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AutostartMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (AutostartMenuItem.IsChecked)
        {
            if (AutostartService.Enable())
            {
                _config.Config.Autostart = true;
                _config.Save();
                MessageBox.Show(this, UiText.AutostartEnabled, UiText.Success,
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                AutostartMenuItem.IsChecked = false;
                MessageBox.Show(this, UiText.AutostartEnableFailed, UiText.Error,
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else
        {
            if (AutostartService.Disable())
            {
                _config.Config.Autostart = false;
                _config.Save();
                MessageBox.Show(this, UiText.AutostartDisabled, UiText.Success,
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                AutostartMenuItem.IsChecked = true;
                MessageBox.Show(this, UiText.AutostartDisableFailed, UiText.Error,
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Quit_OnClick(object sender, RoutedEventArgs e) => QuitApp();
}

internal sealed class RelayCommand(Action execute) : ICommand
{
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }
}
