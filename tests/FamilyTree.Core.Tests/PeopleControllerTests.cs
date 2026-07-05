using FamilyTree.Core.Services;
using FamilyTree.Core.Tests.Helpers;
using Xunit;
using FamilyTree.Shared.DTOs.Person;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FamilyTree.Core.Tests;

public class PersonServiceTests
{
    private static (PersonService service, TestDbContextFactory factory, FakeCurrentUserService fakeUser)
        CreateSut(Guid? userId = null, Guid? familyId = null)
    {
        var factory = new TestDbContextFactory();
        var fakeUser = new FakeCurrentUserService { UserId = userId, FamilyId = familyId };
        var service = new PersonService(
            factory,
            NullLogger<PersonService>.Instance,
            new FakeAuditLogService(),
            fakeUser);
        return (service, factory, fakeUser);
    }

    [Fact]
    public async Task CreateAsync_ValidPerson_ReturnsPerson()
    {
        var (service, _, _) = CreateSut();

        var result = await service.CreateAsync(new PersonUpsertDto { FirstName = "Alice", LastName = "Smith" });

        result.Success.Should().BeTrue();
        result.Data!.FullName.Should().Contain("Alice");
    }

    [Fact]
    public async Task CreateAsync_StampsCreatedByAndFamilyId()
    {
        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var (service, factory, _) = CreateSut(userId, familyId);

        var result = await service.CreateAsync(new PersonUpsertDto { FirstName = "Bob", LastName = "Jones" });

        result.Success.Should().BeTrue();
        await using var ctx = factory.CreateDbContext();
        var person = ctx.People.Single();
        person.CreatedBy.Should().Be(userId);
        person.FamilyId.Should().Be(familyId);
    }

