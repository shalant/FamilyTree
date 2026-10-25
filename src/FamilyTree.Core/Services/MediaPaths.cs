namespace FamilyTree.Core.Services;

/// <summary>
/// Turns a stored blob URL into the app path that serves it (S8, docs/security.md,
/// 2026-10-25). Media used to be shown straight from Azure Blob Storage, so the container
/// had to allow anonymous reads and anyone holding a URL could fetch the photo, even after
/// the person was deleted. Read DTOs now carry <c>/media/{blobName}</c>, which
/// <c>IMediaAccessService</c> serves only to the person's own family; the database keeps
/// the blob URL. URLs that aren't Azure blobs (or Azurite) are left alone.
/// </summary>
public static class MediaPaths
{
    public const string Prefix = "/media/";

    /// <summary>The app path for a stored media URL, or the value unchanged if it isn't a blob.</summary>
    public static string? ToAppPath(string? storedUrl)
    {
        var name = BlobName(storedUrl);
        return name == null ? storedUrl : Prefix + Uri.EscapeDataString(name);
    }

    /// <summary>
    /// The blob name in a stored blob URL or an app path (<c>/media/x.jpg</c>), or null if
    /// the value is neither, or the name isn't one the app could have generated.
    /// </summary>
    public static string? BlobName(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        string candidate;
        if (url.StartsWith(Prefix, StringComparison.Ordinal))
        {
            candidate = Uri.UnescapeDataString(url[Prefix.Length..]);
        }
        else if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
                 && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                 && IsBlobHost(uri))
        {
            candidate = Uri.UnescapeDataString(uri.AbsolutePath.TrimEnd('/').Split('/').Last());
        }
        else
        {
            return null;
        }

        return IsSafeName(candidate) ? candidate : null;
    }

    private static bool IsBlobHost(Uri uri) =>
        uri.Host.EndsWith(".blob.core.windows.net", StringComparison.OrdinalIgnoreCase)
        // Azurite, the local emulator BlobStorageService falls back to.
        || (uri.IsLoopback && uri.Port == 10000);

    // MediumService names blobs "{type}-{guid}{extension}"; nothing else is ever served.
    private static bool IsSafeName(string name) =>
        name.Length is > 0 and <= 200
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
        && !name.StartsWith('.');
}
