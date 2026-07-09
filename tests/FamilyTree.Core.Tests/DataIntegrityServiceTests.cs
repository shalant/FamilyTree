using FamilyTree.Core.Models;
using FamilyTree.Core.Services;
using FamilyTree.Core.Tests.Helpers;
using FamilyTree.Shared.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FamilyTree.Core.Tests;

// Regression coverage for the "orphaned relationship" integrity check — see the Elliot
// Rosenberg incident (docs/TodoList.md) that motivated it: a live Relationship left
// pointing at a soft-deleted Person after some code path bypassed
// PersonService.DeleteAsync's normal cascade.
public class DataIntegrityServiceTests
{
    private static DataIntegrityService CreateService(TestDbContextFactory factory, Guid? actingUserId = null) =>
        new(factory, new FakeAuditLogService(), new FakeCurrentUserService { UserId = actingUserId },
            NullLogger<DataIntegrityService>.Instance);

    [Fact]
    public async Task FixOrphanedRelationshipsAsync_LiveRelationshipToSoftDeletedPerson_IsSoftDeletedAndReported()
    {
        var factory = new TestDbContextFactory();
        var livePersonId = Guid.NewGuid();
        var deletedPersonId = Guid.NewGuid();
        Guid relationshipId;

        await using (var ctx = factory.CreateDbContext())
        {
            ctx.People.Add(new Person { Id = livePersonId, FirstName = "Marc", LastName = "Rosenberg", CreatedAt = DateTime.UtcNow });
            ctx.People.Add(new Person
            {
                Id = deletedPersonId, FirstName = "Elliot", LastName = "Rosenberg", CreatedAt = DateTime.UtcNow,
                DeletedAt = DateTime.UtcNow.AddDays(-1),
            });
            relationshipId = Guid.NewGuid();
            ctx.Relationships.Add(new Relationship
            {
                Id = relationshipId, PersonAId = livePersonId, PersonBId = deletedPersonId,
                Type = RelationshipType.Sibling, CreatedAt = DateTime.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        var actingUserId = Guid.NewGuid();
        var service = CreateService(factory, actingUserId);
        var result = await service.FixOrphanedRelationshipsAsync();

        result.Success.Should().BeTrue();
        result.Data.Should().ContainSingle();
        result.Data![0].RelationshipId.Should().Be(relationshipId);
        result.Data[0].PersonAName.Should().Be("Marc Rosenberg");
        result.Data[0].PersonBName.Should().Be("Elliot Rosenberg");

        await using var verify = factory.CreateDbContext();
        var relationship = await verify.Relationships.IgnoreQueryFilters()
            .SingleAsync(r => r.Id == relationshipId);
        relationship.DeletedAt.Should().NotBeNull();
        relationship.DeletedBy.Should().Be(actingUserId);
    }

    [Fact]
    public async Task FixOrphanedRelationshipsAsync_RunWithNoAuthenticatedUser_StillFixesWithNullDeletedBy()
    {
        // Simulates the startup-triggered run, where there's no HTTP context / signed-in user.
        var factory = new TestDbContextFactory();
        var livePersonId = Guid.NewGuid();
        var deletedPersonId = Guid.NewGuid();

        await using (var ctx = factory.CreateDbContext())
        {
            ctx.People.Add(new Person { Id = livePersonId, FirstName = "A", LastName = "Test", CreatedAt = DateTime.UtcNow });
            ctx.People.Add(new Person
            {
                Id = deletedPersonId, FirstName = "B", LastName = "Test", CreatedAt = DateTime.UtcNow,
                DeletedAt = DateTime.UtcNow,
            });
            ctx.Relationships.Add(new Relationship
            {
                Id = Guid.NewGuid(), PersonAId = livePersonId, PersonBId = deletedPersonId,
                Type = RelationshipType.Parent, CreatedAt = DateTime.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        var service = CreateService(factory, actingUserId: null);
        var result = await service.FixOrphanedRelationshipsAsync();

        result.Success.Should().BeTrue();
        result.Data.Should().ContainSingle();

        await using var verify = factory.CreateDbContext();
        var relationship = await verify.Relationships.IgnoreQueryFilters().SingleAsync();
        relationship.DeletedAt.Should().NotBeNull();
        relationship.DeletedBy.Should().BeNull();
    }

    [Fact]
    public async Task FixOrphanedRelationshipsAsync_RelationshipBetweenTwoLivePeople_IsUntouched()
    {
        var factory = new TestDbContextFactory();
        var personAId = Guid.NewGuid();
        var personBId = Guid.NewGuid();
        Guid relationshipId;

        await using (var ctx = factory.CreateDbContext())
        {
            ctx.People.Add(new Person { Id = personAId, FirstName = "Bud", LastName = "Rosenberg", CreatedAt = DateTime.UtcNow });
            ctx.People.Add(new Person { Id = personBId, FirstName = "Florence", LastName = "Rosenberg", CreatedAt = DateTime.UtcNow });
            relationshipId = Guid.NewGuid();
            ctx.Relationships.Add(new Relationship
            {
                Id = relationshipId, PersonAId = personAId, PersonBId = personBId,
                Type = RelationshipType.Spouse, CreatedAt = DateTime.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        var service = CreateService(factory);
        var result = await service.FixOrphanedRelationshipsAsync();

        result.Success.Should().BeTrue();
        result.Data.Should().BeEmpty();

        await using var verify = factory.CreateDbContext();
        var relationship = await verify.Relationships.SingleAsync(r => r.Id == relationshipId);
        relationship.DeletedAt.Should().BeNull("a relationship between two live people is not orphaned");
    }

    [Fact]
    public async Task FixOrphanedRelationshipsAsync_RelationshipAlreadySoftDeleted_IsNotDoubleCounted()
    {
        // A relationship correctly cascade-soft-deleted by PersonService.DeleteAsync (both
        // sides tagged with the same DeletedAt) must not be re-reported as a "fix" — it's
        // already in the correct state, not drift.
        var factory = new TestDbContextFactory();
        var personAId = Guid.NewGuid();
        var personBId = Guid.NewGuid();
        var deletedAt = DateTime.UtcNow.AddHours(-2);

        await using (var ctx = factory.CreateDbContext())
        {
            ctx.People.Add(new Person
            {
                Id = personAId, FirstName = "A", LastName = "Test", CreatedAt = DateTime.UtcNow,
                DeletedAt = deletedAt,
            });
            ctx.People.Add(new Person { Id = personBId, FirstName = "B", LastName = "Test", CreatedAt = DateTime.UtcNow });
            ctx.Relationships.Add(new Relationship
            {
                Id = Guid.NewGuid(), PersonAId = personAId, PersonBId = personBId,
                Type = RelationshipType.Sibling, CreatedAt = DateTime.UtcNow,
                DeletedAt = deletedAt,
            });
            await ctx.SaveChangesAsync();
        }

        var service = CreateService(factory);
        var result = await service.FixOrphanedRelationshipsAsync();

        result.Success.Should().BeTrue();
        result.Data.Should().BeEmpty("the relationship is already soft-deleted, matching its soft-deleted person");
    }
}
