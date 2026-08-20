using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using ClipboardApp.Models;

namespace ClipboardApp.Services;

public sealed class HistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public ObservableCollection<ClipboardEntry> Entries { get; } = [];

    public void Load()
    {
        Entries.Clear();
        var path = AppPaths.HistoryFile;
        if (!File.Exists(path))
            return;

        try
        {
            var json = File.ReadAllText(path);
            var list = JsonSerializer.Deserialize<List<ClipboardEntry>>(json, JsonOptions);
            if (list is null)
                return;

            foreach (var entry in list)
                Entries.Add(entry);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载历史记录失败: {ex.Message}");
        }
    }

    public void Save()
    {
        try
        {
            var snapshot = Entries.Select(e => new ClipboardEntry
            {
                Timestamp = e.Timestamp,
                Type = e.Type,
                Text = e.Text,
                ImagePath = e.IsImage && !string.IsNullOrWhiteSpace(e.ImagePath)
                    ? AppPaths.ImagePathForStorage(AppPaths.ResolveImagePath(e.ImagePath))
                    : e.ImagePath,
            }).ToList();

            Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.HistoryFile)!);
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            File.WriteAllText(AppPaths.HistoryFile, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存历史记录失败: {ex.Message}");
        }
    }

    /// <summary>
    /// Inserts <paramref name="entry"/> at the top of the history.
    /// If an entry with the same content already exists, it is moved to the top
    /// and its timestamp updated instead of creating a duplicate.
    /// Returns the entry that ended up on top of the list.
    /// </summary>
    public ClipboardEntry Insert(ClipboardEntry entry)
    {
        var existing = FindDuplicate(entry);
        if (existing is not null)
        {
            existing.Timestamp = entry.Timestamp;
            var index = Entries.IndexOf(existing);
            Entries.Move(index, 0);
            Save();
            return existing;
        }

        Entries.Insert(0, entry);
        Save();
        return entry;
    }

    private ClipboardEntry? FindDuplicate(ClipboardEntry entry)
    {
        if (entry.IsImage)
        {
            var newPath = entry.ImagePath;
            if (string.IsNullOrWhiteSpace(newPath))
                return null;
            return Entries.FirstOrDefault(e =>
                e.IsImage &&
                !string.IsNullOrWhiteSpace(e.ImagePath) &&
                string.Equals(e.ImagePath, newPath, StringComparison.OrdinalIgnoreCase));
        }

        var newText = entry.Text ?? string.Empty;
        return Entries.FirstOrDefault(e =>
            !e.IsImage &&
            string.Equals(e.Text ?? string.Empty, newText, StringComparison.Ordinal));
    }

    public void Delete(ClipboardEntry entry)
    {
        DeleteEntryFiles(entry);
        Entries.Remove(entry);
        Save();
    }

    public int ClearByRange(string mode)
    {
        var toRemove = Entries.Where(e => EntryInRange(e, mode)).ToList();
        foreach (var entry in toRemove)
        {
            DeleteEntryFiles(entry);
            Entries.Remove(entry);
        }

        if (toRemove.Count > 0)
            Save();
        return toRemove.Count;
    }

    public static void DeleteEntryFiles(ClipboardEntry entry)
    {
        if (!entry.IsImage || string.IsNullOrWhiteSpace(entry.ImagePath))
            return;

        var path = AppPaths.ResolveImagePath(entry.ImagePath);
        if (!File.Exists(path))
            return;

        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"删除图片失败: {ex.Message}");
        }
    }

    private static bool EntryInRange(ClipboardEntry entry, string mode)
    {
        if (mode == "all")
            return true;

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
}
