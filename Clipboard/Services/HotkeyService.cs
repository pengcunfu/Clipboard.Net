using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ClipboardApp.Services;

public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 1;
    private const int WmHotkey = 0x0312;

    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    private readonly Window _window;
    private HwndSource? _source;
    private bool _registered;

    public event Action? HotkeyPressed;

    public HotkeyService(Window window)
    {
        _window = window;
        _window.SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(_window);
        _source = HwndSource.FromHwnd(helper.Handle);
        _source?.AddHook(WndProc);
    }

    public bool Register(IEnumerable<string> modifiers, string key)
    {
        Unregister();

        if (_source is null)
        {
            var helper = new WindowInteropHelper(_window);
            helper.EnsureHandle();
            _source = HwndSource.FromHwnd(helper.Handle);
            _source?.AddHook(WndProc);
        }

        if (_source is null)
            return false;

        uint mod = 0;
        foreach (var modifier in modifiers)
        {
            mod |= modifier.ToLowerInvariant() switch
            {
                "ctrl" or "control" => ModControl,
                "alt" => ModAlt,
                "shift" => ModShift,
                "win" or "meta" or "windows" => ModWin,
                _ => 0u,
            };
        }

        var vk = KeyToVirtualKey(key);
        if (vk == 0)
            return false;

        _registered = RegisterHotKey(_source.Handle, HotkeyId, mod, vk);
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered || _source is null)
            return;

        UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source?.RemoveHook(WndProc);
        _window.SourceInitialized -= OnSourceInitialized;
    }

    public static string FormatHotkey(IEnumerable<string> modifiers, string key)
        => string.Join('+', modifiers.Select(m => m.ToUpperInvariant()).Append(key.ToUpperInvariant()));

    public static bool TryParseKeyGesture(string gesture, out List<string> modifiers, out string key)
    {
        modifiers = [];
        key = string.Empty;
        if (string.IsNullOrWhiteSpace(gesture))
            return false;

        var parts = gesture.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;

        foreach (var part in parts[..^1])
        {
            var lower = part.ToLowerInvariant();
            if (lower is "ctrl" or "control")
                modifiers.Add("ctrl");
            else if (lower == "alt")
                modifiers.Add("alt");
            else if (lower == "shift")
                modifiers.Add("shift");
            else if (lower is "win" or "windows" or "meta")
                modifiers.Add("win");
        }

        key = parts[^1].Trim().ToLowerInvariant();
        if (key == "escape")
            key = "esc";
        return modifiers.Count > 0 && KeyToVirtualKey(key) != 0;
    }

    private static uint KeyToVirtualKey(string key)
    {
        var k = key.Trim().ToLowerInvariant();
        return k switch
        {
            "space" => 0x20,
            "esc" or "escape" => 0x1B,
            "f1" => 0x70, "f2" => 0x71, "f3" => 0x72, "f4" => 0x73,
            "f5" => 0x74, "f6" => 0x75, "f7" => 0x76, "f8" => 0x77,
            "f9" => 0x78, "f10" => 0x79, "f11" => 0x7A, "f12" => 0x7B,
            _ when k.Length == 1 && k[0] is >= 'a' and <= 'z' => (uint)(k[0] - 'a' + 0x41),
            _ when k.Length == 1 && k[0] is >= '0' and <= '9' => (uint)k[0],
            _ => 0,
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
