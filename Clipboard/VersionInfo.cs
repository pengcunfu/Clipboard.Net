namespace ClipboardApp;

public static class VersionInfo
{
    public const string Version = "1.0.0";
    public const int BuildNumber = 14;
    public const string BuiltAt = "2026-08-09 13:26:21";

    public static string BuildVersion =>
        BuildNumber > 0 ? $"{Version}.{BuildNumber}" : string.Empty;
}
