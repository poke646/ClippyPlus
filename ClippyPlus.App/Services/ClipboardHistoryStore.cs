using ClippyPlus.App.Models;
using Microsoft.Data.Sqlite;

namespace ClippyPlus.App.Services;

public sealed class ClipboardHistoryStore : IClipboardHistoryStore
{
    public const int MaxHistoryItems = 100;
    public const int MaxSections = 10;
    public const int MaxSectionItems = 100;
    public const int MaxSectionNameLength = 40;

    private const string DefaultSectionMigration = "CreateDefaultPinnedSectionV1";
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
        return GetRecent(limit, includeImageBytes: true);
    }

    public IReadOnlyList<ClipboardHistoryItem> GetRecent(int limit, bool includeImageBytes)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        var imageColumn = includeImageBytes ? "ImageBytes" : "NULL";
        command.CommandText = $"""
            SELECT Id, Kind, CreatedAt, UpdatedAt, Preview, ContentHash, TextContent, {imageColumn}
            FROM ClipboardHistoryItems
            ORDER BY UpdatedAt DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        return ReadItems(command);
    }

    public IReadOnlyList<ClipboardHistoryItem> SearchText(string query, int limit)
    {
        return SearchItems(GetRecent(MaxHistoryItems), query, limit);
    }

    public IReadOnlyList<ClipboardSection> GetSections()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.Id, s.Name, s.ItemLimit, COUNT(i.Id)
            FROM ClipboardSections s
            LEFT JOIN PinnedClipboardItems i ON i.SectionId = s.Id
            GROUP BY s.Id, s.Name, s.ItemLimit, s.SortOrder
            ORDER BY s.SortOrder, s.Id;
            """;
        using var reader = command.ExecuteReader();
        var sections = new List<ClipboardSection>();
        while (reader.Read())
        {
            sections.Add(new ClipboardSection(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetInt32(3)));
        }

        return sections;
    }

    public IReadOnlyList<ClipboardHistoryItem> GetSectionItems(long sectionId, int limit)
    {
        return GetSectionItems(sectionId, limit, includeImageBytes: true);
    }

    public IReadOnlyList<ClipboardHistoryItem> GetSectionItems(long sectionId, int limit, bool includeImageBytes)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        var imageColumn = includeImageBytes ? "ImageBytes" : "NULL";
        command.CommandText = $"""
            SELECT Id, Kind, CreatedAt, UpdatedAt, Preview, ContentHash, TextContent, {imageColumn}
            FROM PinnedClipboardItems
            WHERE SectionId = $sectionId
            ORDER BY UpdatedAt DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$sectionId", sectionId);
        command.Parameters.AddWithValue("$limit", limit);
        return ReadItems(command);
    }

    public byte[]? GetImageBytes(long itemId, long? sectionId, string contentHash)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        if (sectionId is { } id)
        {
            command.CommandText = """
                SELECT ImageBytes FROM PinnedClipboardItems
                WHERE Id = $itemId AND SectionId = $sectionId
                  AND ContentHash = $contentHash AND Kind = $kind;
                """;
            command.Parameters.AddWithValue("$sectionId", id);
        }
        else
        {
            command.CommandText = """
                SELECT ImageBytes FROM ClipboardHistoryItems
                WHERE Id = $itemId AND ContentHash = $contentHash AND Kind = $kind;
                """;
        }

        command.Parameters.AddWithValue("$itemId", itemId);
        command.Parameters.AddWithValue("$contentHash", contentHash);
        command.Parameters.AddWithValue("$kind", ClipboardHistoryKind.Image.ToString());
        return command.ExecuteScalar() is byte[] bytes ? _securityService.UnprotectBytes(bytes) : null;
    }

    public IReadOnlyList<ClipboardHistoryItem> SearchSectionText(long sectionId, string query, int limit)
    {
        return SearchItems(GetSectionItems(sectionId, MaxSectionItems), query, limit);
    }

    public void ApplySections(IReadOnlyList<ClipboardSectionDraft> sections)
    {
        ValidateSections(sections);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var existingIds = GetExistingSectionIds(connection, transaction);
        var requestedIds = sections.Where(section => section.Id.HasValue).Select(section => section.Id!.Value).ToHashSet();
        if (!requestedIds.IsSubsetOf(existingIds))
        {
            throw new InvalidOperationException(LocalizationService.T("Validation.SectionNotFound"));
        }

        foreach (var section in sections.Where(section => section.Id.HasValue))
        {
            using var count = connection.CreateCommand();
            count.Transaction = transaction;
            count.CommandText = "SELECT COUNT(*) FROM PinnedClipboardItems WHERE SectionId = $id;";
            count.Parameters.AddWithValue("$id", section.Id!.Value);
            if (Convert.ToInt32(count.ExecuteScalar()) > section.ItemLimit)
            {
                throw new InvalidOperationException(LocalizationService.T("Validation.SectionLimitBelowCount"));
            }
        }

        foreach (var deletedId in existingIds.Except(requestedIds))
        {
            using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM ClipboardSections WHERE Id = $id;";
            delete.Parameters.AddWithValue("$id", deletedId);
            delete.ExecuteNonQuery();
        }

        foreach (var existingId in requestedIds)
        {
            using var temporaryRename = connection.CreateCommand();
            temporaryRename.Transaction = transaction;
            temporaryRename.CommandText = "UPDATE ClipboardSections SET Name = $name WHERE Id = $id;";
            temporaryRename.Parameters.AddWithValue("$id", existingId);
            temporaryRename.Parameters.AddWithValue("$name", $"__clippy_section_{existingId}_{Guid.NewGuid():N}");
            temporaryRename.ExecuteNonQuery();
        }

        for (var index = 0; index < sections.Count; index++)
        {
            var section = sections[index];
            var name = section.Name.Trim();
            if (section.Id is { } id)
            {
                using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE ClipboardSections
                    SET Name = $name, ItemLimit = $limit, SortOrder = $sortOrder
                    WHERE Id = $id;
                    """;
                update.Parameters.AddWithValue("$id", id);
                update.Parameters.AddWithValue("$name", name);
                update.Parameters.AddWithValue("$limit", section.ItemLimit);
                update.Parameters.AddWithValue("$sortOrder", index);
                update.ExecuteNonQuery();
            }
            else
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO ClipboardSections (Name, ItemLimit, SortOrder)
                    VALUES ($name, $limit, $sortOrder);
                    """;
                insert.Parameters.AddWithValue("$name", name);
                insert.Parameters.AddWithValue("$limit", section.ItemLimit);
                insert.Parameters.AddWithValue("$sortOrder", index);
                insert.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }

    public void Upsert(ClipboardHistoryEntry entry)
    {
        using var connection = OpenConnection();
        var protectedHash = _securityService.ComputeProtectedContentHash(entry.ContentHash);
        using (var pinnedCheck = connection.CreateCommand())
        {
            pinnedCheck.CommandText = "SELECT EXISTS(SELECT 1 FROM PinnedClipboardItems WHERE ContentHash = $contentHash);";
            pinnedCheck.Parameters.AddWithValue("$contentHash", protectedHash);
            if (Convert.ToInt32(pinnedCheck.ExecuteScalar()) != 0)
            {
                return;
            }
        }

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
        command.Parameters.AddWithValue("$contentHash", protectedHash);
        command.Parameters.AddWithValue("$textContent", entry.TextContent is null ? DBNull.Value : _securityService.ProtectString(entry.TextContent));
        command.Parameters.AddWithValue("$imageBytes", entry.ImageBytes is null ? DBNull.Value : _securityService.ProtectBytes(entry.ImageBytes));
        command.ExecuteNonQuery();
        TrimHistory(connection, null);
    }

    public PinClipboardItemResult Pin(long historyItemId, long sectionId)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        int itemLimit;
        using (var sectionCommand = connection.CreateCommand())
        {
            sectionCommand.Transaction = transaction;
            sectionCommand.CommandText = "SELECT ItemLimit FROM ClipboardSections WHERE Id = $id;";
            sectionCommand.Parameters.AddWithValue("$id", sectionId);
            var value = sectionCommand.ExecuteScalar();
            if (value is null)
            {
                return PinClipboardItemResult.SectionNotFound;
            }

            itemLimit = Convert.ToInt32(value);
        }

        using (var countCommand = connection.CreateCommand())
        {
            countCommand.Transaction = transaction;
            countCommand.CommandText = "SELECT COUNT(*) FROM PinnedClipboardItems WHERE SectionId = $sectionId;";
            countCommand.Parameters.AddWithValue("$sectionId", sectionId);
            if (Convert.ToInt32(countCommand.ExecuteScalar()) >= itemLimit)
            {
                return PinClipboardItemResult.SectionFull;
            }
        }

        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO PinnedClipboardItems
                    (SectionId, Kind, CreatedAt, UpdatedAt, Preview, ContentHash, TextContent, ImageBytes, IsEncrypted)
                SELECT $sectionId, Kind, CreatedAt, $updatedAt, Preview, ContentHash, TextContent, ImageBytes, IsEncrypted
                FROM ClipboardHistoryItems
                WHERE Id = $itemId;
                """;
            insert.Parameters.AddWithValue("$sectionId", sectionId);
            insert.Parameters.AddWithValue("$itemId", historyItemId);
            insert.Parameters.AddWithValue("$updatedAt", DateTimeOffset.Now.ToString("O"));
            if (insert.ExecuteNonQuery() == 0)
            {
                return PinClipboardItemResult.HistoryItemNotFound;
            }
        }

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM ClipboardHistoryItems WHERE Id = $id;";
            delete.Parameters.AddWithValue("$id", historyItemId);
            delete.ExecuteNonQuery();
        }

        transaction.Commit();
        return PinClipboardItemResult.Success;
    }

    public bool Unpin(long pinnedItemId)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO ClipboardHistoryItems
                    (Kind, CreatedAt, UpdatedAt, Preview, ContentHash, TextContent, ImageBytes, IsEncrypted)
                SELECT Kind, CreatedAt, $updatedAt, Preview, ContentHash, TextContent, ImageBytes, IsEncrypted
                FROM PinnedClipboardItems
                WHERE Id = $itemId
                ON CONFLICT(ContentHash) DO UPDATE SET
                    UpdatedAt = excluded.UpdatedAt,
                    Preview = excluded.Preview,
                    TextContent = excluded.TextContent,
                    ImageBytes = excluded.ImageBytes,
                    IsEncrypted = excluded.IsEncrypted;
                """;
            insert.Parameters.AddWithValue("$itemId", pinnedItemId);
            insert.Parameters.AddWithValue("$updatedAt", DateTimeOffset.Now.ToString("O"));
            if (insert.ExecuteNonQuery() == 0)
            {
                return false;
            }
        }

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM PinnedClipboardItems WHERE Id = $id;";
            delete.Parameters.AddWithValue("$id", pinnedItemId);
            delete.ExecuteNonQuery();
        }

        TrimHistory(connection, transaction);
        transaction.Commit();
        return true;
    }

    public void Delete(long id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ClipboardHistoryItems WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void DeletePinned(long id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM PinnedClipboardItems WHERE Id = $id;";
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
        var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False;Foreign Keys=True");
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
            items.Add(new ClipboardHistoryItem
            {
                Id = reader.GetInt64(0),
                Kind = kind,
                CreatedAt = DateTimeOffset.Parse(reader.GetString(2)),
                UpdatedAt = DateTimeOffset.Parse(reader.GetString(3)),
                Preview = _securityService.UnprotectString(reader.GetString(4)),
                ContentHash = reader.GetString(5),
                TextContent = reader.IsDBNull(6) ? null : _securityService.UnprotectString(reader.GetString(6)),
                ImageBytes = reader.IsDBNull(7) ? null : _securityService.UnprotectBytes(reader.GetFieldValue<byte[]>(7)),
            });
        }

        return items;
    }

    private static IReadOnlyList<ClipboardHistoryItem> SearchItems(
        IReadOnlyList<ClipboardHistoryItem> items,
        string query,
        int limit)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return items.Take(limit).ToList();
        }

        var normalizedQuery = query.Trim();
        return items
            .Where(item => item is { Kind: ClipboardHistoryKind.Text, TextContent: not null }
                && item.TextContent.Contains(normalizedQuery, StringComparison.CurrentCultureIgnoreCase))
            .Take(limit)
            .ToList();
    }

    private void Initialize()
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
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

                CREATE TABLE IF NOT EXISTS ClipboardSections (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                    ItemLimit INTEGER NOT NULL CHECK (ItemLimit BETWEEN 1 AND 100),
                    SortOrder INTEGER NOT NULL
                );

                CREATE TABLE IF NOT EXISTS PinnedClipboardItems (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SectionId INTEGER NOT NULL REFERENCES ClipboardSections(Id) ON DELETE CASCADE,
                    Kind TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    Preview TEXT NOT NULL,
                    ContentHash TEXT NOT NULL UNIQUE,
                    TextContent BLOB NULL,
                    ImageBytes BLOB NULL,
                    IsEncrypted INTEGER NOT NULL DEFAULT 1
                );

                CREATE INDEX IF NOT EXISTS IX_PinnedClipboardItems_Section_UpdatedAt
                ON PinnedClipboardItems (SectionId, UpdatedAt DESC);

                CREATE TABLE IF NOT EXISTS ClipboardHistoryMigrations (
                    Name TEXT PRIMARY KEY,
                    AppliedAt TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        using (var migrationCheck = connection.CreateCommand())
        {
            migrationCheck.Transaction = transaction;
            migrationCheck.CommandText = "SELECT EXISTS(SELECT 1 FROM ClipboardHistoryMigrations WHERE Name = $name);";
            migrationCheck.Parameters.AddWithValue("$name", DefaultSectionMigration);
            if (Convert.ToInt32(migrationCheck.ExecuteScalar()) == 0)
            {
                using var seed = connection.CreateCommand();
                seed.Transaction = transaction;
                seed.CommandText = """
                    INSERT INTO ClipboardSections (Name, ItemLimit, SortOrder)
                    VALUES ('Pinned', 100, 0);
                    INSERT INTO ClipboardHistoryMigrations (Name, AppliedAt)
                    VALUES ($name, $appliedAt);
                    """;
                seed.Parameters.AddWithValue("$name", DefaultSectionMigration);
                seed.Parameters.AddWithValue("$appliedAt", DateTimeOffset.Now.ToString("O"));
                seed.ExecuteNonQuery();
            }
        }

        transaction.Commit();

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

    private static HashSet<long> GetExistingSectionIds(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Id FROM ClipboardSections;";
        using var reader = command.ExecuteReader();
        var ids = new HashSet<long>();
        while (reader.Read())
        {
            ids.Add(reader.GetInt64(0));
        }

        return ids;
    }

    private static void ValidateSections(IReadOnlyList<ClipboardSectionDraft> sections)
    {
        if (sections.Count > MaxSections)
        {
            throw new InvalidOperationException(LocalizationService.T("Validation.TooManySections"));
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in sections)
        {
            var name = section.Name.Trim();
            if (name.Length == 0 || name.Length > MaxSectionNameLength)
            {
                throw new InvalidOperationException(LocalizationService.T("Validation.SectionName"));
            }

            if (!names.Add(name))
            {
                throw new InvalidOperationException(LocalizationService.T("Validation.DuplicateSectionName"));
            }

            if (section.ItemLimit is < 1 or > MaxSectionItems)
            {
                throw new InvalidOperationException(LocalizationService.T("Validation.SectionLimit"));
            }
        }
    }

    private static void TrimHistory(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM ClipboardHistoryItems
            WHERE Id NOT IN (
                SELECT Id
                FROM ClipboardHistoryItems
                ORDER BY UpdatedAt DESC
                LIMIT $limit
            );
            """;
        command.Parameters.AddWithValue("$limit", MaxHistoryItems);
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
