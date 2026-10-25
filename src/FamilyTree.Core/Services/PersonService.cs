using FamilyTree.Core.Data;
using FamilyTree.Core.Mappers;
using FamilyTree.Core.Models;
using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FamilyTree.Core.Services;

public class PersonService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<PersonService> logger,
    IAuditLogService auditLog,
    ICurrentUserService currentUser) : IPersonService
{
    // People the caller may see or touch: a super-user sees every family (including
    // legacy FamilyId == null rows); anyone else only their own family, and a caller
    // with no FamilyId claim (anonymous, or a missing UserFamily row) sees nobody.
    // Every ID-taking method goes through this so a family-B GUID behaves exactly like
    // a GUID that doesn't exist (findings S3/S4/S6, docs/security.md, 2026-10-10).
    private IQueryable<Person> ScopedPeople(AppDbContext ctx, bool includeDeleted = false)
    {
        var people = includeDeleted ? ctx.People.IgnoreQueryFilters() : ctx.People;
        if (currentUser.IsSuperUser) return people;
        var familyId = currentUser.FamilyId;
        return people.Where(p => familyId.HasValue && p.FamilyId == familyId);
    }

    // Relationships of one person whose OTHER party is also visible, so a legacy
    // cross-family row never surfaces a family-B person's ID in a family-A DTO.
    private Task<List<Relationship>> VisibleRelationshipsOfAsync(AppDbContext ctx, Guid personId, CancellationToken ct)
    {
        var visibleIds = ScopedPeople(ctx).Select(p => p.Id);
        return ctx.Relationships
            .AsNoTracking()
            .Where(r => (r.PersonAId == personId && visibleIds.Contains(r.PersonBId)) ||
                        (r.PersonBId == personId && visibleIds.Contains(r.PersonAId)))
            .ToListAsync(ct);
    }

    // Every relative ID in the DTO must be a person the caller can see. Runs before the
    // parent/spouse validators, which quote the relative's name, death date and marriage
    // status back in their error messages.
    private async Task<string?> ValidateRelativesInScopeAsync(AppDbContext ctx, PersonUpsertDto dto, CancellationToken ct)
    {
        var ids = dto.ParentIds.Concat(dto.ChildIds).Concat(dto.SpouseIds)
            .Concat(dto.FormerSpouseIds).Concat(dto.SiblingIds).Distinct().ToList();
        if (ids.Count == 0) return null;

        var visible = await ScopedPeople(ctx).CountAsync(p => ids.Contains(p.Id), ct);
        return visible == ids.Count ? null : "One or more of the selected relatives were not found in your family.";
    }

    // ─────────────────────────────────────────────────────────────
    //  GET ALL
    // ─────────────────────────────────────────────────────────────
    public async Task<ServiceResponse<List<PersonDto>>> GetAllAsync(
        CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var familyId = currentUser.FamilyId;
            var isSuperUser = currentUser.IsSuperUser;

            // A super-user (no FamilyId claim, by design) sees everything, including
            // legacy FamilyId == null records. A regular user with no FamilyId claim —
            // e.g. a broken/missing UserFamily assignment — must see NOTHING rather than
            // being accidentally treated the same as a super-user.
            var people = await ctx.People
                .AsNoTracking()
                .Where(p => isSuperUser || (familyId.HasValue && p.FamilyId == familyId))
                .OrderBy(p => p.LastName)
                .ThenBy(p => p.FirstName)
                .ToListAsync(ct);

            var peopleIds = people.Select(p => p.Id).ToHashSet();

            var relationships = await ctx.Relationships
                .AsNoTracking()
                .ToListAsync(ct);

            // Exclude relationships where either party is soft-deleted or outside this family
            relationships = relationships
                .Where(r => peopleIds.Contains(r.PersonAId) && peopleIds.Contains(r.PersonBId))
                .ToList();

            var dtos = people
                .Select(p => PersonMapper.MapPersonToDto(p, relationships))
                .ToList();

            return ServiceResponse<List<PersonDto>>.Ok(dtos);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading all people");
            return ServiceResponse<List<PersonDto>>.Fail(
                "An error occurred loading family members.");
        }
    }

    public async Task<ServiceResponse<List<PersonDto>>> GetAllForUserAsync(
        Guid userId, CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var familyId = await ctx.UserFamilies
                .Where(uf => uf.UserId == userId)
                .Select(uf => (Guid?)uf.FamilyId)
                .FirstOrDefaultAsync(ct);

            if (familyId is null)
                return ServiceResponse<List<PersonDto>>.Ok([]);

            var people = await ctx.People
                .AsNoTracking()
                .Where(p => p.FamilyId == familyId)
                .OrderBy(p => p.LastName)
                .ThenBy(p => p.FirstName)
                .ToListAsync(ct);

            var peopleIds = people.Select(p => p.Id).ToHashSet();

            var relationships = await ctx.Relationships
                .AsNoTracking()
                .Where(r => peopleIds.Contains(r.PersonAId) && peopleIds.Contains(r.PersonBId))
                .ToListAsync(ct);

            var dtos = people
                .Select(p => PersonMapper.MapPersonToDto(p, relationships))
                .ToList();

            return ServiceResponse<List<PersonDto>>.Ok(dtos);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading people for user {UserId}", userId);
            return ServiceResponse<List<PersonDto>>.Fail(
                "An error occurred loading family members.");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  GET BY ID
    // ─────────────────────────────────────────────────────────────
    public async Task<ServiceResponse<PersonDto>> GetByIdAsync(
        Guid id, CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var familyId = currentUser.FamilyId;
            var isSuperUser = currentUser.IsSuperUser;

            var person = await ctx.People
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id &&
                    (isSuperUser || (familyId.HasValue && p.FamilyId == familyId)), ct);

            if (person is null)
                return ServiceResponse<PersonDto>.Fail($"Person {id} not found.");

            var relationships = await VisibleRelationshipsOfAsync(ctx, id, ct);

            return ServiceResponse<PersonDto>.Ok(PersonMapper.MapPersonToDto(person, relationships));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading person {Id}", id);
            return ServiceResponse<PersonDto>.Fail(
                "An error occurred loading this person.");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  CREATE
    // ─────────────────────────────────────────────────────────────
    public async Task<ServiceResponse<PersonDto>> CreateAsync(
        PersonUpsertDto dto, CancellationToken ct = default)
    {
        try
        {
            var dtoValidation = ValidationHelper.ValidateDto(dto);
            if (!dtoValidation.Success) return ServiceResponse<PersonDto>.Fail(dtoValidation.Message);

            // Anonymous callers and accounts with no UserFamily row used to fall through to
            // the oldest family (finding S1). Only a super-user may create without a family.
            if (!currentUser.IsSuperUser && !currentUser.FamilyId.HasValue)
                return ServiceResponse<PersonDto>.Fail("You must be signed in to a family to add people.");

            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var dateError = ValidatePersonDates(dto);
            if (dateError != null) return ServiceResponse<PersonDto>.Fail(dateError);

            var nameError = ValidatePersonNames(dto);
            if (nameError != null) return ServiceResponse<PersonDto>.Fail(nameError);

            var scopeError = await ValidateRelativesInScopeAsync(ctx, dto, ct);
            if (scopeError != null) return ServiceResponse<PersonDto>.Fail(scopeError);

            var lifeError = await ValidateParentChildDatesAsync(ctx, Guid.Empty, dto, datesChanged: true, ct);
            if (lifeError != null) return ServiceResponse<PersonDto>.Fail(lifeError);

            // The person doesn't exist yet, so Guid.Empty stands in for them.
            var loopError = await ValidateNoParentLoopAsync(ctx, Guid.Empty, $"{dto.FirstName} {dto.LastName}".Trim(), dto, ct);
            if (loopError != null) return ServiceResponse<PersonDto>.Fail(loopError);

            var spouseError = await ValidateSpouseRelationshipsAsync(ctx, Guid.Empty, dto, ct);
            if (spouseError != null) return ServiceResponse<PersonDto>.Fail(spouseError);

            var now = DateTime.UtcNow;
            var userId = currentUser.UserId;
            var familyId = await ResolveFamilyIdForCreateAsync(ctx, ct);

            var person = new Person
            {
                FirstName = dto.FirstName.Trim(),
                LastName = dto.LastName.Trim(),
                MiddleName = string.IsNullOrWhiteSpace(dto.MiddleName) ? null : dto.MiddleName.Trim(),
                MaidenName = string.IsNullOrWhiteSpace(dto.MaidenName) ? null : dto.MaidenName.Trim(),
                BirthDate = dto.BirthDate,
                BirthPlace = string.IsNullOrWhiteSpace(dto.BirthPlace) ? null : dto.BirthPlace.Trim(),
                DeathDate = dto.DeathDate,
                DeathPlace = string.IsNullOrWhiteSpace(dto.DeathPlace) ? null : dto.DeathPlace.Trim(),
                Gender = dto.Gender,
                BiographyNotes = dto.BiographyNotes?.Trim(),
                ProfilePhotoUrl = string.IsNullOrWhiteSpace(dto.ProfilePhotoUrl) ? null : dto.ProfilePhotoUrl.Trim(),
                Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim(),
                FamilyId = familyId,
                CreatedAt = now,
                CreatedBy = userId,
                UpdatedAt = now,
            };

            ctx.People.Add(person);
            await ctx.SaveChangesAsync(ct);

            await SyncRelationshipsAsync(ctx, person.Id, dto, userId, ct);
            await ctx.SaveChangesAsync(ct);

            _ = auditLog.LogAsync("Create", "Person", person.Id, userId: userId);

            var relationships = await VisibleRelationshipsOfAsync(ctx, person.Id, ct);

            return ServiceResponse<PersonDto>.Ok(PersonMapper.MapPersonToDto(person, relationships));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating person");
            return ServiceResponse<PersonDto>.Fail(
                "An error occurred creating this person.");
        }
    }

    // A super-user has no FamilyId claim by design (they see every family), so creating
    // a person directly as a super-user would otherwise leave it ownerless (FamilyId ==
    // null) — invisible to every regular user, including whichever family it should
    // actually belong to. Falls back to the single family this deployment currently has
    // (creating it if truly missing, mirroring AuthService.CreateInviteAsync's same
    // get-or-create pattern). Regular users without a FamilyId no longer reach this:
    // CreateAsync rejects them (finding S1, 2026-10-10) instead of dropping their person
    // into the oldest family.
    private async Task<Guid> ResolveFamilyIdForCreateAsync(AppDbContext ctx, CancellationToken ct)
    {
        if (currentUser.FamilyId.HasValue) return currentUser.FamilyId.Value;

        // Deterministically the oldest family — with more than one Family row now
        // possible (e.g. a separate test/demo family), an unordered FirstOrDefault
        // would pick an arbitrary one instead of the original/primary family.
        var family = await ctx.Families.OrderBy(f => f.CreatedAt).FirstOrDefaultAsync(ct);
        if (family is null)
        {
            family = new Family { Id = Guid.NewGuid(), Name = "My Family", CreatedAt = DateTime.UtcNow };
            ctx.Families.Add(family);
            await ctx.SaveChangesAsync(ct);
        }

        return family.Id;
    }

    // ─────────────────────────────────────────────────────────────
    //  UPDATE
    // ─────────────────────────────────────────────────────────────
    public async Task<ServiceResponse<PersonDto>> UpdateAsync(
    Guid id, PersonUpsertDto dto, CancellationToken ct = default)
    {
        try
        {
            var dtoValidation = ValidationHelper.ValidateDto(dto);
            if (!dtoValidation.Success)
                return ServiceResponse<PersonDto>.Fail(dtoValidation.Message);

            // Only super-users and admins can update people. Checked before the lookup so a
            // Member gets the same answer for a real GUID and a made-up one.
            if (!currentUser.IsSuperUser && !currentUser.IsAdmin)
                return ServiceResponse<PersonDto>.Fail("You do not have permission to update this person.");

            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var person = await ScopedPeople(ctx).FirstOrDefaultAsync(p => p.Id == id, ct);
            if (person is null)
                return ServiceResponse<PersonDto>.Fail($"Person {id} not found.");

            var dateError = ValidatePersonDates(dto);
            if (dateError != null) return ServiceResponse<PersonDto>.Fail(dateError);

            var nameError = ValidatePersonNames(dto);
            if (nameError != null) return ServiceResponse<PersonDto>.Fail(nameError);

            var scopeError = await ValidateRelativesInScopeAsync(ctx, dto, ct);
            if (scopeError != null) return ServiceResponse<PersonDto>.Fail(scopeError);

            var datesChanged = person.BirthDate != dto.BirthDate || person.DeathDate != dto.DeathDate ||
                               person.Gender != dto.Gender;
            var lifeError = await ValidateParentChildDatesAsync(ctx, id, dto, datesChanged, ct);
            if (lifeError != null) return ServiceResponse<PersonDto>.Fail(lifeError);

            var loopError = await ValidateNoParentLoopAsync(ctx, id, $"{person.FirstName} {person.LastName}".Trim(), dto, ct);
            if (loopError != null) return ServiceResponse<PersonDto>.Fail(loopError);

            var spouseError = await ValidateSpouseRelationshipsAsync(ctx, id, dto, ct);
            if (spouseError != null) return ServiceResponse<PersonDto>.Fail(spouseError);

            var userId = currentUser.UserId;

            person.FirstName = dto.FirstName.Trim();
            person.LastName = dto.LastName.Trim();
            person.MiddleName = string.IsNullOrWhiteSpace(dto.MiddleName) ? null : dto.MiddleName.Trim();
            person.MaidenName = string.IsNullOrWhiteSpace(dto.MaidenName) ? null : dto.MaidenName.Trim();
            person.BirthDate = dto.BirthDate;
            person.BirthPlace = string.IsNullOrWhiteSpace(dto.BirthPlace) ? null : dto.BirthPlace.Trim();
            person.DeathDate = dto.DeathDate;
            person.DeathPlace = string.IsNullOrWhiteSpace(dto.DeathPlace) ? null : dto.DeathPlace.Trim();
            person.Gender = dto.Gender;
            person.BiographyNotes = dto.BiographyNotes?.Trim();
            person.ProfilePhotoUrl = string.IsNullOrWhiteSpace(dto.ProfilePhotoUrl) ? null : dto.ProfilePhotoUrl.Trim();
            person.Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();
            person.UpdatedAt = DateTime.UtcNow;
            person.UpdatedBy = userId;

            await SyncRelationshipsDiffAsync(ctx, id, dto, userId, ct);
            await ctx.SaveChangesAsync(ct);

            _ = auditLog.LogAsync("Update", "Person", id, userId: userId);

            var relationships = await VisibleRelationshipsOfAsync(ctx, id, ct);

            return ServiceResponse<PersonDto>.Ok(PersonMapper.MapPersonToDto(person, relationships));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating person {Id}", id);
            return ServiceResponse<PersonDto>.Fail("An error occurred updating this person.");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  SET PROFILE PHOTO
    // ─────────────────────────────────────────────────────────────
    // Narrower than UpdateAsync so the person's creator can do it too: PersonAdd uploads
    // the form's queued photos after the person exists, then makes one of them the profile
    // photo. Until 2026-10-25 it did that through UpdateAsync, which Members can't call,
    // so a Member's new person silently kept no photo while the page said "added".
    // The photo must already be one of that person's own media, so no URL is taken.
    public async Task<ServiceResponse> SetProfilePhotoAsync(
        Guid personId, Guid mediumId, CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var person = await ScopedPeople(ctx).FirstOrDefaultAsync(p => p.Id == personId, ct);
            if (person is null)
                return ServiceResponse.Fail($"Person {personId} not found.");

            var userId = currentUser.UserId;
            var isCreator = userId.HasValue && person.CreatedBy == userId;
            if (!currentUser.IsSuperUser && !currentUser.IsAdmin && !isCreator)
                return ServiceResponse.Fail("You do not have permission to update this person.");

            var medium = await ctx.Media.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == mediumId && m.PersonId == personId, ct);
            if (medium is null)
                return ServiceResponse.Fail("That photo isn't one of this person's photos.");

            person.ProfilePhotoUrl = medium.Url;
            person.UpdatedAt = DateTime.UtcNow;
            person.UpdatedBy = userId;
            await ctx.SaveChangesAsync(ct);

            _ = auditLog.LogAsync("Update", "Person", personId, userId: userId);

            return ServiceResponse.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error setting profile photo for person {Id}", personId);
            return ServiceResponse.Fail("An error occurred setting this person's photo.");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  DELETE (soft)
    // ─────────────────────────────────────────────────────────────
    public async Task<ServiceResponse> DeleteAsync(
        Guid id, CancellationToken ct = default)
    {
        try
        {
            // Only super-users and admins can delete people.
            if (!currentUser.IsSuperUser && !currentUser.IsAdmin)
                return ServiceResponse.Fail("You do not have permission to delete this person.");

            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var person = await ScopedPeople(ctx)
                .FirstOrDefaultAsync(p => p.Id == id, ct);

            if (person is null)
                return ServiceResponse.Fail($"Person {id} not found.");

            var userId = currentUser.UserId;
            var now = DateTime.UtcNow;

            person.DeletedAt = now;
            person.DeletedBy = userId;

            // Cascade soft-delete this person's own relationships so they don't linger
            // as dangling rows once the person disappears from every query (they're
            // already excluded from GetAllAsync's relationship list, but leaving them
            // live in the DB is needless clutter). Tagged with the exact same timestamp
            // so RestoreAsync can tell these apart from relationships that were already
            // independently removed before this delete.
            var relationships = await ctx.Relationships
                .Where(r => r.PersonAId == id || r.PersonBId == id)
                .ToListAsync(ct);
            foreach (var rel in relationships)
            {
                rel.DeletedAt = now;
                rel.DeletedBy = userId;
            }

            await ctx.SaveChangesAsync(ct);

            _ = auditLog.LogAsync("Delete", "Person", id, userId: userId);

            return ServiceResponse.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting person {Id}", id);
            return ServiceResponse.Fail(
                "An error occurred deleting this person.");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  RESTORE
    // ─────────────────────────────────────────────────────────────
    public async Task<ServiceResponse> RestoreAsync(
        Guid id, CancellationToken ct = default)
    {
        try
        {
            // Same rule as DeleteAsync; restore previously had no role or family check.
            if (!currentUser.IsSuperUser && !currentUser.IsAdmin)
                return ServiceResponse.Fail("You do not have permission to restore this person.");

            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var person = await ScopedPeople(ctx, includeDeleted: true)
                .FirstOrDefaultAsync(p => p.Id == id, ct);

            if (person is null)
                return ServiceResponse.Fail($"Person {id} not found.");

            if (person.DeletedAt is null)
                return ServiceResponse.Fail("Person is not deleted.");

            var deletedAt = person.DeletedAt.Value;

            // Restore only relationships that were cascade-deleted alongside this exact
            // person deletion (same timestamp) — a relationship the user had already
            // removed independently, before this person was deleted, stays deleted.
            var relationships = await ctx.Relationships
                .IgnoreQueryFilters()
                .Where(r => (r.PersonAId == id || r.PersonBId == id) && r.DeletedAt == deletedAt)
                .ToListAsync(ct);

            // Restoring brings Parent links back, and the tree may have changed since they
            // were removed (R0): refuse if that would put someone back among their own
            // ancestors, rather than reviving a loop.
            var loopError = await ValidateRestoredParentLinksAsync(ctx, relationships, ct);
            if (loopError != null) return ServiceResponse.Fail(loopError);

            person.DeletedAt = null;
            person.DeletedBy = null;
            foreach (var rel in relationships)
            {
                rel.DeletedAt = null;
                rel.DeletedBy = null;
            }

            await ctx.SaveChangesAsync(ct);

            _ = auditLog.LogAsync("Restore", "Person", id, userId: currentUser.UserId);

            return ServiceResponse.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error restoring person {Id}", id);
            return ServiceResponse.Fail("An error occurred restoring this person.");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  GET DELETED
    // ─────────────────────────────────────────────────────────────
    public async Task<ServiceResponse<List<PersonDto>>> GetDeletedAsync(
        CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var familyId = currentUser.FamilyId;
            var isSuperUser = currentUser.IsSuperUser;

            var people = await ctx.People
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(p => p.DeletedAt != null &&
                            (isSuperUser || (familyId.HasValue && p.FamilyId == familyId)))
                .OrderByDescending(p => p.DeletedAt)
                .ToListAsync(ct);

            var dtos = people
                .Select(p => PersonMapper.MapPersonToDto(p, []))
                .ToList();

            return ServiceResponse<List<PersonDto>>.Ok(dtos);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading deleted people");
            return ServiceResponse<List<PersonDto>>.Fail("An error occurred loading deleted people.");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  RELATIONSHIP SYNC (tuple‑free, Guid‑safe)
    // ─────────────────────────────────────────────────────────────
    private static async Task SyncRelationshipsAsync(
        AppDbContext ctx,
        Guid personId,
        PersonUpsertDto dto,
        Guid? createdBy,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // Parents
        foreach (var parentId in dto.ParentIds)
        {
            ctx.Relationships.Add(new Relationship
            {
                PersonAId = parentId,
                PersonBId = personId,
                Type = RelationshipType.Parent,
                CreatedAt = now,
                CreatedBy = createdBy,
            });
        }

        // Children — personId is the parent
        foreach (var childId in dto.ChildIds)
        {
            ctx.Relationships.Add(new Relationship
            {
                PersonAId = personId,
                PersonBId = childId,
                Type = RelationshipType.Parent,
                CreatedAt = now,
                CreatedBy = createdBy,
            });
        }

        // Siblings (canonical ordering — same as spouse dedup)
        foreach (var siblingId in dto.SiblingIds)
        {
            var ordered = new[] { personId, siblingId }.OrderBy(g => g).ToArray();
            var a = ordered[0];
            var b = ordered[1];

            var exists = await ctx.Relationships.AnyAsync(
                r => r.PersonAId == a &&
                     r.PersonBId == b &&
                     r.Type == RelationshipType.Sibling,
                ct);

            if (!exists)
            {
                ctx.Relationships.Add(new Relationship
                {
                    PersonAId = a,
                    PersonBId = b,
                    Type = RelationshipType.Sibling,
                    CreatedAt = now,
                    CreatedBy = createdBy,
                });
            }
        }

        // Active spouses (canonical ordering)
        foreach (var spouseId in dto.SpouseIds)
        {
            var ordered = new[] { personId, spouseId }.OrderBy(g => g).ToArray();
            var a = ordered[0]; var b = ordered[1];

            var exists = await ctx.Relationships.AnyAsync(
                r => r.PersonAId == a && r.PersonBId == b && r.Type == RelationshipType.Spouse, ct);

            if (!exists)
            {
                ctx.Relationships.Add(new Relationship
                {
                    PersonAId = a, PersonBId = b,
                    Type = RelationshipType.Spouse,
                    EndDate = null,
                    CreatedAt = now,
                    CreatedBy = createdBy,
                });
            }
        }

        // Former spouses (Spouse rel with IsEnded set)
        foreach (var formerSpouseId in dto.FormerSpouseIds)
        {
            if (dto.SpouseIds.Contains(formerSpouseId)) continue;
            var ordered = new[] { personId, formerSpouseId }.OrderBy(g => g).ToArray();
            var a = ordered[0]; var b = ordered[1];

            var exists = await ctx.Relationships.AnyAsync(
                r => r.PersonAId == a && r.PersonBId == b && r.Type == RelationshipType.Spouse, ct);

            if (!exists)
            {
                ctx.Relationships.Add(new Relationship
                {
                    PersonAId = a, PersonBId = b,
                    Type = RelationshipType.Spouse,
                    IsEnded = true,
                    EndDate = await ComputeFormerSpouseEndDateAsync(ctx, dto, formerSpouseId, ct),
                    CreatedAt = now,
                    CreatedBy = createdBy,
                });
            }
        }
    }

    private static async Task SyncRelationshipsDiffAsync(
    AppDbContext ctx,
    Guid personId,
    PersonUpsertDto dto,
    Guid? createdBy,
    CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var existing = await ctx.Relationships
            .Where(r => r.PersonAId == personId || r.PersonBId == personId)
            .ToListAsync(ct);

        // ---------------------------
        // PARENTS
        // ---------------------------
        var existingParents = existing
            .Where(r => r.Type == RelationshipType.Parent && r.PersonBId == personId)
            .Select(r => r.PersonAId)
            .ToHashSet();

        var toAddParents = dto.ParentIds.Except(existingParents);
        var toRemoveParents = existingParents.Except(dto.ParentIds);

        foreach (var parentId in toAddParents)
        {
            ctx.Relationships.Add(new Relationship
            {
                PersonAId = parentId,
                PersonBId = personId,
                Type = RelationshipType.Parent,
                CreatedAt = now,
                CreatedBy = createdBy,
            });
        }

        foreach (var parentId in toRemoveParents)
        {
            var rel = existing.First(r =>
                r.Type == RelationshipType.Parent &&
                r.PersonAId == parentId &&
                r.PersonBId == personId);

            ctx.Relationships.Remove(rel);
        }

        // ---------------------------
        // CHILDREN
        // ---------------------------
        var existingChildren = existing
            .Where(r => r.Type == RelationshipType.Parent && r.PersonAId == personId)
            .Select(r => r.PersonBId)
            .ToHashSet();

        var toAddChildren = dto.ChildIds.Except(existingChildren);
        var toRemoveChildren = existingChildren.Except(dto.ChildIds);

        foreach (var childId in toAddChildren)
        {
            ctx.Relationships.Add(new Relationship
            {
                PersonAId = personId,
                PersonBId = childId,
                Type = RelationshipType.Parent,
                CreatedAt = now,
                CreatedBy = createdBy,
            });
        }

        foreach (var childId in toRemoveChildren)
        {
            var rel = existing.First(r =>
                r.Type == RelationshipType.Parent &&
                r.PersonAId == personId &&
                r.PersonBId == childId);

            ctx.Relationships.Remove(rel);
        }

        // ---------------------------
        // SIBLINGS (explicit only)
        // ---------------------------
        var existingSiblings = existing
            .Where(r => r.Type == RelationshipType.Sibling)
            .Select(r => r.PersonAId == personId ? r.PersonBId : r.PersonAId)
            .ToHashSet();

        var toAddSiblings = dto.SiblingIds.Except(existingSiblings);
        var toRemoveSiblings = existingSiblings.Except(dto.SiblingIds);

        foreach (var sibId in toAddSiblings)
        {
            var ordered = new[] { personId, sibId }.OrderBy(x => x).ToArray();
            ctx.Relationships.Add(new Relationship
            {
                PersonAId = ordered[0],
                PersonBId = ordered[1],
                Type = RelationshipType.Sibling,
                CreatedAt = now,
                CreatedBy = createdBy,
            });
        }

        foreach (var sibId in toRemoveSiblings)
        {
            var ordered = new[] { personId, sibId }.OrderBy(x => x).ToArray();
            var rel = existing.First(r =>
                r.Type == RelationshipType.Sibling &&
                r.PersonAId == ordered[0] &&
                r.PersonBId == ordered[1]);

            ctx.Relationships.Remove(rel);
        }

        // ---------------------------
        // SPOUSES (active + former)
        // ---------------------------
        var existingSpouseRels = existing.Where(r => r.Type == RelationshipType.Spouse).ToList();

        var existingByPartner = existingSpouseRels.ToDictionary(
            r => r.PersonAId == personId ? r.PersonBId : r.PersonAId,
            r => r);

        var allDesiredPartnerIds = dto.SpouseIds.Concat(dto.FormerSpouseIds).ToHashSet();

        // Delete rels no longer desired
        foreach (var partnerId in existingByPartner.Keys.Except(allDesiredPartnerIds).ToList())
            ctx.Relationships.Remove(existingByPartner[partnerId]);

        // Upsert active spouses (not ended, no EndDate)
        foreach (var spouseId in dto.SpouseIds)
        {
            if (existingByPartner.TryGetValue(spouseId, out var rel))
            {
                if (rel.IsEnded || rel.EndDate != null) { rel.IsEnded = false; rel.EndDate = null; rel.UpdatedAt = now; }
            }
            else
            {
                var ordered = new[] { personId, spouseId }.OrderBy(x => x).ToArray();
                ctx.Relationships.Add(new Relationship
                {
                    PersonAId = ordered[0], PersonBId = ordered[1],
                    Type = RelationshipType.Spouse, EndDate = null,
                    CreatedAt = now, CreatedBy = createdBy,
                });
            }
        }

        // Upsert former spouses (IsEnded set)
        foreach (var formerSpouseId in dto.FormerSpouseIds)
        {
            if (dto.SpouseIds.Contains(formerSpouseId)) continue;
            if (existingByPartner.TryGetValue(formerSpouseId, out var rel))
            {
                if (!rel.IsEnded && rel.EndDate == null)
                {
                    rel.IsEnded = true;
                    rel.EndDate = await ComputeFormerSpouseEndDateAsync(ctx, dto, formerSpouseId, ct);
                    rel.UpdatedAt = now;
                }
            }
            else
            {
                var ordered = new[] { personId, formerSpouseId }.OrderBy(x => x).ToArray();
                ctx.Relationships.Add(new Relationship
                {
                    PersonAId = ordered[0], PersonBId = ordered[1],
                    Type = RelationshipType.Spouse,
                    IsEnded = true,
                    EndDate = await ComputeFormerSpouseEndDateAsync(ctx, dto, formerSpouseId, ct),
                    CreatedAt = now, CreatedBy = createdBy,
                });
            }
        }
    }

    // The form has no end-date field, so an ended marriage's EndDate is only ever a
    // default. Prefers the earlier of the two partners' death dates (the marriage
    // ended when the first partner died; Lillian died in 1922, so "ended 2026" was
    // wrong). With no recorded death it stays null: IsEnded says the marriage ended
    // and nobody has said when. Until 2026-10-24 this fell back to today, which made
    // up a date for every divorce (repaired by migration ClearInventedMarriageEndDates).
    private static async Task<DateOnly?> ComputeFormerSpouseEndDateAsync(
        AppDbContext ctx, PersonUpsertDto dto, Guid partnerId, CancellationToken ct)
    {
        var partnerDeathDate = await ctx.People.AsNoTracking()
            .Where(p => p.Id == partnerId)
            .Select(p => p.DeathDate)
            .FirstOrDefaultAsync(ct);

        var earliest = dto.DeathDate;
        if (partnerDeathDate.HasValue && (!earliest.HasValue || partnerDeathDate.Value < earliest.Value))
            earliest = partnerDeathDate;

        return earliest;
    }

    private static string? ValidatePersonDates(PersonUpsertDto dto)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var yearBound = new DateOnly(1800, 1, 1);

        if (dto.BirthDate.HasValue && dto.BirthDate.Value > today)
            return "Birth date cannot be in the future.";
        if (dto.BirthDate.HasValue && dto.BirthDate.Value < yearBound)
            return "Birth date cannot be before 1800.";
        if (dto.DeathDate.HasValue && dto.DeathDate.Value > today)
            return "Death date cannot be in the future.";
        if (dto.BirthDate.HasValue && dto.DeathDate.HasValue && dto.BirthDate.Value >= dto.DeathDate.Value)
            return "Birth date must be before death date.";
        return null;
    }

    private static string? ValidatePersonNames(PersonUpsertDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.FirstName))
            return "First name cannot be empty or whitespace.";
        if (string.IsNullOrWhiteSpace(dto.LastName))
            return "Last name cannot be empty or whitespace.";
        return null;
    }

    // Refuses a parent or child link whose dates are impossible (PlausibilityRules errors;
    // its warnings are the form's to show). Checks the person's children as well as their
    // parents: until 2026-10-25 only parents were checked, so a parent's death date, or a
    // child added from the parent's own form, went unchecked. Judges only the links this
    // save adds, unless the person's own dates or gender changed, so someone whose existing
    // data already breaks a rule can still be edited (the same reasoning as the loop check).
    private static async Task<string?> ValidateParentChildDatesAsync(
        AppDbContext ctx, Guid personId, PersonUpsertDto dto, bool datesChanged, CancellationToken ct)
    {
        var parentIds = dto.ParentIds.Distinct().ToList();
        var childIds = dto.ChildIds.Distinct().ToList();
        if (parentIds.Count == 0 && childIds.Count == 0) return null;
        // Every rule needs one of this person's dates.
        if (!dto.BirthDate.HasValue && !dto.DeathDate.HasValue) return null;

        if (!datesChanged)
        {
            var existing = await ctx.Relationships.AsNoTracking()
                .Where(r => r.Type == RelationshipType.Parent && (r.PersonAId == personId || r.PersonBId == personId))
                .Select(r => new { Parent = r.PersonAId, Child = r.PersonBId })
                .ToListAsync(ct);
            parentIds = parentIds.Where(p => !existing.Any(e => e.Parent == p && e.Child == personId)).ToList();
            childIds = childIds.Where(c => !existing.Any(e => e.Parent == personId && e.Child == c)).ToList();
            if (parentIds.Count == 0 && childIds.Count == 0) return null;
        }

        var relativeIds = parentIds.Concat(childIds).ToList();
        var relatives = await ctx.People.AsNoTracking()
            .Where(p => relativeIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var subject = new LifeDates($"{dto.FirstName} {dto.LastName}".Trim(), dto.BirthDate, dto.DeathDate, dto.Gender);
        var findings = parentIds.Where(relatives.ContainsKey)
            .SelectMany(p => PlausibilityRules.CheckParentChild(ToLifeDates(relatives[p]), subject))
            .Concat(childIds.Where(relatives.ContainsKey)
                .SelectMany(c => PlausibilityRules.CheckParentChild(subject, ToLifeDates(relatives[c]))));

        return findings.FirstOrDefault(f => f.Severity == PlausibilitySeverity.Error)?.Message;
    }

    internal static LifeDates ToLifeDates(Person p) =>
        new($"{p.FirstName} {p.LastName}".Trim(), p.BirthDate, p.DeathDate, p.Gender);

    // Refuses a save that would make someone their own ancestor (R0, docs/TodoList.md): a Parent
    // loop sends the layout engine into unbounded recursion. Judges the tree as it will be AFTER
    // the save (this person's old parent and child links replaced by the DTO's), and only the
    // links the save ADDS, so a person caught in a loop that predates this check can still be
    // edited, and can fix it by removing the bad link.
    private static async Task<string?> ValidateNoParentLoopAsync(
        AppDbContext ctx, Guid personId, string personName, PersonUpsertDto dto, CancellationToken ct)
    {
        var parentIds = dto.ParentIds.Distinct().ToList();
        var childIds = dto.ChildIds.Distinct().ToList();
        if (parentIds.Count == 0 && childIds.Count == 0) return null;

        var edges = await ctx.Relationships.AsNoTracking()
            .Where(r => r.Type == RelationshipType.Parent)
            .Select(r => new { Parent = r.PersonAId, Child = r.PersonBId })
            .ToListAsync(ct);

        var existingParents = edges.Where(e => e.Child == personId).Select(e => e.Parent).ToHashSet();
        var existingChildren = edges.Where(e => e.Parent == personId).Select(e => e.Child).ToHashSet();
        var addedParents = parentIds.Where(p => !existingParents.Contains(p)).ToList();
        var addedChildren = childIds.Where(c => !existingChildren.Contains(c)).ToList();
        if (addedParents.Count == 0 && addedChildren.Count == 0) return null;

        var after = new ParentGraph(edges
            .Where(e => e.Parent != personId && e.Child != personId)
            .Select(e => (e.Parent, e.Child))
            .Concat(parentIds.Select(p => (p, personId)))
            .Concat(childIds.Select(c => (personId, c))));
        var descendants = after.Descendants(personId);
        var ancestors = after.Ancestors(personId);

        Guid? badParent = addedParents.Cast<Guid?>().FirstOrDefault(p => p == personId || descendants.Contains(p!.Value));
        Guid? badChild = badParent is null
            ? addedChildren.Cast<Guid?>().FirstOrDefault(c => c == personId || ancestors.Contains(c!.Value))
            : null;
        var culprit = badParent ?? badChild;
        if (culprit is null) return null;

        if (culprit == personId)
            return "A person can't be their own parent or child. That would create a loop in the family tree.";

        var other = await ctx.People.AsNoTracking()
            .Where(p => p.Id == culprit)
            .Select(p => p.FirstName + " " + p.LastName)
            .FirstOrDefaultAsync(ct) ?? "That person";

        return badParent is not null
            ? $"{other} is already {personName}'s descendant, so they can't also be {personName}'s parent. " +
              "That would create a loop in the family tree."
            : $"{other} is already {personName}'s ancestor, so they can't also be {personName}'s child. " +
              "That would create a loop in the family tree.";
    }

    private static async Task<string?> ValidateRestoredParentLinksAsync(
        AppDbContext ctx, List<Relationship> restoring, CancellationToken ct)
    {
        var restoredParentLinks = restoring.Where(r => r.Type == RelationshipType.Parent).ToList();
        if (restoredParentLinks.Count == 0) return null;

        // Live links plus the ones coming back; the default query filter hides soft-deleted rows.
        var graph = new ParentGraph(
            (await ctx.Relationships.AsNoTracking()
                .Where(r => r.Type == RelationshipType.Parent)
                .Select(r => new { r.PersonAId, r.PersonBId })
                .ToListAsync(ct))
            .Select(r => (r.PersonAId, r.PersonBId))
            .Concat(restoredParentLinks.Select(r => (r.PersonAId, r.PersonBId))));

        var bad = restoredParentLinks.FirstOrDefault(r =>
            r.PersonAId == r.PersonBId || graph.Descendants(r.PersonBId).Contains(r.PersonAId));
        if (bad is null) return null;

        var names = await ctx.People.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Id == bad.PersonAId || p.Id == bad.PersonBId)
            .ToDictionaryAsync(p => p.Id, p => $"{p.FirstName} {p.LastName}", ct);
        return $"Restoring this person would put {names.GetValueOrDefault(bad.PersonAId, "someone")} and " +
               $"{names.GetValueOrDefault(bad.PersonBId, "someone")} in a loop of each other's ancestors, " +
               "because the family tree has changed since they were deleted. Fix the conflicting parent link first.";
    }

    private static async Task<string?> ValidateSpouseRelationshipsAsync(
        AppDbContext ctx, Guid personId, PersonUpsertDto dto, CancellationToken ct)
    {
        foreach (var spouseId in dto.SpouseIds)
        {
            var spouse = await ctx.People
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == spouseId, ct);
            if (spouse == null) continue;

            var hasOtherActiveMarriage = await ctx.Relationships.AnyAsync(
                r => (r.PersonAId == spouseId || r.PersonBId == spouseId) &&
                     r.Type == RelationshipType.Spouse &&
                     r.PersonAId != personId &&
                     r.PersonBId != personId &&
                     !r.IsEnded && r.EndDate == null, ct);

            if (hasOtherActiveMarriage)
                return $"{spouse.FirstName} {spouse.LastName} is already in an active marriage.";
        }
        return null;
    }
}
