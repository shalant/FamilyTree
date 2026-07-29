using FamilyTree.Core.Data;
using FamilyTree.Core.Mappers;
using FamilyTree.Core.Models;
using FamilyTree.Shared;
using FamilyTree.Shared.DTOs;
using FamilyTree.Shared.DTOs.Relationship;
using FamilyTree.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FamilyTree.Core.Services;

public class RelationshipService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<RelationshipService> logger,
    IAuditLogService auditLog,
    ICurrentUserService currentUser) : IRelationshipService
{
    public async Task<ServiceResponse<List<RelationshipDto>>> GetAllAsync(
        CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var familyId = currentUser.FamilyId;
            var isSuperUser = currentUser.IsSuperUser;

            var rels = await ctx.Relationships
                .AsNoTracking()
                .Where(r => isSuperUser ||
                    (familyId.HasValue &&
                     ctx.People.Any(p => p.Id == r.PersonAId && p.FamilyId == familyId) &&
                     ctx.People.Any(p => p.Id == r.PersonBId && p.FamilyId == familyId)))
                .OrderBy(r => r.CreatedAt)
                .Select(r => RelationshipMapper.MapRelationshipToDto(r))
                .ToListAsync(ct);

            return ServiceResponse<List<RelationshipDto>>.Ok(rels);
        }
        catch (OperationCanceledException)
        {
            return ServiceResponse<List<RelationshipDto>>.Fail(
                "Request was cancelled.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading all relationships");
            return ServiceResponse<List<RelationshipDto>>.Fail(
                "An error occurred loading relationships.");
        }
    }

    public async Task<ServiceResponse<List<RelationshipDto>>> GetForPersonAsync(
        Guid personId, CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var familyId = currentUser.FamilyId;
            var isSuperUser = currentUser.IsSuperUser;

            // Confirm the target person is actually visible to this caller before
            // returning anything about their relationships — otherwise a caller who
            // knows a GUID from another family could enumerate that person's relations.
            var personVisible = await ctx.People.AnyAsync(p => p.Id == personId &&
                (isSuperUser || (familyId.HasValue && p.FamilyId == familyId)), ct);
            if (!personVisible)
                return ServiceResponse<List<RelationshipDto>>.Ok([]);

            var rels = await ctx.Relationships
                .AsNoTracking()
                .Where(r => r.PersonAId == personId || r.PersonBId == personId)
                .OrderBy(r => r.CreatedAt)
                .Select(r => RelationshipMapper.MapRelationshipToDto(r))
                .ToListAsync(ct);

            return ServiceResponse<List<RelationshipDto>>.Ok(rels);
        }
        catch (OperationCanceledException)
        {
            return ServiceResponse<List<RelationshipDto>>.Fail(
                "Request was cancelled.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading relationships for person {Id}", personId);
            return ServiceResponse<List<RelationshipDto>>.Fail(
                "An error occurred loading relationships for this person.");
        }
    }

    public async Task<ServiceResponse<RelationshipDto>> CreateAsync(
        RelationshipUpsertDto request, CancellationToken ct = default)
    {
        try
        {
            var dtoValidation = ValidationHelper.ValidateDtoNonGeneric(request);
            if (!dtoValidation.Success)
                return ServiceResponse<RelationshipDto>.Fail(dtoValidation.Message);

            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var scopeError = await ValidateBothPeopleInScopeAsync(ctx, request.PersonAId, request.PersonBId, ct);
            if (scopeError != null)
                return ServiceResponse<RelationshipDto>.Fail(scopeError);

            // Canonical ordering — lower ID is always PersonA
            var (a, b) = request.PersonAId < request.PersonBId
                ? (request.PersonAId, request.PersonBId)
                : (request.PersonBId, request.PersonAId);

            // Prevent duplicate relationships of the same type
            var exists = await ctx.Relationships.AnyAsync(
                r => r.PersonAId == a &&
                     r.PersonBId == b &&
                     r.Type == request.Type, ct);

            if (exists)
                return ServiceResponse<RelationshipDto>.Fail(
                    "This relationship already exists.");

            var ruleError = await ValidateNewRelationshipAsync(ctx, request, ct);
            if (ruleError != null)
                return ServiceResponse<RelationshipDto>.Fail(ruleError);

            var userId = currentUser.UserId;
            var relationship = new Relationship
            {
                PersonAId = a,
                PersonBId = b,
                Type = request.Type,
                StartDate = request.StartDate,
                Notes = request.Notes?.Trim(),
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userId,
            };

            ctx.Relationships.Add(relationship);
            await ctx.SaveChangesAsync(ct);

            _ = auditLog.LogAsync("Create", "Relationship", relationship.Id, userId: userId);

            return ServiceResponse<RelationshipDto>.Ok(RelationshipMapper.MapRelationshipToDto(relationship));
        }
        catch (OperationCanceledException)
        {
            return ServiceResponse<RelationshipDto>.Fail(
                "Request was cancelled.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating relationship");
            return ServiceResponse<RelationshipDto>.Fail(
                "An error occurred creating this relationship.");
        }
    }

    public async Task<ServiceResponse<RelationshipDto>> UpdateAsync(
    Guid id,
    RelationshipUpsertDto request,
    CancellationToken ct = default)
    {
        try
        {
            // Only super-users and admins can update relationships.
            if (!currentUser.IsSuperUser && !currentUser.IsAdmin)
                return ServiceResponse<RelationshipDto>.Fail("You do not have permission to update this relationship.");

            var dtoValidation = ValidationHelper.ValidateDtoNonGeneric(request);
            if (!dtoValidation.Success)
                return ServiceResponse<RelationshipDto>.Fail(dtoValidation.Message);

            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var rel = await ctx.Relationships.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (rel is null)
                return ServiceResponse<RelationshipDto>.Fail($"Relationship {id} not found.");

            // Same generic "not found" message whether the relationship truly doesn't
            // exist or exists in a different family — never reveal cross-family
            // existence. Check both the relationship's current people AND the
            // requested new people, so this can't be used to repoint a relationship
            // onto someone from another family either.
            if (await ValidateBothPeopleInScopeAsync(ctx, rel.PersonAId, rel.PersonBId, ct) != null)
                return ServiceResponse<RelationshipDto>.Fail($"Relationship {id} not found.");

            var scopeError = await ValidateBothPeopleInScopeAsync(ctx, request.PersonAId, request.PersonBId, ct);
            if (scopeError != null)
                return ServiceResponse<RelationshipDto>.Fail(scopeError);

            // Canonical ordering
            var (a, b) = request.PersonAId < request.PersonBId
                ? (request.PersonAId, request.PersonBId)
                : (request.PersonBId, request.PersonAId);

            // Validate updated relationship
            var ruleError = await ValidateNewRelationshipAsync(ctx, request, ct);
            if (ruleError != null)
                return ServiceResponse<RelationshipDto>.Fail(ruleError);

            // Apply updates
            var userId = currentUser.UserId;
            rel.PersonAId = a;
            rel.PersonBId = b;
            rel.Type = request.Type;
            rel.StartDate = request.StartDate;
            rel.EndDate = request.EndDate;
            rel.Notes = request.Notes?.Trim();
            rel.UpdatedAt = DateTime.UtcNow;
            rel.UpdatedBy = userId;

            await ctx.SaveChangesAsync(ct);

            _ = auditLog.LogAsync("Update", "Relationship", id, userId: userId);

            return ServiceResponse<RelationshipDto>.Ok(RelationshipMapper.MapRelationshipToDto(rel));
        }
        catch (DbUpdateConcurrencyException ex)
        {
            logger.LogWarning(ex, "Concurrency conflict updating relationship {Id}", id);
            return ServiceResponse<RelationshipDto>.Fail(
                "This record was modified by someone else. Please reload and try again.");
        }
        catch (OperationCanceledException)
        {
            return ServiceResponse<RelationshipDto>.Fail("Request was cancelled.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating relationship {Id}", id);
            return ServiceResponse<RelationshipDto>.Fail(
                "An error occurred updating this relationship.");
        }
    }

    public async Task<ServiceResponse> DeleteAsync(
        Guid id, CancellationToken ct = default)
    {
        try
        {
            // Only super-users and admins can delete relationships.
            if (!currentUser.IsSuperUser && !currentUser.IsAdmin)
                return ServiceResponse.Fail("You do not have permission to delete this relationship.");

            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var relationship = await ctx.Relationships
                .FirstOrDefaultAsync(r => r.Id == id, ct);

            if (relationship is null)
                return ServiceResponse.Fail($"Relationship {id} not found.");

            if (await ValidateBothPeopleInScopeAsync(ctx, relationship.PersonAId, relationship.PersonBId, ct) != null)
                return ServiceResponse.Fail($"Relationship {id} not found.");

            ctx.Relationships.Remove(relationship);
            await ctx.SaveChangesAsync(ct);

            _ = auditLog.LogAsync("Delete", "Relationship", id, userId: currentUser.UserId);

            return ServiceResponse.Ok();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            logger.LogWarning(ex,
                "Concurrency conflict deleting relationship {Id}", id);
            return ServiceResponse.Fail(
                "This record was modified by someone else. Please reload and try again.");
        }
        catch (OperationCanceledException)
        {
            return ServiceResponse.Fail("Request was cancelled.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting relationship {Id}", id);
            return ServiceResponse.Fail(
                "An error occurred deleting this relationship.");
        }
    }

    private async Task<string?> ValidateNewRelationshipAsync(
        AppDbContext ctx, RelationshipUpsertDto request, CancellationToken ct)
    {
        if (request.PersonAId == request.PersonBId)
            return "A person cannot be related to themselves.";

        // Validate date range
        if (request.StartDate.HasValue && request.EndDate.HasValue &&
            request.StartDate.Value >= request.EndDate.Value)
            return "Start date must be before end date.";

        if (request.Type is RelationshipType.Parent or RelationshipType.Adopted)
        {
            var parent = await ctx.People.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == request.PersonAId, ct);
            var child = await ctx.People.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == request.PersonBId, ct);

            if (parent != null && child != null)
            {
                if (parent.DeathDate.HasValue && child.BirthDate.HasValue &&
                    child.BirthDate.Value > parent.DeathDate.Value.AddMonths(9))
                    return $"{parent.FirstName} {parent.LastName} died on {parent.DeathDate:d} " +
                           $"and cannot be the parent of {child.FirstName}, born {child.BirthDate:d}.";

                if (parent.BirthDate.HasValue && child.BirthDate.HasValue)
                {
                    var ageGap = child.BirthDate.Value.Year - parent.BirthDate.Value.Year;
                    if (ageGap < 12)
                        return $"{parent.FirstName} would have been {ageGap} year(s) old when " +
                               $"{child.FirstName} was born — too young to be a parent.";
                    if (ageGap > 80)
                        return $"{parent.FirstName} would have been {ageGap} years old when " +
                               $"{child.FirstName} was born — too large an age gap.";
                }
            }
        }

        if (request.Type == RelationshipType.Spouse)
        {
            var aId = request.PersonAId;
            var bId = request.PersonBId;

            var personA = await ctx.People.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == aId, ct);
            var personB = await ctx.People.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == bId, ct);

            var aHasSpouse = await ctx.Relationships.AnyAsync(
                r => (r.PersonAId == aId || r.PersonBId == aId) &&
                     r.Type == RelationshipType.Spouse &&
                     r.EndDate == null, ct);
            if (aHasSpouse)
                return $"{personA?.FirstName ?? "This person"} is already in an active marriage.";

            var bHasSpouse = await ctx.Relationships.AnyAsync(
                r => (r.PersonAId == bId || r.PersonBId == bId) &&
                     r.Type == RelationshipType.Spouse &&
                     r.EndDate == null, ct);
            if (bHasSpouse)
                return $"{personB?.FirstName ?? "This person"} is already in an active marriage.";
        }

        return null;
    }

    // Confirms BOTH people belong to the caller's family (or the caller is a
    // super-user) before allowing a relationship to be created, repointed, updated,
    // or deleted — without this, any authenticated user who knew two GUIDs from
    // different families could link them together, or read/modify a relationship
    // that belongs entirely to someone else's family.
    private async Task<string?> ValidateBothPeopleInScopeAsync(
        AppDbContext ctx, Guid personAId, Guid personBId, CancellationToken ct)
    {
        if (currentUser.IsSuperUser) return null;

        var familyId = currentUser.FamilyId;
        if (!familyId.HasValue)
            return "You do not have access to create or modify relationships.";

        var matchingCount = await ctx.People.CountAsync(p =>
            (p.Id == personAId || p.Id == personBId) && p.FamilyId == familyId, ct);

        return matchingCount == 2 ? null : "One or both people were not found in your family.";
    }
}