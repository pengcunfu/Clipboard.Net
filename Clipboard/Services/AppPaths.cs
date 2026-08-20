using System.IO;

namespace ClipboardApp.Services;

public static class AppPaths
{
    private const string VendorFolderName = "FNSoftware";
    private const string AppFolderName = "Clipboard";

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
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var path = Path.Combine(documents, VendorFolderName, AppFolderName);
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

    public static string HistoryDb => Path.Combine(DataDir, "clipboard.db");

    public static string ResourcePath(string name) => Path.Combine(AppRoot, name);

    public static string ResolveImagePath(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
            return Path.Combine(ImagesDir, "unknown.png");

        if (File.Exists(stored))
            return Path.GetFullPath(stored);

        foreach (var baseDir in LegacyDataRoots().Prepend(DataDir).Distinct(StringComparer.OrdinalIgnoreCase))
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
        var dest = DataDir;
        var names = new[] { "config.json", "clipboard_history.json", "clipboard_images" };

        foreach (var srcRoot in LegacyDataRoots())
        {
            if (string.Equals(Path.GetFullPath(srcRoot), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var name in names)
            {
                var src = Path.Combine(srcRoot, name);
                var dst = Path.Combine(dest, name);
                if (!File.Exists(src) && !Directory.Exists(src))
                    continue;
                if (File.Exists(dst) || Directory.Exists(dst))
                    continue;

                try
                {
                    if (Directory.Exists(src))
                        Directory.Move(src, dst);
                    else
                        File.Move(src, dst);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"迁移数据失败 {src} -> {dst}: {ex.Message}");
                }
            }
        }
    }

    private static IEnumerable<string> LegacyDataRoots()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        yield return Path.Combine(documents, VendorFolderName);
        yield return Path.Combine(AppRoot, "data");
        yield return AppRoot;
    }
}
