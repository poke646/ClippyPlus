using System.Diagnostics;

namespace ClippyPlus.App.Services;

public static class ExternalContentService
{
    public static bool CanOpen(string? text)
    {
        return TryGetWebUri(text, out _) || TryGetExistingPath(text, out _);
    }

    public static bool TryOpen(string? text)
    {
        string? target = null;
        if (TryGetWebUri(text, out var uri))
        {
            target = uri!.AbsoluteUri;
        }
        else if (TryGetExistingPath(text, out var path))
        {
            target = path;
        }

        if (target is null)
        {
            return false;
        }

        try
        {
            return Process.Start(new ProcessStartInfo(target)
            {
                UseShellExecute = true,
            }) is not null;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryGetWebUri(string? text, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(text)
            || !Uri.TryCreate(text.Trim(), UriKind.Absolute, out var candidate)
            || (candidate.Scheme != Uri.UriSchemeHttp && candidate.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(candidate.Host))
        {
            return false;
        }

        uri = candidate;
        return true;
    }

    public static bool TryGetExistingPath(string? text, out string? path)
    {
        path = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var candidate = text.Trim();
        if (candidate.Length >= 2 && candidate[0] == '"' && candidate[^1] == '"')
        {
            candidate = candidate[1..^1];
        }

        if (!Path.IsPathFullyQualified(candidate)
            || (!File.Exists(candidate) && !Directory.Exists(candidate)))
        {
            return false;
        }

        path = Path.GetFullPath(candidate);
        return true;
    }
}
