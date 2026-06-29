using ClippyPlus.App.Services;

namespace ClippyPlus.Tests;

public sealed class StartupServiceTests
{
    [Fact]
    public void BuildWindowsRunCommand_UsesAppHostWhenProcessIsApplication()
    {
        var command = StartupService.BuildWindowsRunCommand(
            @"C:\Apps\ClippyPlus.App.exe",
            @"C:\Apps\ClippyPlus.App.dll");

        Assert.Equal(@"""C:\Apps\ClippyPlus.App.exe""", command);
    }

    [Fact]
    public void BuildWindowsRunCommand_IncludesAssemblyWhenProcessIsDotnetHost()
    {
        var command = StartupService.BuildWindowsRunCommand(
            @"C:\Program Files\dotnet\dotnet.exe",
            @"C:\Apps\ClippyPlus.App.dll");

        Assert.Equal(@"""C:\Program Files\dotnet\dotnet.exe"" ""C:\Apps\ClippyPlus.App.dll""", command);
    }

    [Fact]
    public void IsWindowsRunCommandForCurrentApp_RejectsBareDotnetHost()
    {
        var enabled = StartupService.IsWindowsRunCommandForCurrentApp(
            @"""C:\Program Files\dotnet\dotnet.exe""",
            @"C:\Program Files\dotnet\dotnet.exe",
            @"C:\Apps\ClippyPlus.App.dll");

        Assert.False(enabled);
    }

    [Fact]
    public void IsWindowsRunCommandForCurrentApp_AcceptsDotnetHostWithAssembly()
    {
        var enabled = StartupService.IsWindowsRunCommandForCurrentApp(
            @"""C:\Program Files\dotnet\dotnet.exe"" ""C:\Apps\ClippyPlus.App.dll""",
            @"C:\Program Files\dotnet\dotnet.exe",
            @"C:\Apps\ClippyPlus.App.dll");

        Assert.True(enabled);
    }

    [Fact]
    public void IsWindowsRunCommandForCurrentApp_AcceptsAppHostWhenCurrentProcessIsDotnetHost()
    {
        var enabled = StartupService.IsWindowsRunCommandForCurrentApp(
            @"""C:\Apps\ClippyPlus.App.exe""",
            @"C:\Program Files\dotnet\dotnet.exe",
            @"C:\Apps\ClippyPlus.App.dll");

        Assert.True(enabled);
    }
}
