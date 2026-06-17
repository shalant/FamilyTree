using FamilyTree.Core.Data;
using FamilyTree.Core.Models;
using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Story;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FamilyTree.Core.Services;

public class StoryService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<StoryService> logger,
    IAuditLogService auditLog,
    ICurrentUserService currentUser) : IStoryService
{
    // ─────────────────────────────────────────────────────────────
    //  GET BY PERSON
    // ─────────────────────────────────────────────────────────────
    public async Task<ServiceResponse<List<StoryDto>>> GetByPersonAsync(
        Guid personId, CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var stories = await ctx.Stories
                .AsNoTracking()
                .Include(s => s.Author)
                .Where(s => s.PersonId == personId)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync(ct);

            return ServiceResponse<List<StoryDto>>.Ok(stories.Select(MapToDto).ToList());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading stories for person {PersonId}", personId);
            return ServiceResponse<List<StoryDto>>.Fail(
                "An error occurred loading stories for this person.");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  CREATE
    // ─────────────────────────────────────────────────────────────
    public async Task<ServiceResponse<StoryDto>> CreateAsync(
        StoryUpsertDto dto, CancellationToken ct = default)
    {
        try
        {
            var dtoValidation = ValidationHelper.ValidateDtoNonGeneric(dto);
            if (!dtoValidation.Success)
                return ServiceResponse<StoryDto>.Fail(dtoValidation.Message);

            if (dto.PersonId is null && string.IsNullOrWhiteSpace(dto.UnlinkedPersonName))
                return ServiceResponse<StoryDto>.Fail(
                    "Either an existing person or a person's name is required.");

            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            if (dto.PersonId.HasValue)
            {
                var personExists = await ctx.People.AnyAsync(p => p.Id == dto.PersonId, ct);
                if (!personExists)
                    return ServiceResponse<StoryDto>.Fail($"Person {dto.PersonId} not found.");
            }

            var userId = currentUser.UserId;
            var story = new Story
            {
                Id = Guid.NewGuid(),
                PersonId = dto.PersonId,
                UnlinkedPersonName = dto.PersonId.HasValue ? null : dto.UnlinkedPersonName,
                AuthorId = userId,
                Title = dto.Title,
                Body = dto.Body,
                EventId = dto.EventId,
                IsApproved = true,
                CreatedAt = DateTime.UtcNow,
            };

            ctx.Stories.Add(story);
            await ctx.SaveChangesAsync(ct);

            _ = auditLog.LogAsync("Create", "Story", story.Id, userId: userId);

            return ServiceResponse<StoryDto>.Ok(MapToDto(story));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating story");
            return ServiceResponse<StoryDto>.Fail(
                "An error occurred creating this story.");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  UPDATE
    // ─────────────────────────────────────────────────────────────
    public async Task<ServiceResponse<StoryDto>> UpdateAsync(
        Guid id, StoryUpsertDto dto, CancellationToken ct = default)
    {
        try
        {
            var dtoValidation = ValidationHelper.ValidateDtoNonGeneric(dto);
            if (!dtoValidation.Success)
                return ServiceResponse<StoryDto>.Fail(dtoValidation.Message);

            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var familyId = currentUser.FamilyId;

            // TODO: Remove this guard when full family scoping is implemented via UserFamily membership.
            var story = await ctx.Stories.FirstOrDefaultAsync(s => s.Id == id &&
                (!familyId.HasValue || !s.PersonId.HasValue ||
                 ctx.People.Any(p => p.Id == s.PersonId && p.FamilyId == familyId)), ct);
            if (story is null)
                return ServiceResponse<StoryDto>.Fail($"Story {id} not found.");

            story.Title = dto.Title;
            story.Body = dto.Body;
            story.EventId = dto.EventId;
            story.UpdatedAt = DateTime.UtcNow;

            await ctx.SaveChangesAsync(ct);

            _ = auditLog.LogAsync("Update", "Story", id, userId: currentUser.UserId);

            return ServiceResponse<StoryDto>.Ok(MapToDto(story));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating story {Id}", id);
            return ServiceResponse<StoryDto>.Fail(
                "An error occurred updating this story.");
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

            // TODO: Remove this guard when full family scoping is implemented via UserFamily membership.
            var story = await ctx.Stories.FirstOrDefaultAsync(s => s.Id == id &&
                (!familyId.HasValue || !s.PersonId.HasValue ||
                 ctx.People.Any(p => p.Id == s.PersonId && p.FamilyId == familyId)), ct);
            if (story is null)
                return ServiceResponse.Fail($"Story {id} not found.");

            ctx.Stories.Remove(story);
            await ctx.SaveChangesAsync(ct);

            _ = auditLog.LogAsync("Delete", "Story", id, userId: currentUser.UserId);

            return ServiceResponse.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting story {Id}", id);
            return ServiceResponse.Fail(
                "An error occurred deleting this story.");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  GET UNLINKED
    // ─────────────────────────────────────────────────────────────
    public async Task<ServiceResponse<List<StoryDto>>> GetUnlinkedAsync(CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var stories = await ctx.Stories
                .AsNoTracking()
                .Include(s => s.Author)
                .Where(s => s.PersonId == null)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync(ct);

            return ServiceResponse<List<StoryDto>>.Ok(stories.Select(MapToDto).ToList());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading unlinked stories");
            return ServiceResponse<List<StoryDto>>.Fail(
                "An error occurred loading unlinked stories.");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  LINK TO PERSON
    // ─────────────────────────────────────────────────────────────
    public async Task<ServiceResponse<StoryDto>> LinkToPersonAsync(
        Guid storyId, Guid personId, CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var story = await ctx.Stories.FirstOrDefaultAsync(s => s.Id == storyId, ct);
            if (story is null)
                return ServiceResponse<StoryDto>.Fail($"Story {storyId} not found.");

            var personExists = await ctx.People.AnyAsync(p => p.Id == personId, ct);
            if (!personExists)
                return ServiceResponse<StoryDto>.Fail($"Person {personId} not found.");

            story.PersonId = personId;
            story.UnlinkedPersonName = null;
            story.UpdatedAt = DateTime.UtcNow;

            await ctx.SaveChangesAsync(ct);

            _ = auditLog.LogAsync("Link", "Story", storyId, userId: currentUser.UserId);

            return ServiceResponse<StoryDto>.Ok(MapToDto(story));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error linking story {Id} to person {PersonId}", storyId, personId);
            return ServiceResponse<StoryDto>.Fail(
                "An error occurred linking this story.");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  GET PENDING APPROVAL
    // ─────────────────────────────────────────────────────────────
    public async Task<ServiceResponse<List<StoryDto>>> GetPendingApprovalAsync(CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var stories = await ctx.Stories
                .AsNoTracking()
                .Include(s => s.Author)
                .Include(s => s.Person)
                .Where(s => !s.IsApproved)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync(ct);

            return ServiceResponse<List<StoryDto>>.Ok(stories.Select(MapToDto).ToList());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading stories pending approval");
            return ServiceResponse<List<StoryDto>>.Fail(
                "An error occurred loading stories pending approval.");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  APPROVE
    // ─────────────────────────────────────────────────────────────
    public async Task<ServiceResponse<StoryDto>> ApproveAsync(Guid storyId, CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var story = await ctx.Stories.FirstOrDefaultAsync(s => s.Id == storyId, ct);
            if (story is null)
                return ServiceResponse<StoryDto>.Fail($"Story {storyId} not found.");

            story.IsApproved = true;
            story.UpdatedAt = DateTime.UtcNow;
            await ctx.SaveChangesAsync(ct);

            _ = auditLog.LogAsync("Approve", "Story", storyId, userId: currentUser.UserId);

            return ServiceResponse<StoryDto>.Ok(MapToDto(story));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error approving story {Id}", storyId);
            return ServiceResponse<StoryDto>.Fail(
                "An error occurred approving this story.");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  MAPPING
    // ─────────────────────────────────────────────────────────────
    private static StoryDto MapToDto(Story story) => new()
    {
        Id = story.Id,
        PersonId = story.PersonId,
        UnlinkedPersonName = story.UnlinkedPersonName,
        AuthorId = story.AuthorId,
        AuthorDisplayName = story.Author?.DisplayName,
        AuthorName = story.AuthorName,
        InviteId = story.InviteId,
        Title = story.Title,
        Body = story.Body,
        CreatedAt = story.CreatedAt,
        UpdatedAt = story.UpdatedAt,
        EventId = story.EventId,
        IsApproved = story.IsApproved,
    };
}
