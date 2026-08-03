using System.IO;
using Microsoft.Win32;

namespace ClipboardApp.Services;

public static class AutostartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "熔岩超级剪贴板";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(ValueName) is not null;
        }
        catch
        {
            return false;
        }
    }

    public static bool Enable()
    {
        try
        {
            var exe = Environment.ProcessPath ??
                      Path.Combine(AppPaths.AppRoot, "熔岩超级剪贴板.exe");
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);
            key.SetValue(ValueName, $"\"{exe}\"");
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            key?.DeleteValue(ValueName, false);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
