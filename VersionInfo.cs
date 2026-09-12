namespace ClipboardApp;

public static class VersionInfo
{
    public const string Version = "1.1.0";
    public const int BuildNumber = 0;
    public const string BuiltAt = "2026-09-13 03:58:22";

    public static string BuildVersion =>
        BuildNumber > 0 ? $"{Version}.{BuildNumber}" : string.Empty;
}
