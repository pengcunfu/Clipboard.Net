namespace ClipboardApp;

public static class VersionInfo
{
    public const string Version = "1.0.0";
    public const int BuildNumber = 16;
    public const string BuiltAt = "2026-08-21 00:00:51";

    public static string BuildVersion =>
        BuildNumber > 0 ? $"{Version}.{BuildNumber}" : string.Empty;
}
