using FamilyTree.Core.Data;
using FamilyTree.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FamilyTree.Core.Services;

/// <summary>A media file ready to stream, and how the browser may treat it.</summary>
/// <param name="Inline">False for anything that isn't one of the four allowed image types:
/// served as an attachment of type application/octet-stream, never rendered.</param>
public sealed record MediaContent(Stream Content, string ContentType, bool Inline);

public interface IMediaAccessService
{
    /// <summary>
    /// A media file by blob name, if it belongs to a person the caller may see: a media
    /// row of theirs, or their profile photo. Anyone else gets "not found".
    /// </summary>
    Task<ServiceResponse<MediaContent>> OpenAsync(string blobName, CancellationToken ct = default);

    /// <summary>
    /// The profile photo of a story invite's subject, for the invite email and the
    /// signed-out respond page. The invite token is the credential; it works until the
    /// invite expires.
    /// </summary>
    Task<ServiceResponse<MediaContent>> OpenInvitePhotoAsync(string token, CancellationToken ct = default);
}

/// <summary>
/// Serves media through the app instead of public blob URLs (S8, docs/security.md,
/// 2026-10-25), so the container can be private. See <see cref="MediaPaths"/>.
/// </summary>
public class MediaAccessService(
    IDbContextFactory<AppDbContext> dbFactory,
    IBlobStorageService blobStorage,
    ILogger<MediaAccessService> logger,
    ICurrentUserService currentUser) : IMediaAccessService
{
    private const string NotFound = "Not found.";

    // Same visibility rule as MediumService and PersonService: super-user sees all, anyone
    // else only their own family, no FamilyId sees nothing. Soft-deleted people and media
    // drop out through the global query filters.
    public async Task<ServiceResponse<MediaContent>> OpenAsync(string blobName, CancellationToken ct = default)
    {
        var name = MediaPaths.BlobName(MediaPaths.Prefix + blobName);
        if (name == null) return ServiceResponse<MediaContent>.Fail(NotFound);

        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);
            var familyId = currentUser.FamilyId;
            var isSuperUser = currentUser.IsSuperUser;
            var visible = ctx.People
                .Where(p => isSuperUser || (familyId.HasValue && p.FamilyId == familyId));

            var suffix = "/" + name;
            var medium = await ctx.Media
                .Where(m => m.Url.EndsWith(suffix) && visible.Any(p => p.Id == m.PersonId))
                .Select(m => new { m.Url, m.MimeType })
                .FirstOrDefaultAsync(ct);

            string? url = medium?.Url;
            if (url == null)
            {
                url = await visible
                    .Where(p => p.ProfilePhotoUrl != null && p.ProfilePhotoUrl.EndsWith(suffix))
                    .Select(p => p.ProfilePhotoUrl)
                    .FirstOrDefaultAsync(ct);
            }
            if (url == null) return ServiceResponse<MediaContent>.Fail(NotFound);

            return ServiceResponse<MediaContent>.Ok(await DownloadAsync(name, medium?.MimeType, ct));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error opening media {BlobName}", name);
            return ServiceResponse<MediaContent>.Fail(NotFound);
        }
    }

    public async Task<ServiceResponse<MediaContent>> OpenInvitePhotoAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return ServiceResponse<MediaContent>.Fail(NotFound);

        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);
            var now = DateTime.UtcNow;
            // People's query filter hides a deleted subject, so their photo goes with them.
            var photoUrl = await ctx.StoryInvites
                .Where(i => i.Token == token && i.ExpiresAt > now && i.Person != null)
                .Select(i => i.Person!.ProfilePhotoUrl)
                .FirstOrDefaultAsync(ct);

            var name = MediaPaths.BlobName(photoUrl);
            if (name == null) return ServiceResponse<MediaContent>.Fail(NotFound);

            return ServiceResponse<MediaContent>.Ok(await DownloadAsync(name, null, ct));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error opening a story invite's photo");
            return ServiceResponse<MediaContent>.Fail(NotFound);
        }
    }

    private async Task<MediaContent> DownloadAsync(string blobName, string? mimeType, CancellationToken ct)
    {
        var stream = await blobStorage.DownloadAsync(blobName, ct);
        var type = ImageType(mimeType) ?? ImageType(Path.GetExtension(blobName));
        return type == null
            ? new MediaContent(stream, "application/octet-stream", Inline: false)
            : new MediaContent(stream, type, Inline: true);
    }

    // The only types served inline: the four MediumService accepts. Anything else (a file
    // uploaded with no MIME type skips that check) could be HTML or script, which must
    // never render on the app's own origin.
    private static string? ImageType(string? mimeOrExtension) => mimeOrExtension?.ToLowerInvariant() switch
    {
        "image/jpeg" or ".jpg" or ".jpeg" => "image/jpeg",
        "image/png" or ".png" => "image/png",
        "image/webp" or ".webp" => "image/webp",
        "image/gif" or ".gif" => "image/gif",
        _ => null,
    };
}