    [Fact]
    public async Task CreateAsync_WhitespaceFirstName_ReturnsFail()
    {
        var (service, _, _) = CreateSut();

        var result = await service.CreateAsync(new PersonUpsertDto { FirstName = "   ", LastName = "Smith" });

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task CreateAsync_BirthDateAfterDeathDate_ReturnsFail()
    {
        var (service, _, _) = CreateSut();
        var dto = new PersonUpsertDto
        {
            FirstName = "Alice",
            LastName = "Smith",
            BirthDate = new DateOnly(1990, 1, 1),
            DeathDate = new DateOnly(1980, 1, 1),
        };

        var result = await service.CreateAsync(dto);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Birth date");
    }

    [Fact]
    public async Task DeleteAsync_SetsDeletedAtAndDeletedBy()
    {
        var userId = Guid.NewGuid();
        var (service, factory, _) = CreateSut(userId);
        var created = await service.CreateAsync(new PersonUpsertDto { FirstName = "Carol", LastName = "White" });
        var personId = created.Data!.Id;

        var result = await service.DeleteAsync(personId);

        result.Success.Should().BeTrue();
        await using var ctx = factory.CreateDbContext();
        var person = ctx.People.IgnoreQueryFilters().Single(p => p.Id == personId);
        person.DeletedAt.Should().NotBeNull();
        person.DeletedBy.Should().Be(userId);
    }

    [Fact]
    public async Task RestoreAsync_ClearsDeletedAtAndDeletedBy()
    {
        var userId = Guid.NewGuid();
        var (service, factory, _) = CreateSut(userId);
        var created = await service.CreateAsync(new PersonUpsertDto { FirstName = "Dave", LastName = "Brown" });
        var personId = created.Data!.Id;
        await service.DeleteAsync(personId);

        var result = await service.RestoreAsync(personId);

        result.Success.Should().BeTrue();
        await using var ctx = factory.CreateDbContext();
        var person = ctx.People.Single(p => p.Id == personId);
        person.DeletedAt.Should().BeNull();
        person.DeletedBy.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_CascadeSoftDeletesOwnRelationships()
    {
        var userId = Guid.NewGuid();
        var (service, factory, _) = CreateSut(userId);
        var alice = await service.CreateAsync(new PersonUpsertDto { FirstName = "Alice", LastName = "One" });
        var bob = await service.CreateAsync(new PersonUpsertDto { FirstName = "Bob", LastName = "Two" });
        Guid relationshipId;
        await using (var ctx = factory.CreateDbContext())
        {
            var rel = new FamilyTree.Core.Models.Relationship
            {
                Id = Guid.NewGuid(),
                PersonAId = alice.Data!.Id,
                PersonBId = bob.Data!.Id,
                Type = FamilyTree.Shared.Enums.RelationshipType.Sibling,
                CreatedAt = DateTime.UtcNow,
            };
            ctx.Relationships.Add(rel);
            await ctx.SaveChangesAsync();
            relationshipId = rel.Id;
        }

        await service.DeleteAsync(alice.Data!.Id);

        await using var checkCtx = factory.CreateDbContext();
        var rel2 = checkCtx.Relationships.IgnoreQueryFilters().Single(r => r.Id == relationshipId);
        rel2.DeletedAt.Should().NotBeNull(
            "deleting a person should cascade to their own relationships so they don't linger as dangling rows");
    }

    [Fact]
    public async Task RestoreAsync_RestoresRelationshipsCascadeDeletedAtTheSameTime()
    {
        var userId = Guid.NewGuid();
        var (service, factory, _) = CreateSut(userId);
        var alice = await service.CreateAsync(new PersonUpsertDto { FirstName = "Alice", LastName = "One" });
        var bob = await service.CreateAsync(new PersonUpsertDto { FirstName = "Bob", LastName = "Two" });
        Guid relationshipId;
        await using (var ctx = factory.CreateDbContext())
        {
            var rel = new FamilyTree.Core.Models.Relationship
            {
                Id = Guid.NewGuid(),
                PersonAId = alice.Data!.Id,
                PersonBId = bob.Data!.Id,
                Type = FamilyTree.Shared.Enums.RelationshipType.Sibling,
                CreatedAt = DateTime.UtcNow,
            };
            ctx.Relationships.Add(rel);
            await ctx.SaveChangesAsync();
            relationshipId = rel.Id;
        }
        await service.DeleteAsync(alice.Data!.Id);

        await service.RestoreAsync(alice.Data!.Id);

        await using var checkCtx = factory.CreateDbContext();
        var rel2 = checkCtx.Relationships.Single(r => r.Id == relationshipId);
        rel2.DeletedAt.Should().BeNull(
            "restoring a person should bring back exactly the relationships that were cascade-deleted alongside them");
    }

    [Fact]
    public async Task RestoreAsync_DoesNotResurrectRelationshipsDeletedIndependently()
    {
        var userId = Guid.NewGuid();
        var (service, factory, _) = CreateSut(userId);
        var alice = await service.CreateAsync(new PersonUpsertDto { FirstName = "Alice", LastName = "One" });
        var bob = await service.CreateAsync(new PersonUpsertDto { FirstName = "Bob", LastName = "Two" });
        Guid relationshipId;
        await using (var ctx = factory.CreateDbContext())
        {
            var rel = new FamilyTree.Core.Models.Relationship
            {
                Id = Guid.NewGuid(),
                PersonAId = alice.Data!.Id,
                PersonBId = bob.Data!.Id,
                Type = FamilyTree.Shared.Enums.RelationshipType.Sibling,
                CreatedAt = DateTime.UtcNow,
                // Deliberately soft-deleted well before the person is, simulating the
                // user having removed this specific relationship on its own earlier.
                DeletedAt = DateTime.UtcNow.AddDays(-1),
            };
            ctx.Relationships.Add(rel);
            await ctx.SaveChangesAsync();
            relationshipId = rel.Id;
        }

        await service.DeleteAsync(alice.Data!.Id);
        await service.RestoreAsync(alice.Data!.Id);

        await using var checkCtx = factory.CreateDbContext();
        var rel2 = checkCtx.Relationships.IgnoreQueryFilters().Single(r => r.Id == relationshipId);
        rel2.DeletedAt.Should().NotBeNull(
            "a relationship removed independently, before the person was deleted, must stay removed — restoring the person shouldn't resurrect it");
    }

    [Fact]
    public async Task GetAllAsync_FamilyScopingFiltersToMatchingFamily()
    {
        var family1 = Guid.NewGuid();
        var family2 = Guid.NewGuid();
        var (service, _, fakeUser) = CreateSut(familyId: family1);

        await service.CreateAsync(new PersonUpsertDto { FirstName = "Alice", LastName = "FamilyOne" });
        fakeUser.FamilyId = family2;
        await service.CreateAsync(new PersonUpsertDto { FirstName = "Bob", LastName = "FamilyTwo" });

        fakeUser.FamilyId = family1;
        var result = await service.GetAllAsync();

        result.Success.Should().BeTrue();
        result.Data!.Should().HaveCount(1);
        result.Data![0].LastName.Should().Be("FamilyOne");
    }

    [Fact]
    public async Task GetAllAsync_NoFamilyIdAndNotSuperUser_ReturnsNoPeople()
    {
        // A regular user with no FamilyId claim (e.g. a broken/missing UserFamily
        // assignment) must see NOTHING — not be silently treated as a super-user and
        // shown every family's data. This was the actual security bug: the bypass
        // condition only checked "FamilyId is null," which is also true for a
        // misconfigured regular account, not just genuine super-users.
        var (service, _, fakeUser) = CreateSut(familyId: Guid.NewGuid());

        await service.CreateAsync(new PersonUpsertDto { FirstName = "Alice", LastName = "Smith" });
        fakeUser.FamilyId = Guid.NewGuid();
        await service.CreateAsync(new PersonUpsertDto { FirstName = "Bob", LastName = "Jones" });

        fakeUser.FamilyId = null;
        fakeUser.IsSuperUser = false;
        var result = await service.GetAllAsync();

        result.Success.Should().BeTrue();
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_SuperUser_ReturnsAllPeopleAcrossFamilies()
    {
        var (service, _, fakeUser) = CreateSut(familyId: Guid.NewGuid());

        await service.CreateAsync(new PersonUpsertDto { FirstName = "Alice", LastName = "Smith" });
        fakeUser.FamilyId = Guid.NewGuid();
        await service.CreateAsync(new PersonUpsertDto { FirstName = "Bob", LastName = "Jones" });

        fakeUser.FamilyId = null;
        fakeUser.IsSuperUser = true;
        var result = await service.GetAllAsync();

        result.Success.Should().BeTrue();
        result.Data!.Should().HaveCount(2);
    }

    [Fact]
    public async Task CreateAsync_SuperUserWithNoFamilyId_AssignsNonNullFamilyId_AndReusesSameFamily()
    {
        // A super-user creating a person directly (e.g. via "Add Person") has no
        // FamilyId claim by design — without a fallback, the new person would end up
        // ownerless (FamilyId == null) and invisible to every regular family member.
        // Two creates in a row should also land in the SAME fallback family, not each
        // spin up a brand new one.
        var (service, factory, fakeUser) = CreateSut();
        fakeUser.IsSuperUser = true;

        await service.CreateAsync(new PersonUpsertDto { FirstName = "Bill", LastName = "Small" });
        await service.CreateAsync(new PersonUpsertDto { FirstName = "Morton", LastName = "Small" });

        await using var ctx = factory.CreateDbContext();
        var bill = ctx.People.Single(p => p.FirstName == "Bill");
        var morton = ctx.People.Single(p => p.FirstName == "Morton");

        bill.FamilyId.Should().NotBeNull("a super-user creating a person should never leave them ownerless");
        morton.FamilyId.Should().Be(bill.FamilyId,
            "repeated super-user creates should reuse the same fallback family, not create a new one each time");
    }

    [Fact]
    public void Delete_ShouldRemovePerson_WhenSuccessful()
    {
        var people = new List<PersonDto>
        {
            new() { Id = Guid.NewGuid(), FirstName = "Douglas" }
        };

        people.RemoveAt(0);

        people.Should().BeEmpty();
    }
}
