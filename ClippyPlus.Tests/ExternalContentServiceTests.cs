using ClippyPlus.App.Services;

namespace ClippyPlus.Tests;

public sealed class ExternalContentServiceTests
{
    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com/path?q=value#part")]
    [InlineData("  https://example.com/path  ")]
    public void TryGetWebUri_AcceptsHttpAndHttpsLinks(string text)
    {
        Assert.True(ExternalContentService.TryGetWebUri(text, out var uri));
        Assert.NotNull(uri);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("example.com")]
    [InlineData("some text https://example.com")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("javascript:alert(1)")]
    public void TryGetWebUri_RejectsNonWebLinks(string? text)
    {
        Assert.False(ExternalContentService.TryGetWebUri(text, out var uri));
        Assert.Null(uri);
    }

    [Fact]
    public void TryGetExistingPath_AcceptsExistingDirectoryAndQuotedFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ClippyPlus-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "example.txt");
        File.WriteAllText(file, "test");

        try
        {
            Assert.True(ExternalContentService.TryGetExistingPath(directory, out var directoryPath));
            Assert.Equal(Path.GetFullPath(directory), directoryPath);
            Assert.True(ExternalContentService.TryGetExistingPath($"\"{file}\"", out var filePath));
            Assert.Equal(Path.GetFullPath(file), filePath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("relative/path.txt")]
    [InlineData("definitely-not-an-existing-path")]
    public void TryGetExistingPath_RejectsInvalidPath(string text)
    {
        Assert.False(ExternalContentService.TryGetExistingPath(text, out var path));
        Assert.Null(path);
    }
}
