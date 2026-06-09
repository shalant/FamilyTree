using FamilyTree.Core.Data;
using FamilyTree.Core.Models;
using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Medium;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FamilyTree.Core.Services;

public class MediumService(
    IDbContextFactory<AppDbContext> dbFactory,
    IBlobStorageService blobStorage,
    ILogger<MediumService> logger) : IMediumService
{
    // ─────────────────────────────────────────────────────────────
    //  GET ALL
    // ─────────────────────────────────────────────────────────────
    public async Task<ServiceResponse<List<MediumDto>>> GetAllAsync(CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var media = await ctx.Media
                .AsNoTracking()
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

            var media = await ctx.Media
                .AsNoTracking()
                .Where(m => m.PersonId == personId)
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

            var medium = await ctx.Media
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == id, ct);

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

            var personExists = await ctx.People.AnyAsync(p => p.Id == dto.PersonId, ct);
            if (!personExists)
                return ServiceResponse<MediumDto>.Fail($"Person {dto.PersonId} not found.");

            var fileName = $"{dto.Type}-{Guid.NewGuid()}{Path.GetExtension(dto.FileName)}";
            var url = await blobStorage.UploadAsync(fileStream, fileName, dto.MimeType, ct);

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
            };

            ctx.Media.Add(medium);
            await ctx.SaveChangesAsync(ct);

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

            var medium = await ctx.Media.FirstOrDefaultAsync(m => m.Id == id, ct);
            if (medium is null)
                return ServiceResponse<MediumDto>.Fail($"Medium {id} not found.");

            medium.Caption = dto.Caption;
            medium.Type = dto.Type;
            medium.UpdatedAt = DateTime.UtcNow;

            ctx.Media.Update(medium);
            await ctx.SaveChangesAsync(ct);

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

            var medium = await ctx.Media.FirstOrDefaultAsync(m => m.Id == id, ct);
            if (medium is null)
                return ServiceResponse.Fail($"Medium {id} not found.");

            // TODO: Delete from blob storage
            _ = await blobStorage.DeleteAsync(medium.Url, ct);

            ctx.Media.Remove(medium);
            await ctx.SaveChangesAsync(ct);

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
    //  VALIDATION & HELPERS
    // ─────────────────────────────────────────────────────────────
    private static readonly HashSet<string> AllowedMimeTypes = new()
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif",
    };

    private static readonly long MaxFileSizeBytes = 50 * 1024 * 1024; // 50 MB

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
