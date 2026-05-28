using FamilyTree.Api.Data;
using FamilyTree.Api.Models;
using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Medium;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Api.Services;

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
        MediumUpsertDto dto, CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var medium = new Medium
            {
                Id = Guid.NewGuid(),
                PersonId = dto.PersonId,
                FileName = dto.FileName,
                Caption = dto.Caption,
                MimeType = dto.MimeType,
                Type = dto.Type,
                Url = "blob://placeholder",  // TODO: Populate from blob storage
                CreatedAt = DateTime.UtcNow,
            };

            ctx.Media.Add(medium);
            await ctx.SaveChangesAsync(ct);

            return ServiceResponse<MediumDto>.Ok(MapToDto(medium));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating medium");
            return ServiceResponse<MediumDto>.Fail(
                "An error occurred creating this media.");
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
    //  HELPERS
    // ─────────────────────────────────────────────────────────────
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
