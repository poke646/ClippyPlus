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
    public void LightweightReads_DeferImageBytesAndRespectSectionScope()
    {
        var (store, _) = CreateStore();
        var section = Assert.Single(store.GetSections());
        byte[] imageBytes = [1, 2, 3, 4];
        var text = new string('a', 250) + "searchable text";
        store.Upsert(ClipboardHistoryEntryFactory.FromImageBytes(imageBytes)!);
        store.Upsert(ClipboardHistoryEntryFactory.FromText(text)!);

        var recent = store.GetRecent(100, includeImageBytes: false);
        var image = recent.Single(item => item.IsImage);
        Assert.Equal(text, recent.Single(item => item.IsText).TextContent);
        Assert.Null(image.ImageBytes);
        Assert.Equal(imageBytes, store.GetImageBytes(image.Id, null, image.ContentHash));
        Assert.Null(store.GetImageBytes(image.Id, null, "different hash"));

        Assert.Equal(PinClipboardItemResult.Success, store.Pin(image.Id, section.Id));
        var pinnedImage = Assert.Single(store.GetSectionItems(section.Id, 100, includeImageBytes: false));
        Assert.Null(pinnedImage.ImageBytes);
        Assert.Equal(imageBytes, store.GetImageBytes(pinnedImage.Id, section.Id, pinnedImage.ContentHash));
        Assert.Null(store.GetImageBytes(pinnedImage.Id, section.Id + 1, pinnedImage.ContentHash));
        Assert.Null(store.GetImageBytes(pinnedImage.Id, null, pinnedImage.ContentHash));
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
    public void Initialize_CreatesDefaultSectionOnlyOnce()
    {
        var databasePath = CreateDatabasePath();
        var security = new ClipboardSecurityService(databasePath);
        security.SetPassword("correct horse battery staple");
        var store = new ClipboardHistoryStore(databasePath, security);

        var section = Assert.Single(store.GetSections());
        Assert.Equal("Pinned", section.Name);
        Assert.Equal(100, section.ItemLimit);

        store.ApplySections([]);
        var reopened = new ClipboardHistoryStore(databasePath, security);

        Assert.Empty(reopened.GetSections());
    }

    [Fact]
    public void ApplySections_AddsRenamesAndValidatesSections()
    {
        var (store, _) = CreateStore();
        var pinned = Assert.Single(store.GetSections());

        store.ApplySections(
        [
            new ClipboardSectionDraft(pinned.Id, "Work", 25),
            new ClipboardSectionDraft(null, "Personal", 10),
        ]);

        var sections = store.GetSections();
        Assert.Equal(["Work", "Personal"], sections.Select(section => section.Name));
        Assert.Equal([25, 10], sections.Select(section => section.ItemLimit));
        Assert.Throws<InvalidOperationException>(() => store.ApplySections(
        [
            new ClipboardSectionDraft(sections[0].Id, "Same", 10),
            new ClipboardSectionDraft(sections[1].Id, "same", 10),
        ]));
        Assert.Throws<InvalidOperationException>(() => store.ApplySections(
            Enumerable.Range(1, 11)
                .Select(index => new ClipboardSectionDraft(null, $"Section {index}", 10))
                .ToList()));
    }

    [Fact]
    public void PinAndUnpin_MoveItemBetweenIndependentLists()
    {
        var (store, _) = CreateStore();
        var section = Assert.Single(store.GetSections());
        store.Upsert(ClipboardHistoryEntryFactory.FromText("move me")!);
        var historyItem = Assert.Single(store.GetRecent(100));

        var result = store.Pin(historyItem.Id, section.Id);

        Assert.Equal(PinClipboardItemResult.Success, result);
        Assert.Empty(store.GetRecent(100));
        var pinnedItem = Assert.Single(store.GetSectionItems(section.Id, 100));
        Assert.Equal("move me", pinnedItem.TextContent);

        Assert.True(store.Unpin(pinnedItem.Id));
        Assert.Empty(store.GetSectionItems(section.Id, 100));
        Assert.Equal("move me", Assert.Single(store.GetRecent(100)).TextContent);
    }

    [Fact]
    public void Pin_RefusesFullSectionWithoutRemovingHistoryItem()
    {
        var (store, _) = CreateStore();
        var original = Assert.Single(store.GetSections());
        store.ApplySections([new ClipboardSectionDraft(original.Id, original.Name, 1)]);
        store.Upsert(ClipboardHistoryEntryFactory.FromText("first")!);
        var first = Assert.Single(store.GetRecent(100));
        Assert.Equal(PinClipboardItemResult.Success, store.Pin(first.Id, original.Id));
        store.Upsert(ClipboardHistoryEntryFactory.FromText("second")!);
        var second = Assert.Single(store.GetRecent(100));

        var result = store.Pin(second.Id, original.Id);

        Assert.Equal(PinClipboardItemResult.SectionFull, result);
        Assert.Equal("second", Assert.Single(store.GetRecent(100)).TextContent);
        Assert.Equal("first", Assert.Single(store.GetSectionItems(original.Id, 100)).TextContent);
    }

    [Fact]
    public void Upsert_DoesNotDuplicateContentAlreadyPinned()
    {
        var (store, _) = CreateStore();
        var section = Assert.Single(store.GetSections());
        var entry = ClipboardHistoryEntryFactory.FromText("stay pinned")!;
        store.Upsert(entry);
        store.Pin(Assert.Single(store.GetRecent(100)).Id, section.Id);

        store.Upsert(entry);

        Assert.Empty(store.GetRecent(100));
        Assert.Single(store.GetSectionItems(section.Id, 100));
    }

    [Fact]
    public void SearchAndDeletePinned_AreScopedToSelectedSection()
    {
        var (store, _) = CreateStore();
        var firstSection = Assert.Single(store.GetSections());
        store.ApplySections(
        [
            new ClipboardSectionDraft(firstSection.Id, "First", 10),
            new ClipboardSectionDraft(null, "Second", 10),
        ]);
        var sections = store.GetSections();
        store.Upsert(ClipboardHistoryEntryFactory.FromText("alpha pinned")!);
        store.Pin(Assert.Single(store.GetRecent(100)).Id, sections[0].Id);
        store.Upsert(ClipboardHistoryEntryFactory.FromText("beta pinned")!);
        store.Pin(Assert.Single(store.GetRecent(100)).Id, sections[1].Id);

        var result = Assert.Single(store.SearchSectionText(sections[0].Id, "ALPHA", 100));
        Assert.Empty(store.SearchSectionText(sections[1].Id, "ALPHA", 100));

        store.DeletePinned(result.Id);

        Assert.Empty(store.GetSectionItems(sections[0].Id, 100));
        Assert.Empty(store.GetRecent(100));
        Assert.Equal("beta pinned", Assert.Single(store.GetSectionItems(sections[1].Id, 100)).TextContent);
    }

    [Fact]
    public void ApplySections_RejectsLimitBelowCurrentItemCount()
    {
        var (store, _) = CreateStore();
        var section = Assert.Single(store.GetSections());
        store.ApplySections([new ClipboardSectionDraft(section.Id, section.Name, 2)]);
        store.Upsert(ClipboardHistoryEntryFactory.FromText("first")!);
        store.Pin(Assert.Single(store.GetRecent(100)).Id, section.Id);
        store.Upsert(ClipboardHistoryEntryFactory.FromText("second")!);
        store.Pin(Assert.Single(store.GetRecent(100)).Id, section.Id);

        Assert.Throws<InvalidOperationException>(() =>
            store.ApplySections([new ClipboardSectionDraft(section.Id, section.Name, 1)]));
        Assert.Equal(2, store.GetSectionItems(section.Id, 100).Count);
    }

    [Fact]
    public void DeleteSection_CascadesEncryptedPinnedItems()
    {
        var databasePath = CreateDatabasePath();
        var security = new ClipboardSecurityService(databasePath);
        security.SetPassword("correct horse battery staple");
        var store = new ClipboardHistoryStore(databasePath, security);
        var section = Assert.Single(store.GetSections());
        store.Upsert(ClipboardHistoryEntryFactory.FromText("secret pinned value")!);
        store.Pin(Assert.Single(store.GetRecent(100)).Id, section.Id);

        var databaseBytes = File.ReadAllBytes(databasePath);
        Assert.DoesNotContain(
            "secret pinned value",
            System.Text.Encoding.UTF8.GetString(databaseBytes));

        store.ApplySections([]);

        Assert.Empty(store.GetSections());
        Assert.Empty(store.GetSectionItems(section.Id, 100));
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

    [Fact]
    public void PasswordProtection_PreservesHistoryAcrossDisableAndEnable()
    {
        var path = CreateDatabasePath();
        var security = new ClipboardSecurityService(path);
        security.SetPassword("original password");
        var store = new ClipboardHistoryStore(path, security);
        store.Upsert(ClipboardHistoryEntryFactory.FromText("retained secret")!);
        var protectedImage = security.ProtectBytes([1, 2, 3, 4]);
        var hash = security.ComputeProtectedContentHash("stable hash");

        Assert.False(security.SetPasswordProtection("wrong password", null));
        Assert.True(security.IsPasswordEnabled);
        Assert.True(security.SetPasswordProtection("original password", null));
        var automaticSession = new ClipboardSecurityService(path);
        Assert.False(automaticSession.IsPasswordEnabled);
        Assert.True(automaticSession.IsUnlocked);
        Assert.Single(new ClipboardHistoryStore(path, automaticSession).GetRecent(100));
        Assert.False(automaticSession.SetPasswordProtection("wrong password", "replacement password"));
        Assert.Throws<ArgumentException>(() => automaticSession.SetPasswordProtection("original password", "short"));
        Assert.False(new ClipboardSecurityService(path).IsPasswordEnabled);
        Assert.True(automaticSession.SetPasswordProtection("original password", "replacement password"));

        var lockedSession = new ClipboardSecurityService(path);
        Assert.True(lockedSession.IsPasswordEnabled);
        Assert.False(lockedSession.IsUnlocked);
        Assert.False(lockedSession.TryUnlock("original password"));
        Assert.True(lockedSession.TryUnlock("replacement password"));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, lockedSession.UnprotectBytes(protectedImage));
        Assert.Equal(hash, lockedSession.ComputeProtectedContentHash("stable hash"));
        var reopenedStore = new ClipboardHistoryStore(path, lockedSession);
        Assert.Single(reopenedStore.GetRecent(100));
        reopenedStore.Upsert(ClipboardHistoryEntryFactory.FromText("retained secret")!);
        Assert.Single(reopenedStore.GetRecent(100));

        // Repeat with wrapped keys, rather than the original password-derived keys.
        Assert.True(lockedSession.SetPasswordProtection("replacement password", null));
        var secondAutomaticSession = new ClipboardSecurityService(path);
        Assert.True(secondAutomaticSession.IsUnlocked);
        Assert.True(secondAutomaticSession.SetPasswordProtection("replacement password", "third password"));
        var finalSession = new ClipboardSecurityService(path);
        Assert.False(finalSession.TryUnlock("replacement password"));
        Assert.True(finalSession.TryUnlock("third password"));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, finalSession.UnprotectBytes(protectedImage));
        Assert.Single(new ClipboardHistoryStore(path, finalSession).GetRecent(100));
    }

    [Fact]
    public void SetPassword_RejectsReplacingExistingKeys()
    {
        var security = new ClipboardSecurityService(CreateDatabasePath());
        security.SetPassword("original password");
        var value = security.ProtectString("preserved");
        Assert.Throws<InvalidOperationException>(() => security.SetPassword("replacement password"));
        Assert.Equal("preserved", security.UnprotectString(value));
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
