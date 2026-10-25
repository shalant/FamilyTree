using FamilyTree.Core.Models;
using FamilyTree.Core.Services;
using FamilyTree.Core.Tests.Helpers;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Shared.DTOs.Relationship;
using FamilyTree.Shared.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FamilyTree.Core.Tests;

// Outreach readiness A3 (2026-10-25): the services refuse impossible parent/child dates
// (PlausibilityRules errors) and allow the implausible ones, which the form warns about.
public class EntryGuardrailServiceTests
{
    private readonly Guid _familyId = Guid.NewGuid();
    private readonly TestDbContextFactory _db = new();
    private readonly FakeCurrentUserService _admin;

    public EntryGuardrailServiceTests() =>
        _admin = new FakeCurrentUserService { UserId = Guid.NewGuid(), FamilyId = _familyId, IsAdmin = true };

    private PersonService People() =>
        new(_db, NullLogger<PersonService>.Instance, new FakeAuditLogService(), _admin);

    private RelationshipService Relationships() =>
        new(_db, NullLogger<RelationshipService>.Instance, new FakeAuditLogService(), _admin);

    private async Task<Guid> SeedAsync(string first, DateOnly? born, DateOnly? died = null, Gender gender = Gender.Unknown)
    {
        await using var ctx = _db.CreateDbContext();
        var p = new Person
        {
            Id = Guid.NewGuid(), FirstName = first, LastName = "Test", FamilyId = _familyId,
            BirthDate = born, DeathDate = died, Gender = gender, CreatedAt = DateTime.UtcNow,
        };
        ctx.People.Add(p);
        await ctx.SaveChangesAsync();
        return p.Id;
    }

    private async Task LinkAsync(Guid parent, Guid child)
    {
        await using var ctx = _db.CreateDbContext();
        ctx.Relationships.Add(new Relationship
        {
            Id = Guid.NewGuid(), PersonAId = parent, PersonBId = child, Type = RelationshipType.Parent, CreatedAt = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync();
    }

    private async Task<PersonUpsertDto> DtoOfAsync(Guid id)
    {
        var p = (await People().GetByIdAsync(id)).Data!;
        return new PersonUpsertDto
        {
            FirstName = p.FirstName, LastName = p.LastName, BirthDate = p.BirthDate, DeathDate = p.DeathDate,
            Gender = p.Gender, ParentIds = [.. p.ParentIds ?? []], ChildIds = [.. p.ChildIds ?? []],
            SpouseIds = [.. p.SpouseIds ?? []], FormerSpouseIds = [.. p.FormerSpouseIds ?? []], SiblingIds = [.. p.SiblingIds ?? []],
        };
    }

    [Fact]
    public async Task CreateAsync_ChildBornAfterTheirMothersDeath_IsRefused()
    {
        var mother = await SeedAsync("Mother", new DateOnly(1920, 1, 1), new DateOnly(1949, 1, 1), Gender.Female);

        var result = await People().CreateAsync(new PersonUpsertDto
        {
            FirstName = "Kid", LastName = "Test", BirthDate = new DateOnly(1950, 6, 15), ParentIds = [mother],
        });

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("can't be Kid Test's mother");
    }

    [Fact]
    public async Task CreateAsync_ParentAgedTen_IsAllowed_TheFormWarnsInstead()
    {
        // Refused outright until 2026-10-25; now "implausible", so the form asks instead.
        var parent = await SeedAsync("Parent", new DateOnly(1940, 1, 1));

        var result = await People().CreateAsync(new PersonUpsertDto
        {
            FirstName = "Kid", LastName = "Test", BirthDate = new DateOnly(1950, 6, 15), ParentIds = [parent],
        });

        result.Success.Should().BeTrue(result.Message);
    }

    // Until 2026-10-25 PersonService checked only a person's parents, never their children,
    // so a mother's death date could be moved to before her child's birth.
    [Fact]
    [Trait("Category", "Regression")]
    public async Task UpdateAsync_MothersDeathMovedBeforeHerChildsBirth_IsRefused()
    {
        var mother = await SeedAsync("Mother", new DateOnly(1920, 1, 1), null, Gender.Female);
        var kid = await SeedAsync("Kid", new DateOnly(1950, 6, 15));
        await LinkAsync(mother, kid);

        var dto = await DtoOfAsync(mother);
        dto.DeathDate = new DateOnly(1949, 1, 1);
        var result = await People().UpdateAsync(mother, dto);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("can't be Kid Test's mother");
    }

    [Fact]
    public async Task UpdateAsync_AddingAChildOlderThanTheParent_IsRefused()
    {
        var parent = await SeedAsync("Parent", new DateOnly(1950, 1, 1));
        var older = await SeedAsync("Older", new DateOnly(1940, 1, 1));

        var dto = await DtoOfAsync(parent);
        dto.ChildIds.Add(older);
        var result = await People().UpdateAsync(parent, dto);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("can't be Older Test's parent");
    }

    [Fact]
    public async Task UpdateAsync_ExistingImpossibleLink_DoesNotBlockAnUnrelatedEdit()
    {
        // Saved before these rules: the child is born after the mother's death.
        var mother = await SeedAsync("Mother", new DateOnly(1920, 1, 1), new DateOnly(1949, 1, 1), Gender.Female);
        var kid = await SeedAsync("Kid", new DateOnly(1950, 6, 15));
        await LinkAsync(mother, kid);

        var dto = await DtoOfAsync(kid);
        dto.BiographyNotes = "Grew up in Brooklyn.";
        var result = await People().UpdateAsync(kid, dto);

        result.Success.Should().BeTrue(result.Message);
    }

    [Fact]
    public async Task RelationshipCreate_ChildBornAfterTheMothersDeath_IsRefused_ButAYoungParentIsAllowed()
    {
        var mother = await SeedAsync("Mother", new DateOnly(1920, 1, 1), new DateOnly(1949, 1, 1), Gender.Female);
        var young = await SeedAsync("Young", new DateOnly(1940, 1, 1));
        var kid = await SeedAsync("Kid", new DateOnly(1950, 6, 15));

        var refused = await Relationships().CreateAsync(new RelationshipUpsertDto
        {
            PersonAId = mother, PersonBId = kid, Type = RelationshipType.Parent,
        });
        var allowed = await Relationships().CreateAsync(new RelationshipUpsertDto
        {
            PersonAId = young, PersonBId = kid, Type = RelationshipType.Parent,
        });

        refused.Success.Should().BeFalse();
        refused.Message.Should().Contain("can't be Kid Test's mother");
        allowed.Success.Should().BeTrue(allowed.Message);
    }
}
