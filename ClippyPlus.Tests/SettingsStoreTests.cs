using ClippyPlus.App.Services;

namespace ClippyPlus.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"ClippyPlusSettingsTests-{Guid.NewGuid():N}");

    [Fact]
    public void Load_ReturnsDefaultsWhenFileIsMissing()
    {
        var store = new SettingsStore(Path.Combine(_tempDirectory, "settings.json"));

        var settings = store.Load();

        Assert.False(string.IsNullOrWhiteSpace(settings.CaptureHotkey));
        Assert.Equal("pl", settings.LanguageCode);
        Assert.Equal("Ctrl+Alt+Shift+S", settings.EditCaptureHotkey);
        Assert.False(string.IsNullOrWhiteSpace(settings.ClipboardHistoryHotkey));
        Assert.True(settings.HideSharingEnabled);
    }

    [Fact]
    public void Save_AndLoad_RoundTripsSettings()
    {
        var store = new SettingsStore(Path.Combine(_tempDirectory, "settings.json"));
        var settings = new AppSettings
        {
            CaptureHotkey = "Ctrl+Shift+D",
            LanguageCode = "de",
            EditCaptureHotkey = "Ctrl+Alt+Shift+S",
            ClipboardHistoryHotkey = "Ctrl+Shift+V",
            HideSharingEnabled = false,
        };

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal("Ctrl+Shift+D", loaded.CaptureHotkey);
        Assert.Equal("de", loaded.LanguageCode);
        Assert.Equal("Ctrl+Alt+Shift+S", loaded.EditCaptureHotkey);
        Assert.Equal("Ctrl+Shift+V", loaded.ClipboardHistoryHotkey);
        Assert.False(loaded.HideSharingEnabled);
    }

    [Fact]
    public void TryValidate_RejectsDuplicateHotkeys()
    {
        var settings = new AppSettings
        {
            CaptureHotkey = "Ctrl+Shift+D",
            EditCaptureHotkey = "Ctrl+Alt+Shift+S",
            ClipboardHistoryHotkey = "Ctrl+Shift+D",
        };

        var valid = SettingsStore.TryValidate(settings, out _, out _, out _, out var error);

        Assert.False(valid);
        Assert.Contains("takie same", error);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
