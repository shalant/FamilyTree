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
    //  FAMILY SCOPE (findings S3/S4, docs/security.md, 2026-10-10)
    // ─────────────────────────────────────────────────────────────
    // A story has no FamilyId column. It belongs to its subject's family when linked;
    // an unlinked one (PersonId == null) belongs to its author's family, or for an
    // invite response, to the family of whoever sent the invite.
    private IQueryable<Story> ScopedStories(AppDbContext ctx)
    {
        if (currentUser.IsSuperUser) return ctx.Stories;
        if (currentUser.FamilyId is not { } familyId) return ctx.Stories.Where(_ => false);

        return ctx.Stories.Where(s =>
            (s.PersonId != null && ctx.People.Any(p => p.Id == s.PersonId && p.FamilyId == familyId)) ||
            (s.PersonId == null &&
                ((s.AuthorId != null && ctx.UserFamilies.Any(uf => uf.UserId == s.AuthorId && uf.FamilyId == familyId)) ||
                 (s.InviteId != null && ctx.StoryInvites.Any(i => i.Id == s.InviteId &&
                     ctx.UserFamilies.Any(uf => uf.UserId == i.InvitedByUserId && uf.FamilyId == familyId))))));
    }

    private IQueryable<Person> ScopedPeople(AppDbContext ctx)
    {
        var familyId = currentUser.FamilyId;
        var isSuperUser = currentUser.IsSuperUser;
        return ctx.People.Where(p => isSuperUser || (familyId.HasValue && p.FamilyId == familyId));
    }

    private bool IsAdmin => currentUser.IsSuperUser || currentUser.IsAdmin;

    // The family a story belongs to, by the same rule as ScopedStories; null when it
    // can't be determined (e.g. an unlinked story written by a super-user).
    private static async Task<Guid?> StoryFamilyIdAsync(AppDbContext ctx, Story story, CancellationToken ct)
    {
        if (story.PersonId is { } personId)
            return await ctx.People.IgnoreQueryFilters().Where(p => p.Id == personId)
                .Select(p => p.FamilyId).FirstOrDefaultAsync(ct);

        var userId = story.AuthorId
            ?? await ctx.StoryInvites.Where(i => i.Id == story.InviteId)
                .Select(i => (Guid?)i.InvitedByUserId).FirstOrDefaultAsync(ct);
        if (userId is null) return null;

        return await ctx.UserFamilies.Where(uf => uf.UserId == userId)
            .OrderBy(uf => uf.JoinedAt).Select(uf => (Guid?)uf.FamilyId).FirstOrDefaultAsync(ct);
    }

    // ─────────────────────────────────────────────────────────────
    //  GET BY PERSON
    // ─────────────────────────────────────────────────────────────
    public async Task<ServiceResponse<List<StoryDto>>> GetByPersonAsync(
        Guid personId, CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            // Approved and not hidden only: the person drawer showed pending invite
            // responses and admin-hidden stories to every member, bypassing moderation.
            // Admins review those in Admin > Stories.
            var stories = await ScopedStories(ctx)
                .AsNoTracking()
                .Include(s => s.Author)
                .Where(s => s.PersonId == personId && s.IsApproved && !s.IsHidden)
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

            // Stories are published immediately (IsApproved = true), so an anonymous or
            // family-less caller must not reach this (finding S1).
            if (currentUser.UserId is null || (!currentUser.IsSuperUser && !currentUser.FamilyId.HasValue))
                return ServiceResponse<StoryDto>.Fail("You must be signed in to a family to add a story.");

            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            if (dto.PersonId.HasValue)
            {
                var personExists = await ScopedPeople(ctx).AnyAsync(p => p.Id == dto.PersonId, ct);
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
            await ctx.Entry(story).Reference(s => s.Author).LoadAsync(ct);

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

            // Unlinked stories used to be in scope for every caller (!s.PersonId.HasValue).
            // Now: in the caller's family, and the caller is its author or an Admin.
            var story = await ScopedStories(ctx).FirstOrDefaultAsync(s => s.Id == id, ct);
            if (story is null)
                return ServiceResponse<StoryDto>.Fail($"Story {id} not found.");
            if (!IsAdmin && story.AuthorId != currentUser.UserId)
                return ServiceResponse<StoryDto>.Fail("You can only edit stories you wrote.");

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

            var story = await ScopedStories(ctx).FirstOrDefaultAsync(s => s.Id == id, ct);
            if (story is null)
                return ServiceResponse.Fail($"Story {id} not found.");
            if (!IsAdmin && story.AuthorId != currentUser.UserId)
                return ServiceResponse.Fail("You can only delete stories you wrote.");

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
    //  GET ALL APPROVED (public feed — excludes hidden)
    // ─────────────────────────────────────────────────────────────
    public async Task<ServiceResponse<List<StoryDto>>> GetAllApprovedAsync(CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var stories = await ScopedStories(ctx)
                .AsNoTracking()
                .Include(s => s.Author)
                .Where(s => s.IsApproved && !s.IsHidden)
                .OrderBy(s => s.SortOrder)
                .ThenByDescending(s => s.CreatedAt)
                .ToListAsync(ct);

            return ServiceResponse<List<StoryDto>>.Ok(stories.Select(MapToDto).ToList());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading all approved stories");
            return ServiceResponse<List<StoryDto>>.Fail(
                "An error occurred loading stories.");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  GET ALL (admin management — every status)
    // ─────────────────────────────────────────────────────────────
    public async Task<ServiceResponse<List<StoryDto>>> GetAllAsync(CancellationToken ct = default)
    {
        if (!IsAdmin) return ServiceResponse<List<StoryDto>>.Fail("Only admins can manage stories.");

        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var stories = await ScopedStories(ctx)
                .AsNoTracking()
                .Include(s => s.Author)
                .Include(s => s.Person)
                .OrderBy(s => s.SortOrder)
                .ThenByDescending(s => s.CreatedAt)
                .ToListAsync(ct);

            return ServiceResponse<List<StoryDto>>.Ok(stories.Select(MapToDto).ToList());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading all stories");
            return ServiceResponse<List<StoryDto>>.Fail(
                "An error occurred loading stories.");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  SET HIDDEN
    // ─────────────────────────────────────────────────────────────
    public async Task<ServiceResponse<StoryDto>> SetHiddenAsync(
        Guid storyId, bool hidden, CancellationToken ct = default)
    {
        if (!IsAdmin) return ServiceResponse<StoryDto>.Fail("Only admins can manage stories.");

        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var story = await ScopedStories(ctx).FirstOrDefaultAsync(s => s.Id == storyId, ct);
            if (story is null)
                return ServiceResponse<StoryDto>.Fail($"Story {storyId} not found.");

            story.IsHidden = hidden;
            story.UpdatedAt = DateTime.UtcNow;
            await ctx.SaveChangesAsync(ct);

            _ = auditLog.LogAsync(hidden ? "Hide" : "Unhide", "Story", storyId, userId: currentUser.UserId);

            return ServiceResponse<StoryDto>.Ok(MapToDto(story));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error setting hidden state for story {Id}", storyId);
            return ServiceResponse<StoryDto>.Fail(
                "An error occurred updating this story.");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  MOVE (reorder relative to display order, -1 = up, +1 = down)
    // ─────────────────────────────────────────────────────────────
    public async Task<ServiceResponse> MoveAsync(Guid storyId, int direction, CancellationToken ct = default)
    {
        if (!IsAdmin) return ServiceResponse.Fail("Only admins can manage stories.");

        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            // Reorders within the caller's family only; the global list used to swap
            // SortOrder with whichever family's story happened to sit next to it.
            var ordered = await ScopedStories(ctx)
                .OrderBy(s => s.SortOrder)
                .ThenByDescending(s => s.CreatedAt)
                .ToListAsync(ct);

            var index = ordered.FindIndex(s => s.Id == storyId);
            if (index < 0)
                return ServiceResponse.Fail($"Story {storyId} not found.");

            var swapIndex = index + direction;
            if (swapIndex < 0 || swapIndex >= ordered.Count)
                return ServiceResponse.Ok();

            (ordered[index].SortOrder, ordered[swapIndex].SortOrder) =
                (ordered[swapIndex].SortOrder, ordered[index].SortOrder);

            await ctx.SaveChangesAsync(ct);

            return ServiceResponse.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error reordering story {Id}", storyId);
            return ServiceResponse.Fail("An error occurred reordering this story.");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  GET UNLINKED
    // ─────────────────────────────────────────────────────────────
    public async Task<ServiceResponse<List<StoryDto>>> GetUnlinkedAsync(CancellationToken ct = default)
    {
        if (!IsAdmin) return ServiceResponse<List<StoryDto>>.Fail("Only admins can manage stories.");

        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var stories = await ScopedStories(ctx)
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

    public async Task<ServiceResponse<List<StoryDto>>> GetByAuthorEmailAsync(string email, CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var normalized = email.Trim().ToLower();
            var stories = await ctx.Stories
                .AsNoTracking()
                .Include(s => s.Author)
                .Include(s => s.Invite)
                .Where(s => (s.Author != null && s.Author.Email != null && s.Author.Email.ToLower() == normalized)
                         || (s.Invite != null && s.Invite.InvitedEmail.ToLower() == normalized))
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync(ct);

            return ServiceResponse<List<StoryDto>>.Ok(stories.Select(MapToDto).ToList());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading stories by author email");
            return ServiceResponse<List<StoryDto>>.Fail(
                "An error occurred loading stories.");
        }
    }

    public async Task<ServiceResponse<StoryDto?>> GetPendingLinkingSubjectAsync(string email, CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var normalized = email.Trim().ToLower();

            // Deliberately AuthorId == null (never a self-authored story) — someone
            // writing about themselves under a free-text name must never drive the
            // "subject-first" flow, or the modal ends up asking "is Tom related to
            // anyone?" about Tom while Tom is the one answering.
            var story = await ctx.Stories
                .AsNoTracking()
                .Include(s => s.Invite)
                .Where(s => s.PersonId == null
                         && s.AuthorId == null
                         && !string.IsNullOrWhiteSpace(s.UnlinkedPersonName)
                         && s.Invite != null
                         && s.Invite.InvitedEmail.ToLower() == normalized)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync(ct);

            return ServiceResponse<StoryDto?>.Ok(story is null ? null : MapToDto(story));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading pending linking subject for email");
            return ServiceResponse<StoryDto?>.Fail("An error occurred loading the pending story.");
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

            // Two callers: Admin > Stories (an Admin linking any story in their family) and
            // the post-signup LinkToTreeModal, which runs before the new user is signed in.
            // Either way the person must sit in the story's own family, so a story can't
            // be moved into, or attached to, another family (finding S4).
            var story = IsAdmin
                ? await ScopedStories(ctx).FirstOrDefaultAsync(s => s.Id == storyId, ct)
                : await ctx.Stories.FirstOrDefaultAsync(s => s.Id == storyId && s.PersonId == null, ct);
            if (story is null)
                return ServiceResponse<StoryDto>.Fail($"Story {storyId} not found.");

            var storyFamilyId = await StoryFamilyIdAsync(ctx, story, ct);
            var people = IsAdmin ? ScopedPeople(ctx) : ctx.People;
            var personExists = await people.AnyAsync(p => p.Id == personId &&
                (storyFamilyId == null ? currentUser.IsSuperUser : p.FamilyId == storyFamilyId), ct);
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
    //  APPROVE
    // ─────────────────────────────────────────────────────────────
    public async Task<ServiceResponse<StoryDto>> ApproveAsync(Guid storyId, CancellationToken ct = default)
    {
        if (!IsAdmin) return ServiceResponse<StoryDto>.Fail("Only admins can manage stories.");

        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var story = await ScopedStories(ctx).FirstOrDefaultAsync(s => s.Id == storyId, ct);
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
        IsHidden = story.IsHidden,
        SortOrder = story.SortOrder,
    };
}
