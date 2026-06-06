using FamilyTree.Core.Data;
using FamilyTree.Core.Mappers;
using FamilyTree.Core.Models;
using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Core.Services;

public class PersonService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<PersonService> logger) : IPersonService
{
    // ─────────────────────────────────────────────────────────────
    //  GET ALL
    // ─────────────────────────────────────────────────────────────
    public async Task<ServiceResponse<List<PersonDto>>> GetAllAsync(
        CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var people = await ctx.People
                .AsNoTracking()
                .OrderBy(p => p.LastName)
                .ThenBy(p => p.FirstName)
                .ToListAsync(ct);

            var relationships = await ctx.Relationships
                .AsNoTracking()
                .ToListAsync(ct);

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

    // ─────────────────────────────────────────────────────────────
    //  GET BY ID
    // ─────────────────────────────────────────────────────────────
    public async Task<ServiceResponse<PersonDto>> GetByIdAsync(
        Guid id, CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var person = await ctx.People
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id, ct);

            if (person is null)
                return ServiceResponse<PersonDto>.Fail($"Person {id} not found.");

            var relationships = await ctx.Relationships
                .AsNoTracking()
                .Where(r => r.PersonAId == id || r.PersonBId == id)
                .ToListAsync(ct);

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

            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var dateError = ValidatePersonDates(dto);
            if (dateError != null) return ServiceResponse<PersonDto>.Fail(dateError);

            var nameError = ValidatePersonNames(dto);
            if (nameError != null) return ServiceResponse<PersonDto>.Fail(nameError);

            var parentError = await ValidateParentRelationshipsAsync(ctx, dto, ct);
            if (parentError != null) return ServiceResponse<PersonDto>.Fail(parentError);

            var spouseError = await ValidateSpouseRelationshipsAsync(ctx, Guid.Empty, dto, ct);
            if (spouseError != null) return ServiceResponse<PersonDto>.Fail(spouseError);

            var now = DateTime.UtcNow;

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
                CreatedAt = now,
                UpdatedAt = now
            };

            ctx.People.Add(person);
            await ctx.SaveChangesAsync(ct);

            await SyncRelationshipsAsync(ctx, person.Id, dto, ct);
            await ctx.SaveChangesAsync(ct);

            var relationships = await ctx.Relationships
                .AsNoTracking()
                .Where(r => r.PersonAId == person.Id || r.PersonBId == person.Id)
                .ToListAsync(ct);

            return ServiceResponse<PersonDto>.Ok(PersonMapper.MapPersonToDto(person, relationships));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating person");
            return ServiceResponse<PersonDto>.Fail(
                "An error occurred creating this person.");
        }
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
            if (!dtoValidation.Success) return ServiceResponse<PersonDto>.Fail(dtoValidation.Message);

            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var person = await ctx.People
                .FirstOrDefaultAsync(p => p.Id == id, ct);

            if (person is null)
                return ServiceResponse<PersonDto>.Fail($"Person {id} not found.");

            var dateError = ValidatePersonDates(dto);
            if (dateError != null) return ServiceResponse<PersonDto>.Fail(dateError);

            var nameError = ValidatePersonNames(dto);
            if (nameError != null) return ServiceResponse<PersonDto>.Fail(nameError);

            var parentError = await ValidateParentRelationshipsAsync(ctx, dto, ct);
            if (parentError != null) return ServiceResponse<PersonDto>.Fail(parentError);

            var spouseError = await ValidateSpouseRelationshipsAsync(ctx, id, dto, ct);
            if (spouseError != null) return ServiceResponse<PersonDto>.Fail(spouseError);

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
            person.UpdatedAt = DateTime.UtcNow;

            // Remove old relationships
            var existing = await ctx.Relationships
                .Where(r => r.PersonAId == id || r.PersonBId == id)
                .ToListAsync(ct);

            ctx.Relationships.RemoveRange(existing);
            await ctx.SaveChangesAsync(ct);

            // Add new ones
            await SyncRelationshipsAsync(ctx, id, dto, ct);
            await ctx.SaveChangesAsync(ct);

            var relationships = await ctx.Relationships
                .AsNoTracking()
                .Where(r => r.PersonAId == id || r.PersonBId == id)
                .ToListAsync(ct);

            return ServiceResponse<PersonDto>.Ok(PersonMapper.MapPersonToDto(person, relationships));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating person {Id}", id);
            return ServiceResponse<PersonDto>.Fail(
                "An error occurred updating this person.");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  DELETE
    // ─────────────────────────────────────────────────────────────
    public async Task<ServiceResponse> DeleteAsync(
        Guid id, CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var person = await ctx.People
                .FirstOrDefaultAsync(p => p.Id == id, ct);

            if (person is null)
                return ServiceResponse.Fail($"Person {id} not found.");

            var relationships = await ctx.Relationships
                .Where(r => r.PersonAId == id || r.PersonBId == id)
                .ToListAsync(ct);

            ctx.Relationships.RemoveRange(relationships);
            ctx.People.Remove(person);

            await ctx.SaveChangesAsync(ct);

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
    //  RELATIONSHIP SYNC (tuple‑free, Guid‑safe)
    // ─────────────────────────────────────────────────────────────
    private static async Task SyncRelationshipsAsync(
        AppDbContext ctx,
        Guid personId,
        PersonUpsertDto dto,
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
                CreatedAt = now
            });
        }

        // Spouses (canonical ordering)
        foreach (var spouseId in dto.SpouseIds)
        {
            var ordered = new[] { personId, spouseId }.OrderBy(g => g).ToArray();
            var a = ordered[0];
            var b = ordered[1];

            var exists = await ctx.Relationships.AnyAsync(
                r => r.PersonAId == a &&
                     r.PersonBId == b &&
                     r.Type == RelationshipType.Spouse,
                ct);

            if (!exists)
            {
                ctx.Relationships.Add(new Relationship
                {
                    PersonAId = a,
                    PersonBId = b,
                    Type = RelationshipType.Spouse,
                    CreatedAt = now
                });
            }
        }
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

    private static async Task<string?> ValidateParentRelationshipsAsync(
        AppDbContext ctx, PersonUpsertDto dto, CancellationToken ct)
    {
        if (dto.ParentIds.Count == 0) return null;

        var parents = await ctx.People
            .AsNoTracking()
            .Where(p => dto.ParentIds.Contains(p.Id))
            .ToListAsync(ct);

        foreach (var parent in parents)
        {
            if (parent.DeathDate.HasValue && dto.BirthDate.HasValue &&
                dto.BirthDate.Value > parent.DeathDate.Value.AddMonths(9))
                return $"{parent.FirstName} {parent.LastName} died on {parent.DeathDate:d} " +
                       $"and cannot be the parent of a child born on {dto.BirthDate:d}.";

            if (parent.BirthDate.HasValue && dto.BirthDate.HasValue)
            {
                var ageGap = dto.BirthDate.Value.Year - parent.BirthDate.Value.Year;
                if (ageGap < 12)
                    return $"{parent.FirstName} {parent.LastName} would have been {ageGap} year(s) old " +
                           "at this child's birth — too young to be a parent.";
                if (ageGap > 80)
                    return $"{parent.FirstName} {parent.LastName} would have been {ageGap} years old " +
                           "at this child's birth — too large an age gap.";
            }
        }

        return null;
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
                     r.EndDate == null, ct);

            if (hasOtherActiveMarriage)
                return $"{spouse.FirstName} {spouse.LastName} is already in an active marriage.";
        }
        return null;
    }
}