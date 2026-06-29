using Microsoft.Data.Sqlite;
using ClippyPlus.App.Models;

namespace ClippyPlus.App.Services;

public sealed class ClipboardHistoryStore : IClipboardHistoryStore
{
    private const int MaxItems = 100;
    private readonly string _databasePath;
    private readonly IClipboardSecurityService _securityService;

    public ClipboardHistoryStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClippyPlus",
            "clipboard-history.db"),
            null)
    {
    }

    public ClipboardHistoryStore(string databasePath)
        : this(databasePath, null)
    {
    }

    public ClipboardHistoryStore(string databasePath, IClipboardSecurityService? securityService)
    {
        _databasePath = databasePath;
        _securityService = securityService ?? new ClipboardSecurityService(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        Initialize();
        _securityService.Unlocked += (_, _) => MigrateLegacyPlaintextRows();
    }

    public IReadOnlyList<ClipboardHistoryItem> GetRecent(int limit)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Kind, CreatedAt, UpdatedAt, Preview, ContentHash, TextContent, ImageBytes
            FROM ClipboardHistoryItems
            ORDER BY UpdatedAt DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        return ReadItems(command);
    }

    public IReadOnlyList<ClipboardHistoryItem> SearchText(string query, int limit)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return GetRecent(limit);
        }

        var normalizedQuery = query.Trim();
        return GetRecent(MaxItems)
            .Where(item => item is { Kind: ClipboardHistoryKind.Text, TextContent: not null }
                && item.TextContent.Contains(normalizedQuery, StringComparison.CurrentCultureIgnoreCase))
            .Take(limit)
            .ToList();
    }

    public void Upsert(ClipboardHistoryEntry entry)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ClipboardHistoryItems (Kind, CreatedAt, UpdatedAt, Preview, ContentHash, TextContent, ImageBytes, IsEncrypted)
            VALUES ($kind, $createdAt, $updatedAt, $preview, $contentHash, $textContent, $imageBytes, 1)
            ON CONFLICT(ContentHash) DO UPDATE SET
                UpdatedAt = excluded.UpdatedAt,
                Preview = excluded.Preview,
                TextContent = excluded.TextContent,
                ImageBytes = excluded.ImageBytes,
                IsEncrypted = 1;
            """;
        command.Parameters.AddWithValue("$kind", entry.Kind.ToString());
        var now = DateTimeOffset.Now.ToString("O");
        command.Parameters.AddWithValue("$createdAt", now);
        command.Parameters.AddWithValue("$updatedAt", now);
        command.Parameters.AddWithValue("$preview", _securityService.ProtectString(entry.Preview));
        command.Parameters.AddWithValue("$contentHash", _securityService.ComputeProtectedContentHash(entry.ContentHash));
        command.Parameters.AddWithValue("$textContent", entry.TextContent is null ? DBNull.Value : _securityService.ProtectString(entry.TextContent));
        command.Parameters.AddWithValue("$imageBytes", entry.ImageBytes is null ? DBNull.Value : _securityService.ProtectBytes(entry.ImageBytes));
        command.ExecuteNonQuery();

        Trim(connection);
    }

    public void Delete(long id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ClipboardHistoryItems WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void DeleteAll()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ClipboardHistoryItems;";
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
        connection.Open();
        return connection;
    }

    private IReadOnlyList<ClipboardHistoryItem> ReadItems(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var items = new List<ClipboardHistoryItem>();
        while (reader.Read())
        {
            var kind = Enum.Parse<ClipboardHistoryKind>(reader.GetString(1));
            var imageBytes = reader.IsDBNull(7) ? null : _securityService.UnprotectBytes((byte[])reader["ImageBytes"]);
            items.Add(new ClipboardHistoryItem
            {
                Id = reader.GetInt64(0),
                Kind = kind,
                CreatedAt = DateTimeOffset.Parse(reader.GetString(2)),
                UpdatedAt = DateTimeOffset.Parse(reader.GetString(3)),
                Preview = _securityService.UnprotectString(reader.GetString(4)),
                ContentHash = reader.GetString(5),
                TextContent = reader.IsDBNull(6) ? null : _securityService.UnprotectString(reader.GetString(6)),
                ImageBytes = imageBytes,
            });
        }

        return items;
    }

    private void Initialize()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ClipboardHistoryItems (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Kind TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                Preview TEXT NOT NULL,
                ContentHash TEXT NOT NULL UNIQUE,
                TextContent BLOB NULL,
                ImageBytes BLOB NULL,
                IsEncrypted INTEGER NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS IX_ClipboardHistoryItems_UpdatedAt
            ON ClipboardHistoryItems (UpdatedAt DESC);
            """;
        command.ExecuteNonQuery();

        using var migrationCommand = connection.CreateCommand();
        migrationCommand.CommandText = """
            ALTER TABLE ClipboardHistoryItems
            ADD COLUMN IsEncrypted INTEGER NOT NULL DEFAULT 0;
            """;
        try
        {
            migrationCommand.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1)
        {
            // Existing databases already have the migration column.
        }
    }

    private void MigrateLegacyPlaintextRows()
    {
        using var connection = OpenConnection();
        using var selectCommand = connection.CreateCommand();
        selectCommand.CommandText = """
            SELECT Id, Kind, Preview, ContentHash, TextContent, ImageBytes
            FROM ClipboardHistoryItems
            WHERE IsEncrypted = 0;
            """;
        using var reader = selectCommand.ExecuteReader();
        var legacyRows = new List<LegacyRow>();
        while (reader.Read())
        {
            legacyRows.Add(new LegacyRow(
                reader.GetInt64(0),
                Enum.Parse<ClipboardHistoryKind>(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : (byte[])reader["ImageBytes"]));
        }

        foreach (var row in legacyRows)
        {
            using var updateCommand = connection.CreateCommand();
            updateCommand.CommandText = """
                UPDATE ClipboardHistoryItems
                SET Preview = $preview,
                    ContentHash = $contentHash,
                    TextContent = $textContent,
                    ImageBytes = $imageBytes,
                    IsEncrypted = 1
                WHERE Id = $id;
                """;
            updateCommand.Parameters.AddWithValue("$id", row.Id);
            updateCommand.Parameters.AddWithValue("$preview", _securityService.ProtectString(row.Preview));
            updateCommand.Parameters.AddWithValue("$contentHash", _securityService.ComputeProtectedContentHash(row.ContentHash));
            updateCommand.Parameters.AddWithValue("$textContent", row.TextContent is null ? DBNull.Value : _securityService.ProtectString(row.TextContent));
            updateCommand.Parameters.AddWithValue("$imageBytes", row.ImageBytes is null ? DBNull.Value : _securityService.ProtectBytes(row.ImageBytes));
            updateCommand.ExecuteNonQuery();
        }
    }

    private static void Trim(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM ClipboardHistoryItems
            WHERE Id NOT IN (
                SELECT Id
                FROM ClipboardHistoryItems
                ORDER BY UpdatedAt DESC
                LIMIT $limit
            );
            """;
        command.Parameters.AddWithValue("$limit", MaxItems);
        command.ExecuteNonQuery();
    }

    private sealed record LegacyRow(
        long Id,
        ClipboardHistoryKind Kind,
        string Preview,
        string ContentHash,
        string? TextContent,
        byte[]? ImageBytes);
}
