using System.IO;

namespace ClipboardApp.Services;

public static class AppPaths
{
    public static string AppRoot
    {
        get
        {
            var baseDir = AppContext.BaseDirectory.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return baseDir;
        }
    }

    public static string DataDir
    {
        get
        {
            var path = Path.Combine(AppRoot, "data");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public static string ImagesDir
    {
        get
        {
            var path = Path.Combine(DataDir, "clipboard_images");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public static string ConfigFile => Path.Combine(DataDir, "config.json");

    public static string HistoryFile => Path.Combine(DataDir, "clipboard_history.json");

    public static string ResourcePath(string name) => Path.Combine(AppRoot, name);

    public static string ResolveImagePath(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
            return Path.Combine(ImagesDir, "unknown.png");

        if (File.Exists(stored))
            return Path.GetFullPath(stored);

        foreach (var baseDir in new[] { DataDir, AppRoot })
        {
            var candidate = Path.Combine(baseDir, stored);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        return Path.GetFullPath(Path.Combine(DataDir, stored));
    }

    public static string ImagePathForStorage(string path)
    {
        var full = Path.GetFullPath(path);
        var data = Path.GetFullPath(DataDir);
        if (full.StartsWith(data, StringComparison.OrdinalIgnoreCase))
        {
            var relative = Path.GetRelativePath(data, full);
            return relative.Replace('\\', '/');
        }

        return full;
    }

    public static void MigrateLegacyData()
    {
        var root = AppRoot;
        var dest = DataDir;
        var moves = new (string Src, string Dst)[]
        {
            ("config.json", "config.json"),
            ("clipboard_history.json", "clipboard_history.json"),
            ("clipboard_images", "clipboard_images"),
        };

        foreach (var (srcName, dstName) in moves)
        {
            var src = Path.Combine(root, srcName);
            var dst = Path.Combine(dest, dstName);
            if (!File.Exists(src) && !Directory.Exists(src))
                continue;
            if (File.Exists(dst) || Directory.Exists(dst))
                continue;

            if (Directory.Exists(src))
                Directory.Move(src, dst);
            else
                File.Move(src, dst);
        }
    }
}
