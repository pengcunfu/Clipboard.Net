using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using ClipboardApp.Models;

namespace ClipboardApp.Services;

/// <summary>
/// Owns the in-memory <see cref="Entries"/> list that the UI binds to, and
/// delegates persistence to <see cref="HistoryStore"/>. The collection holds
/// only the currently visible window — the underlying SQLite database is the
/// source of truth and may contain more rows.
/// </summary>
public sealed class HistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HistoryStore _store;

    public ObservableCollection<ClipboardEntry> Entries { get; } = [];

    public HistoryService()
    {
        _store = new HistoryStore(AppPaths.HistoryDb);
        _store.Open();
        MigrateLegacyJson();
    }

    /// <summary>
    /// Reload the in-memory visible window from the database.
    /// </summary>
    public void ReloadView(HistoryQuery query)
    {
        var fresh = _store.Query(query);
        Entries.Clear();
        foreach (var e in fresh)
            Entries.Add(e);
    }

    /// <summary>
    /// Persist <paramref name="entry"/>; if the same content already exists,
    /// update its timestamp instead of creating a duplicate. Returns the row
    /// that ended up in the database (with its <see cref="ClipboardEntry.Id"/>
    /// populated). The caller is responsible for refreshing the visible window.
    /// </summary>
    public ClipboardEntry Insert(ClipboardEntry entry) => _store.Insert(entry);

    /// <summary>
    /// Delete a single entry by reference. Also removes its image file (if any).
    /// </summary>
    public bool Delete(ClipboardEntry entry)
    {
        if (entry.Id == 0) return false;
        DeleteEntryFiles(entry);
        var ok = _store.Delete(entry.Id);
        if (ok) Entries.Remove(entry);
        return ok;
    }

    /// <summary>
    /// Remove entries in the time window expressed by <paramref name="mode"/>:
    /// "today" (from 00:00 today), "week" (last 7 days), "month" (last 30 days),
    /// or "all". Returns the number of database rows removed.
    /// </summary>
    public int ClearByRange(string mode)
    {
        string? since = mode switch
        {
            "today" => TodayStartTimestamp(),
            "week"  => DaysAgoTimestamp(7),
            "month" => DaysAgoTimestamp(30),
            "all"   => null,
            _ => null,
        };

        // Collect image files to delete BEFORE wiping rows.
        var imagesQuery = new HistoryQuery(SinceTimestamp: since, Type: "image");
        var images = _store.Query(imagesQuery);
        foreach (var img in images)
            DeleteEntryFiles(img);

        int removed = since is null
            ? _store.DeleteAll()
            : _store.DeleteSince(since);

        if (removed > 0)
        {
            // Refresh the visible window to reflect the deletion.
            ReloadView(GetCurrentQuery());
        }
        return removed;
    }

    /// <summary>
    /// Returns the timestamp string for "today 00:00:00" in local time.
    /// </summary>
    public static string TodayStartTimestamp()
        => DateTime.Today.ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>
    /// Returns the timestamp string for "now minus N days" (used as a lower
    /// bound cutoff for "last week / last month" queries).
    /// </summary>
    public static string DaysAgoTimestamp(int days)
        => DateTime.Now.AddDays(-days).ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>
    /// Returns the number of rows that would be deleted by <see cref="ClearByRange"/> for
    /// the given mode. The visible window is irrelevant here — this counts the whole
    /// database so the confirmation message is accurate even when the user is looking
    /// at a narrow date range.
    /// </summary>
    public long CountByRange(string mode)
    {
        var since = mode switch
        {
            "today" => TodayStartTimestamp(),
            "week"  => DaysAgoTimestamp(7),
            "month" => DaysAgoTimestamp(30),
            "all"   => null,
            _ => null,
        };
        return _store.Count(new HistoryQuery(SinceTimestamp: since));
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

    // -- Legacy migration --

    private void MigrateLegacyJson()
    {
        var path = AppPaths.HistoryFile;
        if (!File.Exists(path)) return;
        // If the DB already has data, leave the JSON file alone — the user
        // may want to inspect or recover it. It will simply be ignored.
        if (_store.Count() > 0) return;

        try
        {
            var json = File.ReadAllText(path);
            var list = JsonSerializer.Deserialize<List<ClipboardEntry>>(json, JsonOptions);
            if (list is null || list.Count == 0) return;

            // Insert in chronological order so dedup doesn't fight us; the
            // resulting timestamps will be the original ones, and re-copies
            // after migration will surface the correct entry at the top.
            foreach (var entry in list)
            {
                if (string.IsNullOrWhiteSpace(entry.Timestamp)) continue;
                _store.Insert(entry);
            }
            System.Diagnostics.Debug.WriteLine($"已从旧版 JSON 迁移 {list.Count} 条记录到 SQLite。");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"迁移历史记录失败: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns the current UI-visible query. The history service doesn't
    /// itself remember the filter, so this is a thin wrapper for callers
    /// (e.g. after a destructive operation) to use a default.
    /// </summary>
    public HistoryQuery GetCurrentQuery() => new(Last3DaysCutoff());

    private static string Last3DaysCutoff()
        => DateTime.Now.AddDays(-3).ToString("yyyy-MM-dd HH:mm:ss");
}
