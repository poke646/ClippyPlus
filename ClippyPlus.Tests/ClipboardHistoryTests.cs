using ClippyPlus.App.Models;
using ClippyPlus.App.Services;

namespace ClippyPlus.Tests;

public sealed class ClipboardHistoryTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"ClippyPlusTests-{Guid.NewGuid():N}");

    [Fact]
    public void FromText_ReturnsNullForEmptyText()
    {
        Assert.Null(ClipboardHistoryEntryFactory.FromText(""));
        Assert.Null(ClipboardHistoryEntryFactory.FromText("   "));
    }

    [Fact]
    public void FromText_CreatesStableHashAndShortPreview()
    {
        var text = new string('a', 220);

        var first = ClipboardHistoryEntryFactory.FromText(text);
        var second = ClipboardHistoryEntryFactory.FromText(text);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.True(first.Preview.Length < text.Length);
        Assert.EndsWith("...", first.Preview);
    }

    [Fact]
    public void Upsert_AddsNewTextItem()
    {
        var (store, _) = CreateStore();

        store.Upsert(ClipboardHistoryEntryFactory.FromText("hello")!);

        var items = store.GetRecent(100);
        Assert.Single(items);
        Assert.Equal(ClipboardHistoryKind.Text, items[0].Kind);
        Assert.Equal("hello", items[0].TextContent);
    }

    [Fact]
    public void Upsert_AddsNewImageItem()
    {
        var (store, _) = CreateStore();
        var imageBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

        store.Upsert(ClipboardHistoryEntryFactory.FromImageBytes(imageBytes)!);

        var items = store.GetRecent(100);
        Assert.Single(items);
        Assert.Equal(ClipboardHistoryKind.Image, items[0].Kind);
        Assert.Equal(imageBytes, items[0].ImageBytes);
    }


    [Fact]
    public void Upsert_DuplicateTextMovesExistingItemToTop()
    {
        var (store, _) = CreateStore();
        store.Upsert(ClipboardHistoryEntryFactory.FromText("first")!);
        store.Upsert(ClipboardHistoryEntryFactory.FromText("second")!);
        Thread.Sleep(20);

        store.Upsert(ClipboardHistoryEntryFactory.FromText("first")!);

        var items = store.GetRecent(100);
        Assert.Equal(2, items.Count);
        Assert.Equal("first", items[0].TextContent);
        Assert.Equal("second", items[1].TextContent);
    }

    [Fact]
    public void GetRecent_ReturnsNewestItemsFirst()
    {
        var (store, _) = CreateStore();
        store.Upsert(ClipboardHistoryEntryFactory.FromText("old")!);
        Thread.Sleep(20);
        store.Upsert(ClipboardHistoryEntryFactory.FromText("new")!);

        var items = store.GetRecent(100);

        Assert.Equal("new", items[0].TextContent);
        Assert.Equal("old", items[1].TextContent);
    }

    [Fact]
    public void SearchText_ReturnsTextItemsContainingQuery()
    {
        var (store, _) = CreateStore();
        store.Upsert(ClipboardHistoryEntryFactory.FromText("alpha project")!);
        store.Upsert(ClipboardHistoryEntryFactory.FromText("beta note")!);
        var imageBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        store.Upsert(ClipboardHistoryEntryFactory.FromImageBytes(imageBytes)!);

        var items = store.SearchText("PROJECT", 100);

        Assert.Single(items);
        Assert.Equal("alpha project", items[0].TextContent);
    }

    [Fact]
    public void SearchText_EscapesLikeWildcards()
    {
        var (store, _) = CreateStore();
        store.Upsert(ClipboardHistoryEntryFactory.FromText("100% match")!);
        store.Upsert(ClipboardHistoryEntryFactory.FromText("1000 match")!);

        var items = store.SearchText("100%", 100);

        Assert.Single(items);
        Assert.Equal("100% match", items[0].TextContent);
    }

    [Fact]
    public void Upsert_TrimsHistoryToOneHundredItems()
    {
        var (store, _) = CreateStore();

        for (var i = 0; i < 105; i++)
        {
            store.Upsert(ClipboardHistoryEntryFactory.FromText($"item-{i:000}")!);
        }

        var items = store.GetRecent(200);
        Assert.Equal(100, items.Count);
        Assert.DoesNotContain(items, item => item.TextContent == "item-000");
        Assert.Equal("item-104", items[0].TextContent);
    }

    [Fact]
    public void Delete_RemovesSingleHistoryItem()
    {
        var (store, _) = CreateStore();
        store.Upsert(ClipboardHistoryEntryFactory.FromText("keep")!);
        store.Upsert(ClipboardHistoryEntryFactory.FromText("remove")!);
        var itemToRemove = store.GetRecent(100).Single(item => item.TextContent == "remove");

        store.Delete(itemToRemove.Id);

        var items = store.GetRecent(100);
        Assert.Single(items);
        Assert.Equal("keep", items[0].TextContent);
    }

    [Fact]
    public void DeleteAll_RemovesEveryHistoryItem()
    {
        var (store, _) = CreateStore();
        store.Upsert(ClipboardHistoryEntryFactory.FromText("first")!);
        store.Upsert(ClipboardHistoryEntryFactory.FromText("second")!);

        store.DeleteAll();

        Assert.Empty(store.GetRecent(100));
    }

    [Fact]
    public void Upsert_DoesNotPersistPlainTextContent()
    {
        var databasePath = CreateDatabasePath();
        var security = new ClipboardSecurityService(databasePath);
        security.SetPassword("correct horse battery staple");
        var store = new ClipboardHistoryStore(databasePath, security);

        store.Upsert(ClipboardHistoryEntryFactory.FromText("top secret clipboard value")!);

        var databaseBytes = File.ReadAllBytes(databasePath);
        var databaseText = System.Text.Encoding.UTF8.GetString(databaseBytes);
        Assert.DoesNotContain("top secret clipboard value", databaseText);
    }

    [Fact]
    public void TryUnlock_RejectsWrongPassword()
    {
        var databasePath = CreateDatabasePath();
        var security = new ClipboardSecurityService(databasePath);
        security.SetPassword("correct horse battery staple");
        var nextSessionSecurity = new ClipboardSecurityService(databasePath);

        Assert.False(nextSessionSecurity.TryUnlock("wrong password"));
        Assert.False(nextSessionSecurity.IsUnlocked);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private (ClipboardHistoryStore Store, ClipboardSecurityService Security) CreateStore()
    {
        var databasePath = CreateDatabasePath();
        var security = new ClipboardSecurityService(databasePath);
        security.SetPassword("correct horse battery staple");
        return (new ClipboardHistoryStore(databasePath, security), security);
    }

    private string CreateDatabasePath()
    {
        Directory.CreateDirectory(_tempDirectory);
        return Path.Combine(_tempDirectory, $"{Guid.NewGuid():N}.db");
    }
}
