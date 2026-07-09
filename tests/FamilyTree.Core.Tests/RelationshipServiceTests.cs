using FamilyTree.Core.Models;
using FamilyTree.Core.Services;
using FamilyTree.Core.Tests.Helpers;
using Xunit;
using FamilyTree.Shared.DTOs.Relationship;
using FamilyTree.Shared.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FamilyTree.Core.Tests;

public class RelationshipServiceTests
{
    private static readonly Guid LowerGuid  = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid HigherGuid = Guid.Parse("00000000-0000-0000-0000-000000000002");

    // Super-user by default: these tests exercise canonical ordering / duplicate
    // detection using bare GUIDs with no backing Person rows, so the family-scoping
    // check (which requires both people to actually exist in the caller's family)
    // would otherwise fail them for an unrelated reason. Scoping itself is covered by
    // FamilyScopingTests below.
    private static (RelationshipService service, TestDbContextFactory factory, FakeCurrentUserService fakeUser)
        CreateSut(bool isSuperUser = true, Guid? familyId = null)
    {
        var factory = new TestDbContextFactory();
        var fakeUser = new FakeCurrentUserService { IsSuperUser = isSuperUser, FamilyId = familyId };
        var service = new RelationshipService(
            factory,
            NullLogger<RelationshipService>.Instance,
            new FakeAuditLogService(),
            fakeUser);
        return (service, factory, fakeUser);
    }

    [Fact]
    public async Task CreateAsync_AlwaysStoresLowerGuidAsPersonA()
    {
        var (service, factory, _) = CreateSut();

        // Pass in reversed order: higher as PersonA, lower as PersonB
        var result = await service.CreateAsync(new RelationshipUpsertDto
        {
            PersonAId = HigherGuid,
            PersonBId = LowerGuid,
            Type = RelationshipType.Sibling,
        });

        result.Success.Should().BeTrue();
        await using var ctx = factory.CreateDbContext();
        var rel = ctx.Relationships.Single();
        rel.PersonAId.Should().Be(LowerGuid);
        rel.PersonBId.Should().Be(HigherGuid);
    }

    [Fact]
    public async Task CreateAsync_DuplicateRelationship_ReturnsFail()
    {
        var (service, _, _) = CreateSut();
        var dto = new RelationshipUpsertDto
        {
            PersonAId = LowerGuid,
            PersonBId = HigherGuid,
            Type = RelationshipType.Sibling,
        };

        var first = await service.CreateAsync(dto);
        var second = await service.CreateAsync(dto);

        first.Success.Should().BeTrue();
        second.Success.Should().BeFalse();
        second.Message.Should().Contain("already exists");
    }

    [Fact]
    public async Task DeleteAsync_RemovesRelationship()
    {
        var (service, factory, _) = CreateSut();
        var created = await service.CreateAsync(new RelationshipUpsertDto
        {
            PersonAId = LowerGuid,
            PersonBId = HigherGuid,
            Type = RelationshipType.Sibling,
        });

        var result = await service.DeleteAsync(created.Data!.Id);

        result.Success.Should().BeTrue();
        await using var ctx = factory.CreateDbContext();
        ctx.Relationships.Should().BeEmpty();
    }
}

// Regression tests for the family-scoping gap found 2026-07-04: RelationshipService
// had no FamilyId checks at all — any authenticated user who knew two GUIDs from
// different families could link them together, or read/modify a relationship
// belonging entirely to someone else's family.
public class RelationshipServiceFamilyScopingTests
{
    private static (RelationshipService service, TestDbContextFactory factory, FakeCurrentUserService fakeUser)
        CreateSut(bool isSuperUser = false, Guid? familyId = null)
    {
        var factory = new TestDbContextFactory();
        var fakeUser = new FakeCurrentUserService { IsSuperUser = isSuperUser, FamilyId = familyId };
        var service = new RelationshipService(
            factory,
            NullLogger<RelationshipService>.Instance,
            new FakeAuditLogService(),
            fakeUser);
        return (service, factory, fakeUser);
    }

    private static Person MakePerson(Guid? familyId) => new()
    {
        Id = Guid.NewGuid(), FirstName = "Test", LastName = "Person", FamilyId = familyId,
    };

