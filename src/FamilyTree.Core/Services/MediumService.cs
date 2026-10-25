using FamilyTree.Core.Data;
using FamilyTree.Core.Models;
using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Medium;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace FamilyTree.Core.Services;

public class MediumService(
    IDbContextFactory<AppDbContext> dbFactory,
    IBlobStorageService blobStorage,
    ILogger<MediumService> logger,
    IAuditLogService auditLog,
    ICurrentUserService currentUser) : IMediumService
{
    // Media belong to whoever owns the person. Same rule as PersonService.ScopedPeople:
    // super-user sees all, everyone else only their own family, no FamilyId sees nothing
    // (findings S3/S4/S6, docs/security.md, 2026-10-10).
    private IQueryable<Guid> VisiblePersonIds(AppDbContext ctx)
    {
        var familyId = currentUser.FamilyId;
        var isSuperUser = currentUser.IsSuperUser;
        return ctx.People
            .Where(p => isSuperUser || (familyId.HasValue && p.FamilyId == familyId))
            .Select(p => p.Id);
    }

    // ─────────────────────────────────────────────────────────────
    //  GET ALL
    // ─────────────────────────────────────────────────────────────
    public async Task<ServiceResponse<List<MediumDto>>> GetAllAsync(CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var visible = VisiblePersonIds(ctx);
            var media = await ctx.Media
                .AsNoTracking()
                .Where(m => visible.Contains(m.PersonId))
                .OrderByDescending(m => m.CreatedAt)
                .ToListAsync(ct);

            var dtos = media.Select(MapToDto).ToList();
            return ServiceResponse<List<MediumDto>>.Ok(dtos);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading all media");
            return ServiceResponse<List<MediumDto>>.Fail(
                "An error occurred loading media.");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  GET BY PERSON ID
    // ─────────────────────────────────────────────────────────────
    public async Task<ServiceResponse<List<MediumDto>>> GetByPersonIdAsync(
        Guid personId, CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var visible = VisiblePersonIds(ctx);
            var media = await ctx.Media
                .AsNoTracking()
                .Where(m => m.PersonId == personId && visible.Contains(m.PersonId))
                .OrderByDescending(m => m.CreatedAt)
                .ToListAsync(ct);

            var dtos = media.Select(MapToDto).ToList();
            return ServiceResponse<List<MediumDto>>.Ok(dtos);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading media for person {PersonId}", personId);
            return ServiceResponse<List<MediumDto>>.Fail(
                "An error occurred loading media for this person.");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  GET BY ID
    // ─────────────────────────────────────────────────────────────
    public async Task<ServiceResponse<MediumDto>> GetByIdAsync(
        Guid id, CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var familyId = currentUser.FamilyId;
            var isSuperUser = currentUser.IsSuperUser;

            // TODO: Remove this guard when full family scoping is implemented via UserFamily membership.
            var medium = await ctx.Media
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == id &&
                    (isSuperUser || (familyId.HasValue && ctx.People.Any(p => p.Id == m.PersonId && p.FamilyId == familyId))), ct);

            if (medium is null)
                return ServiceResponse<MediumDto>.Fail($"Medium {id} not found.");

            return ServiceResponse<MediumDto>.Ok(MapToDto(medium));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading medium {Id}", id);
            return ServiceResponse<MediumDto>.Fail(
                "An error occurred loading this media.");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  CREATE
    // ─────────────────────────────────────────────────────────────
    public async Task<ServiceResponse<MediumDto>> CreateAsync(
        MediumUpsertDto dto, Stream fileStream, CancellationToken ct = default)
    {
        try
        {
            var dtoValidation = ValidationHelper.ValidateDtoNonGeneric(dto);
            if (!dtoValidation.Success)
                return ServiceResponse<MediumDto>.Fail(dtoValidation.Message);

            var fileError = ValidateMediumFile(fileStream, dto);
            if (fileError != null)
                return ServiceResponse<MediumDto>.Fail(fileError);

            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            // Scoped: another family's person (or any person, for an anonymous caller)
            // gets the same "not found" as a GUID that doesn't exist.
            var personExists = await VisiblePersonIds(ctx).AnyAsync(id => id == dto.PersonId, ct);
            if (!personExists)
                return ServiceResponse<MediumDto>.Fail($"Person {dto.PersonId} not found.");

            var formatError = await ValidateDetectedFormatAsync(fileStream, ct);
            if (formatError != null)
                return ServiceResponse<MediumDto>.Fail(formatError);

            var fileName = $"{dto.Type}-{Guid.NewGuid()}{Path.GetExtension(dto.FileName)}";
            var uploadStream = await ResizeIfOversizedAsync(fileStream, dto.MimeType, ct);
            var url = await blobStorage.UploadAsync(uploadStream, fileName, dto.MimeType, ct);
            // Only dispose if ResizeIfOversizedAsync actually created a new stream — the
            // original fileStream is caller-owned.
            if (!ReferenceEquals(uploadStream, fileStream))
                await uploadStream.DisposeAsync();

            var userId = currentUser.UserId;
            var medium = new Medium
            {
                Id = Guid.NewGuid(),
                PersonId = dto.PersonId,
                FileName = dto.FileName,
                Caption = dto.Caption,
                MimeType = dto.MimeType,
                Type = dto.Type,
                Url = url,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userId,
            };

            ctx.Media.Add(medium);
            await ctx.SaveChangesAsync(ct);

            _ = auditLog.LogAsync("Create", "Medium", medium.Id, userId: userId);

            return ServiceResponse<MediumDto>.Ok(MapToDto(medium));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating medium with file upload");
            return ServiceResponse<MediumDto>.Fail(
                "An error occurred uploading and creating this media.");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  UPDATE
    // ─────────────────────────────────────────────────────────────
    public async Task<ServiceResponse<MediumDto>> UpdateAsync(
        Guid id, MediumUpsertDto dto, CancellationToken ct = default)
    {
        try
        {
            var dtoValidation = ValidationHelper.ValidateDtoNonGeneric(dto);
            if (!dtoValidation.Success)
                return ServiceResponse<MediumDto>.Fail(dtoValidation.Message);

            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var familyId = currentUser.FamilyId;
            var isSuperUser = currentUser.IsSuperUser;

            // TODO: Remove this guard when full family scoping is implemented via UserFamily membership.
            var medium = await ctx.Media.FirstOrDefaultAsync(m => m.Id == id &&
                (isSuperUser || (familyId.HasValue && ctx.People.Any(p => p.Id == m.PersonId && p.FamilyId == familyId))), ct);
            if (medium is null)
                return ServiceResponse<MediumDto>.Fail($"Medium {id} not found.");

            var userId = currentUser.UserId;
            medium.Caption = dto.Caption;
            medium.Type = dto.Type;
            medium.UpdatedAt = DateTime.UtcNow;
            medium.UpdatedBy = userId;

            ctx.Media.Update(medium);
            await ctx.SaveChangesAsync(ct);

            _ = auditLog.LogAsync("Update", "Medium", id, userId: userId);

            return ServiceResponse<MediumDto>.Ok(MapToDto(medium));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating medium {Id}", id);
            return ServiceResponse<MediumDto>.Fail(
                "An error occurred updating this media.");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  DELETE
    // ─────────────────────────────────────────────────────────────
    public async Task<ServiceResponse> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var familyId = currentUser.FamilyId;
            var isSuperUser = currentUser.IsSuperUser;

            // TODO: Remove this guard when full family scoping is implemented via UserFamily membership.
            var medium = await ctx.Media.FirstOrDefaultAsync(m => m.Id == id &&
                (isSuperUser || (familyId.HasValue && ctx.People.Any(p => p.Id == m.PersonId && p.FamilyId == familyId))), ct);
            if (medium is null)
                return ServiceResponse.Fail($"Medium {id} not found.");

            _ = await blobStorage.DeleteAsync(medium.Url, ct);

            ctx.Media.Remove(medium);
            await ctx.SaveChangesAsync(ct);

            _ = auditLog.LogAsync("Delete", "Medium", id, userId: currentUser.UserId);

            return ServiceResponse.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting medium {Id}", id);
            return ServiceResponse.Fail(
                "An error occurred deleting this media.");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  IMAGE RESIZE (backstop for whatever the client didn't already shrink)
    // ─────────────────────────────────────────────────────────────
    // Both real upload paths (PersonForm's profile photo, PersonMedia's gallery) already
    // downscale to 1400px client-side via ftCompressImage before the bytes ever leave the
    // browser — so this almost never fires on the common path. It exists for the fallback:
    // when client-side compression fails (canvas can't decode the file, e.g. some HEIC
    // photos in browsers without native HEIC decoding), both callers silently fall back to
    // uploading the original, unresized file. Without a server-side backstop, that fallback
    // is a real path to a raw, full-resolution phone photo landing in blob storage.
    private const int MaxImageDimension = 2000;

    private static async Task<Stream> ResizeIfOversizedAsync(
        Stream fileStream, string? mimeType, CancellationToken ct)
    {
        if (mimeType is null || !mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) || !fileStream.CanSeek)
            return fileStream;

        try
        {
            fileStream.Position = 0;
            var format = await Image.DetectFormatAsync(fileStream, ct);
            fileStream.Position = 0;
            if (format is null || !DecodableFormats.Contains(format))
                return fileStream;

            using var image = await Image.LoadAsync(fileStream, ct);
            if (image.Width <= MaxImageDimension && image.Height <= MaxImageDimension)
            {
                fileStream.Position = 0;
                return fileStream;
            }

            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(MaxImageDimension, MaxImageDimension),
            }));

            var encoder = image.Configuration.ImageFormatsManager.GetEncoder(format);
            var output = new MemoryStream();
            await image.SaveAsync(output, encoder, ct);
            output.Position = 0;
            return output;
        }
        catch
        {
            // Not a decodable raster image, or decoding failed for any other reason —
            // upload the original bytes unchanged rather than fail the whole upload.
            // ValidateMediumFile has already gated size and MIME type.
            fileStream.Position = 0;
            return fileStream;
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  VALIDATION & HELPERS
    // ─────────────────────────────────────────────────────────────
    private static readonly HashSet<string> AllowedMimeTypes = new()
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif",
    };

    // The formats ImageSharp may decode, checked against the file's actual bytes, not the
    // MIME type the browser sent: a TIFF labelled image/jpeg passes AllowedMimeTypes but
    // would still reach ImageSharp's TIFF decoder, which has open advisories in 3.1.x
    // (docs/security.md, A03).
    private static readonly HashSet<IImageFormat> DecodableFormats = new()
    {
        JpegFormat.Instance,
        PngFormat.Instance,
        WebpFormat.Instance,
        GifFormat.Instance,
    };

    // Rejects a file whose bytes are a recognised image format other than DecodableFormats.
    // Bytes ImageSharp doesn't recognise at all are left alone: they're stored unchanged and
    // never decoded (see ResizeIfOversizedAsync).
    private static async Task<string?> ValidateDetectedFormatAsync(Stream fileStream, CancellationToken ct)
    {
        if (!fileStream.CanSeek)
            return null;

        fileStream.Position = 0;
        IImageFormat? format;
        try
        {
            format = await Image.DetectFormatAsync(fileStream, ct);
        }
        catch (UnknownImageFormatException)
        {
            format = null;
        }
        fileStream.Position = 0;

        if (format is null || DecodableFormats.Contains(format))
            return null;

        return $"{format.Name} images aren't supported. Please upload a JPEG, PNG, WebP or GIF.";
    }

    private static readonly long MaxFileSizeBytes = 8 * 1024 * 1024; // 8 MB (images compressed client-side before upload)

    private static string? ValidateMediumFile(Stream fileStream, MediumUpsertDto dto)
    {
        if (fileStream.Length > MaxFileSizeBytes)
            return $"File size exceeds 50 MB limit. Uploaded file is {fileStream.Length / (1024 * 1024)} MB.";

        if (!string.IsNullOrEmpty(dto.MimeType) && !AllowedMimeTypes.Contains(dto.MimeType))
            return $"File type '{dto.MimeType}' is not allowed. Allowed types: {string.Join(", ", AllowedMimeTypes)}";

        return null;
    }

    private static MediumDto MapToDto(Medium medium) => new()
    {
        Id = medium.Id,
        PersonId = medium.PersonId,
        Url = medium.Url,
        FileName = medium.FileName,
        Caption = medium.Caption,
        MimeType = medium.MimeType,
        Type = medium.Type,
        CreatedAt = medium.CreatedAt,
        RowVersion = medium.RowVersion,
    };
}
