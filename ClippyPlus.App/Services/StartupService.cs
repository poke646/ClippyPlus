using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace ClippyPlus.App.Services;

public sealed class StartupService : IStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClippyPlus";
    private const string MacLaunchAgentId = "com.clippyplus";

    public bool IsEnabled
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return GetWindowsEnabled();
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return File.Exists(GetMacLaunchAgentPath());
            }

            return false;
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            SetWindowsEnabled(enabled);
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            SetMacEnabled(enabled);
            return;
        }

        throw new PlatformNotSupportedException("Autostart jest wspierany tylko na Windows i macOS.");
    }

    [SupportedOSPlatform("windows")]
    private static bool GetWindowsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        return key?.GetValue(ValueName) is string command
            && IsWindowsRunCommandForCurrentApp(
                command,
                Environment.ProcessPath,
                Assembly.GetEntryAssembly()?.Location);
    }

    [SupportedOSPlatform("windows")]
    private static void SetWindowsEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, true);

        if (enabled)
        {
            key.SetValue(
                ValueName,
                BuildWindowsRunCommand(Environment.ProcessPath, Assembly.GetEntryAssembly()?.Location));
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }
    }

    internal static string BuildWindowsRunCommand(string? processPath, string? entryAssemblyLocation)
    {
        if (!string.IsNullOrWhiteSpace(processPath) && !IsDotNetHost(processPath))
        {
            return QuoteWindowsArgument(processPath);
        }

        if (!string.IsNullOrWhiteSpace(processPath) && !string.IsNullOrWhiteSpace(entryAssemblyLocation))
        {
            return $"{QuoteWindowsArgument(processPath)} {QuoteWindowsArgument(entryAssemblyLocation)}";
        }

        var appHostPath = GetAppHostPath(entryAssemblyLocation);
        if (!string.IsNullOrWhiteSpace(appHostPath))
        {
            if (File.Exists(appHostPath))
            {
                return QuoteWindowsArgument(appHostPath);
            }
        }

        throw new InvalidOperationException("Nie mozna ustalic sciezki aplikacji do autostartu.");
    }

    internal static bool IsWindowsRunCommandForCurrentApp(
        string? command,
        string? processPath,
        string? entryAssemblyLocation)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(processPath) && IsDotNetHost(processPath))
        {
            return ContainsWindowsPath(command, entryAssemblyLocation)
                || ContainsWindowsPath(command, GetAppHostPath(entryAssemblyLocation));
        }

        return ContainsWindowsPath(command, processPath)
            || ContainsWindowsPath(command, entryAssemblyLocation);
    }

    private static bool IsDotNetHost(string processPath)
    {
        var lastSeparator = processPath.LastIndexOfAny(['\\', '/']);
        var fileName = processPath[(lastSeparator + 1)..];
        return string.Equals(
            Path.GetFileNameWithoutExtension(fileName),
            "dotnet",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsWindowsPath(string command, string? path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && (command.Contains(QuoteWindowsArgument(path), StringComparison.OrdinalIgnoreCase)
                || command.Contains(path, StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetAppHostPath(string? entryAssemblyLocation)
    {
        return string.IsNullOrWhiteSpace(entryAssemblyLocation)
            ? null
            : Path.ChangeExtension(entryAssemblyLocation, ".exe");
    }

    private static string QuoteWindowsArgument(string argument)
    {
        return $"\"{argument}\"";
    }

    private static void SetMacEnabled(bool enabled)
    {
        var path = GetMacLaunchAgentPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (!enabled)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Nie mozna ustalic sciezki aplikacji.");
        var plist = $"""
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>{MacLaunchAgentId}</string>
    <key>ProgramArguments</key>
    <array>
        <string>{SecurityElementEscape(processPath)}</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
</dict>
</plist>
""";
        File.WriteAllText(path, plist);
        TryLaunchCtl("bootstrap", $"gui/{Environment.UserName}", path);
    }

    private static string GetMacLaunchAgentPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "LaunchAgents",
            $"{MacLaunchAgentId}.plist");
    }

    private static string SecurityElementEscape(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }

    private static void TryLaunchCtl(params string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/bin/launchctl",
                UseShellExecute = false,
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            process?.WaitForExit(1000);
        }
        catch
        {
            // The plist is still written; launchctl can be handled by the next login.
        }
    }
}
