using System.Text.RegularExpressions;

namespace Sciencetopia.Utils;

public static class ChinaAccessFilter
{
    // A conservative list of well-known blocked domains in Mainland China
    private static readonly string[] BlockedSuffixes = new[]
    {
        ".youtube.com", "youtube.com", "youtu.be",
        ".google.com", "google.com", ".gstatic.com", ".googleapis.com", "blogger.com",
        "twitter.com", ".twitter.com", "x.com", "t.co",
        "facebook.com", ".facebook.com", "instagram.com", ".instagram.com",
        "reddit.com", ".reddit.com",
        "medium.com",
        "twitch.tv", ".twitch.tv", "vimeo.com",
        "pinterest.com",
        "telegram.org", ".telegram.org", "t.me",
        "discord.com", ".discord.com",
        "dropbox.com", ".dropbox.com",
        "drive.google.com", "docs.google.com", "sites.google.com",
    };

    public static bool IsAccessibleInChina(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return true;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            // attempt to prepend scheme if missing
            if (Uri.TryCreate("https://" + url, UriKind.Absolute, out uri) == false)
                return true; // don't block unknown
        }

        var host = uri.Host.ToLowerInvariant();
        foreach (var suffix in BlockedSuffixes)
        {
            var s = suffix.ToLowerInvariant();
            if (host == s || host.EndsWith(s)) return false;
        }
        return true;
    }

    public static IEnumerable<ResourceDTO> FilterResourcesForChina(IEnumerable<ResourceDTO> resources)
    {
        foreach (var r in resources)
        {
            if (IsAccessibleInChina(r?.Link)) yield return r;
        }
    }
}

