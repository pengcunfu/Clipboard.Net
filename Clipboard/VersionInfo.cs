namespace ClipboardApp;

public static class VersionInfo
{
    public const string Version = "1.0.0";
    public const int BuildNumber = 6;
    public const string BuiltAt = "2026-05-17 08:00:45";

    public static string BuildVersion =>
        BuildNumber > 0 ? $"{Version}.{BuildNumber}" : string.Empty;
}