    [Fact]
    public async Task CreateAsync_BothPeopleInCallersFamily_Succeeds()
    {
        var familyId = Guid.NewGuid();
        var (service, factory, _) = CreateSut(familyId: familyId);
        var a = MakePerson(familyId);
        var b = MakePerson(familyId);
        await using (var ctx = factory.CreateDbContext())
        {
            ctx.People.AddRange(a, b);
            await ctx.SaveChangesAsync();
        }

        var result = await service.CreateAsync(new RelationshipUpsertDto
        {
            PersonAId = a.Id, PersonBId = b.Id, Type = RelationshipType.Sibling,
        });

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_OnePersonFromDifferentFamily_Fails()
    {
        var myFamily = Guid.NewGuid();
        var otherFamily = Guid.NewGuid();
        var (service, factory, _) = CreateSut(familyId: myFamily);
        var mine = MakePerson(myFamily);
        var theirs = MakePerson(otherFamily);
        await using (var ctx = factory.CreateDbContext())
        {
            ctx.People.AddRange(mine, theirs);
            await ctx.SaveChangesAsync();
        }

        var result = await service.CreateAsync(new RelationshipUpsertDto
        {
            PersonAId = mine.Id, PersonBId = theirs.Id, Type = RelationshipType.Sibling,
        });

        result.Success.Should().BeFalse(
            "linking two people from different families must be blocked, not just filtered from lists later");
    }

    [Fact]
    public async Task GetAllAsync_OnlyReturnsRelationshipsWithinCallersFamily()
    {
        var myFamily = Guid.NewGuid();
        var otherFamily = Guid.NewGuid();
        var (service, factory, _) = CreateSut(isSuperUser: true, familyId: myFamily);

        var a = MakePerson(myFamily);
        var b = MakePerson(myFamily);
        var x = MakePerson(otherFamily);
        var y = MakePerson(otherFamily);
        await using (var ctx = factory.CreateDbContext())
        {
            ctx.People.AddRange(a, b, x, y);
            await ctx.SaveChangesAsync();
        }

        // Created as super-user (bypasses the create-time cross-family guard) so the
        // "other family" relationship can exist at all to test read-scoping against.
        await service.CreateAsync(new RelationshipUpsertDto { PersonAId = a.Id, PersonBId = b.Id, Type = RelationshipType.Sibling });
        await service.CreateAsync(new RelationshipUpsertDto { PersonAId = x.Id, PersonBId = y.Id, Type = RelationshipType.Sibling });

        // Point a regular (non-super) user's service at the same underlying data.
        var scopedService = new RelationshipService(
            factory, NullLogger<RelationshipService>.Instance, new FakeAuditLogService(),
            new FakeCurrentUserService { FamilyId = myFamily });

        var result = await scopedService.GetAllAsync();

        result.Success.Should().BeTrue();
        result.Data.Should().ContainSingle(
            "only the relationship between two people in the caller's own family should be visible");
    }

    [Fact]
    public async Task DeleteAsync_RelationshipInDifferentFamily_ReturnsGenericNotFound()
    {
        var otherFamily = Guid.NewGuid();
        var (superUserService, factory, _) = CreateSut(isSuperUser: true);
        var x = MakePerson(otherFamily);
        var y = MakePerson(otherFamily);
        await using (var ctx = factory.CreateDbContext())
        {
            ctx.People.AddRange(x, y);
            await ctx.SaveChangesAsync();
        }
        var created = await superUserService.CreateAsync(new RelationshipUpsertDto
        {
            PersonAId = x.Id, PersonBId = y.Id, Type = RelationshipType.Sibling,
        });

        var regularUserService = new RelationshipService(
            factory, NullLogger<RelationshipService>.Instance, new FakeAuditLogService(),
            new FakeCurrentUserService { FamilyId = Guid.NewGuid() });

        var result = await regularUserService.DeleteAsync(created.Data!.Id);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("not found",
            "a relationship in a different family should look identical to one that truly doesn't exist — never leak cross-family existence");
    }

    [Fact]
    public async Task UpdateAsync_BothExistingAndNewPeopleInCallersFamily_Succeeds()
    {
        var familyId = Guid.NewGuid();
        var (service, factory, _) = CreateSut(familyId: familyId);
        var a = MakePerson(familyId);
        var b = MakePerson(familyId);
        var c = MakePerson(familyId);
        await using (var ctx = factory.CreateDbContext())
        {
            ctx.People.AddRange(a, b, c);
            await ctx.SaveChangesAsync();
        }
        var created = await service.CreateAsync(new RelationshipUpsertDto
        {
            PersonAId = a.Id, PersonBId = b.Id, Type = RelationshipType.Sibling,
        });

        // Retarget from (a, b) to (a, c) — still entirely within the caller's own family.
        var result = await service.UpdateAsync(created.Data!.Id, new RelationshipUpsertDto
        {
            PersonAId = a.Id, PersonBId = c.Id, Type = RelationshipType.Sibling,
        });

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_ExistingRelationshipInDifferentFamily_ReturnsGenericNotFound()
    {
        var otherFamily = Guid.NewGuid();
        var (superUserService, factory, _) = CreateSut(isSuperUser: true);
        var x = MakePerson(otherFamily);
        var y = MakePerson(otherFamily);
        await using (var ctx = factory.CreateDbContext())
        {
            ctx.People.AddRange(x, y);
            await ctx.SaveChangesAsync();
        }
        var created = await superUserService.CreateAsync(new RelationshipUpsertDto
        {
            PersonAId = x.Id, PersonBId = y.Id, Type = RelationshipType.Sibling,
        });

        var regularUserService = new RelationshipService(
            factory, NullLogger<RelationshipService>.Instance, new FakeAuditLogService(),
            new FakeCurrentUserService { FamilyId = Guid.NewGuid() });

        var result = await regularUserService.UpdateAsync(created.Data!.Id, new RelationshipUpsertDto
        {
            PersonAId = x.Id, PersonBId = y.Id, Type = RelationshipType.Sibling, EndDate = new DateOnly(2020, 1, 1),
        });

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("not found",
            "a relationship entirely in a different family should look identical to one that doesn't exist");
    }

    [Fact]
    public async Task UpdateAsync_AttemptToRetargetOwnRelationshipOntoDifferentFamily_Fails()
    {
        var myFamily = Guid.NewGuid();
        var otherFamily = Guid.NewGuid();
        var (service, factory, _) = CreateSut(familyId: myFamily);
        var a = MakePerson(myFamily);
        var b = MakePerson(myFamily);
        var stranger = MakePerson(otherFamily);
        await using (var ctx = factory.CreateDbContext())
        {
            ctx.People.AddRange(a, b, stranger);
            await ctx.SaveChangesAsync();
        }
        var created = await service.CreateAsync(new RelationshipUpsertDto
        {
            PersonAId = a.Id, PersonBId = b.Id, Type = RelationshipType.Sibling,
        });

        // A relationship the caller legitimately owns (a, b) must not be retargetable
        // onto someone from another family just by knowing their GUID.
        var result = await service.UpdateAsync(created.Data!.Id, new RelationshipUpsertDto
        {
            PersonAId = a.Id, PersonBId = stranger.Id, Type = RelationshipType.Sibling,
        });

        result.Success.Should().BeFalse(
            "retargeting a relationship you own onto a person from a different family must be blocked");
    }

    [Fact]
    public async Task UpdateAsync_SuperUser_CanRetargetAcrossFamilies()
    {
        var familyOne = Guid.NewGuid();
        var familyTwo = Guid.NewGuid();
        var (service, factory, _) = CreateSut(isSuperUser: true);
        var a = MakePerson(familyOne);
        var b = MakePerson(familyOne);
        var c = MakePerson(familyTwo);
        await using (var ctx = factory.CreateDbContext())
        {
            ctx.People.AddRange(a, b, c);
            await ctx.SaveChangesAsync();
        }
        var created = await service.CreateAsync(new RelationshipUpsertDto
        {
            PersonAId = a.Id, PersonBId = b.Id, Type = RelationshipType.Sibling,
        });

        var result = await service.UpdateAsync(created.Data!.Id, new RelationshipUpsertDto
        {
            PersonAId = a.Id, PersonBId = c.Id, Type = RelationshipType.Sibling,
        });

        result.Success.Should().BeTrue("a super-user bypasses family scoping entirely");
    }
}
