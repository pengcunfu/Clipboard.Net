using System.Security.Cryptography;
using System.Text;
using ClipboardApp.Models;
using Microsoft.Data.Sqlite;

namespace ClipboardApp.Services;

/// <summary>
/// Filter parameters for <see cref="HistoryStore.Query"/>.
/// </summary>
public sealed record HistoryQuery(
    string? SinceTimestamp = null,   // exclusive lower bound (yyyy-MM-dd HH:mm:ss), null = no lower bound
    string? Type = null,             // "text" or "image" (null = both)
    string? Search = null,           // LIKE pattern, applied to text and image filename
    int Limit = 5000
);

/// <summary>
/// SQLite-backed persistence layer for clipboard history.
/// One row per unique clipboard entry; deduplication is enforced via a content hash.
/// </summary>
public sealed class HistoryStore : IDisposable
{
    private readonly string _connectionString;
    private SqliteConnection? _connection;
    private bool _disposed;

    public string DatabasePath { get; }

    public HistoryStore(string dbPath)
    {
        DatabasePath = dbPath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        }.ToString();
    }

    public void Open()
    {
        if (_connection is not null) return;
        var dir = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _connection = new SqliteConnection(_connectionString);
        _connection.Open();
        EnsureSchema();
    }

    private SqliteConnection Conn
    {
        get
        {
            if (_disposed) throw new ObjectDisposedException(nameof(HistoryStore));
            if (_connection is null) Open();
            return _connection!;
        }
    }

    private void EnsureSchema()
    {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            CREATE TABLE IF NOT EXISTS entries (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp    TEXT    NOT NULL,
                type         TEXT    NOT NULL,
                content_hash TEXT    NOT NULL,
                text         TEXT,
                image_path   TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_entries_timestamp ON entries(timestamp DESC);
            CREATE INDEX IF NOT EXISTS idx_entries_hash      ON entries(content_hash);
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Insert a new entry, or move the existing one with the same content to the top
    /// (timestamp updated). Returns the entry that ended up in the database — its
    /// <see cref="ClipboardEntry.Id"/> is set to the database row id.
    /// </summary>
    public ClipboardEntry Insert(ClipboardEntry entry)
    {
        var hash = ComputeHash(entry);

        // 1. Try to find an existing row with the same hash
        long? existingId = null;
        string existingType = entry.Type;
        string? existingText = entry.Text;
        string? existingImage = entry.ImagePath;

        using (var find = Conn.CreateCommand())
        {
            find.CommandText =
                "SELECT id, type, text, image_path FROM entries WHERE content_hash = $hash LIMIT 1";
            find.Parameters.AddWithValue("$hash", hash);
            using var reader = find.ExecuteReader();
            if (reader.Read())
            {
                existingId = reader.GetInt64(0);
                existingType = reader.GetString(1);
                existingText = reader.IsDBNull(2) ? null : reader.GetString(2);
                existingImage = reader.IsDBNull(3) ? null : reader.GetString(3);
            }
        }

        if (existingId is long id)
        {
            using var update = Conn.CreateCommand();
            update.CommandText = "UPDATE entries SET timestamp = $timestamp WHERE id = $id";
            update.Parameters.AddWithValue("$timestamp", entry.Timestamp);
            update.Parameters.AddWithValue("$id", id);
            update.ExecuteNonQuery();

            return new ClipboardEntry
            {
                Id = id,
                Timestamp = entry.Timestamp,
                Type = existingType,
                Text = existingText,
                ImagePath = existingImage,
            };
        }

        // 2. Otherwise insert a fresh row
        using var insert = Conn.CreateCommand();
        insert.CommandText = """
            INSERT INTO entries (timestamp, type, content_hash, text, image_path)
            VALUES ($timestamp, $type, $hash, $text, $imagePath);
            SELECT last_insert_rowid();
            """;
        insert.Parameters.AddWithValue("$timestamp", entry.Timestamp);
        insert.Parameters.AddWithValue("$type", entry.Type);
        insert.Parameters.AddWithValue("$hash", hash);
        insert.Parameters.AddWithValue("$text", (object?)entry.Text ?? DBNull.Value);
        insert.Parameters.AddWithValue("$imagePath", (object?)entry.ImagePath ?? DBNull.Value);
        var newId = Convert.ToInt64(insert.ExecuteScalar() ?? 0L);

        return new ClipboardEntry
        {
            Id = newId,
            Timestamp = entry.Timestamp,
            Type = entry.Type,
            Text = entry.Text,
            ImagePath = entry.ImagePath,
        };
    }

    /// <summary>
    /// Delete a single entry by its database id.
    /// </summary>
    public bool Delete(long id)
    {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "DELETE FROM entries WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// Delete every entry whose timestamp is at or newer than <paramref name="sinceTimestamp"/>.
    /// Used by "clear today / last week / last month".
    /// </summary>
    public int DeleteSince(string sinceTimestamp)
    {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "DELETE FROM entries WHERE timestamp >= $since";
        cmd.Parameters.AddWithValue("$since", sinceTimestamp);
        return cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Delete every entry. Returns the number of rows removed.
    /// </summary>
    public int DeleteAll()
    {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "DELETE FROM entries";
        return cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Total number of rows in the database (across all dates).
    /// </summary>
    public long Count()
    {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM entries";
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
    }

    /// <summary>
    /// Count rows matching the given query (used for "you are about to delete N rows" messages).
    /// </summary>
    public long Count(HistoryQuery query)
    {
        var sql = new StringBuilder("SELECT COUNT(*) FROM entries WHERE 1=1");
        using var cmd = Conn.CreateCommand();

        if (!string.IsNullOrEmpty(query.SinceTimestamp))
        {
            sql.Append(" AND timestamp >= $since");
            cmd.Parameters.AddWithValue("$since", query.SinceTimestamp);
        }
        if (!string.IsNullOrEmpty(query.Type))
        {
            sql.Append(" AND type = $type");
            cmd.Parameters.AddWithValue("$type", query.Type);
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var like = EscapeLike(query.Search.Trim());
            sql.Append(" AND (text LIKE $search ESCAPE '\\' OR image_path LIKE $search ESCAPE '\\')");
            cmd.Parameters.AddWithValue("$search", "%" + like + "%");
        }

        cmd.CommandText = sql.ToString();
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
    }

    /// <summary>
    /// Run a filtered query and return matching entries, newest first.
    /// </summary>
    public List<ClipboardEntry> Query(HistoryQuery query)
    {
        var sql = new StringBuilder(
            "SELECT id, timestamp, type, text, image_path FROM entries WHERE 1=1");
        using var cmd = Conn.CreateCommand();

        if (!string.IsNullOrEmpty(query.SinceTimestamp))
        {
            sql.Append(" AND timestamp >= $since");
            cmd.Parameters.AddWithValue("$since", query.SinceTimestamp);
        }

        if (!string.IsNullOrEmpty(query.Type))
        {
            sql.Append(" AND type = $type");
            cmd.Parameters.AddWithValue("$type", query.Type);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Escape LIKE wildcards so user input doesn't surprise us.
            var like = EscapeLike(query.Search.Trim());
            sql.Append(" AND (text LIKE $search ESCAPE '\\' OR image_path LIKE $search ESCAPE '\\')");
            cmd.Parameters.AddWithValue("$search", "%" + like + "%");
        }

        sql.Append(" ORDER BY timestamp DESC");
        if (query.Limit > 0)
        {
            sql.Append(" LIMIT $limit");
            cmd.Parameters.AddWithValue("$limit", query.Limit);
        }

        cmd.CommandText = sql.ToString();

        var result = new List<ClipboardEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new ClipboardEntry
            {
                Id = reader.GetInt64(0),
                Timestamp = reader.GetString(1),
                Type = reader.GetString(2),
                Text = reader.IsDBNull(3) ? null : reader.GetString(3),
                ImagePath = reader.IsDBNull(4) ? null : reader.GetString(4),
            });
        }
        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _connection?.Close();
            _connection?.Dispose();
        }
        catch
        {
            // ignore — closing during shutdown is best-effort
        }
        _connection = null;
    }

    private static string ComputeHash(ClipboardEntry entry)
    {
        // For text: hash the literal content. For image: hash the absolute path
        // (case-insensitive). The hash is purely a dedup key.
        using var sha = SHA256.Create();
        byte[] bytes;
        if (entry.IsImage)
        {
            bytes = Encoding.UTF8.GetBytes("img:" + (entry.ImagePath ?? string.Empty).ToLowerInvariant());
        }
        else
        {
            bytes = Encoding.UTF8.GetBytes("txt:" + (entry.Text ?? string.Empty));
        }
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }

    private static string EscapeLike(string s)
    {
        return s
            .Replace(@"\", @"\\")
            .Replace("%", @"\%")
            .Replace("_", @"\_");
    }
}
