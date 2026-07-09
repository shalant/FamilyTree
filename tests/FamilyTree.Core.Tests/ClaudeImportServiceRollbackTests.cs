using FamilyTree.Core.Models;
using FamilyTree.Core.Services;
using FamilyTree.Core.Tests.Helpers;
using FamilyTree.Shared.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FamilyTree.Core.Tests;

// Regression coverage for the Elliot Rosenberg duplicate-person bug (docs/TodoList.md,
// found 2026-07-08): RollbackBatchAsync soft-deleted every Person in the batch, but only
// soft-deleted a Relationship when BOTH endpoints were in the batch — a relationship
// linking a batch-created person to a pre-existing one survived as an orphan pointing at
// a soft-deleted Person, and a later delete/restore test exposed it as a "ghost" duplicate.
public class ClaudeImportServiceRollbackTests
{
    private static ClaudeImportService CreateService(TestDbContextFactory factory) =>
        new(
            httpFactory: new ThrowingHttpClientFactory(),
            config: new ConfigurationBuilder().Build(),
            dbFactory: factory,
            logger: NullLogger<ClaudeImportService>.Instance);

    [Fact]
    public async Task RollbackBatchAsync_RelationshipToPreExistingPerson_IsCascadeDeletedToo()
    {
        var factory = new TestDbContextFactory();
        var batchId = Guid.NewGuid();
        var preExistingId = Guid.NewGuid();
        var importedId = Guid.NewGuid();
        Guid relationshipId;

        await using (var ctx = factory.CreateDbContext())
        {
            ctx.ImportBatches.Add(new ImportBatch { Id = batchId, CreatedAt = DateTime.UtcNow });
            ctx.People.Add(new Person
            {
                Id = preExistingId, FirstName = "Marc", LastName = "Rosenberg",
                ImportBatchId = null, CreatedAt = DateTime.UtcNow,
            });
            ctx.People.Add(new Person
            {
                Id = importedId, FirstName = "Elliot", LastName = "Rosenberg",
                ImportBatchId = batchId, CreatedAt = DateTime.UtcNow,
            });
            relationshipId = Guid.NewGuid();
            ctx.Relationships.Add(new Relationship
            {
                Id = relationshipId,
                PersonAId = preExistingId,
                PersonBId = importedId,
                Type = RelationshipType.Sibling,
                CreatedAt = DateTime.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        var service = CreateService(factory);
        await service.RollbackBatchAsync(batchId);

        await using var verify = factory.CreateDbContext();
        var imported = await verify.People.IgnoreQueryFilters().SingleAsync(p => p.Id == importedId);
        var preExisting = await verify.People.IgnoreQueryFilters().SingleAsync(p => p.Id == preExistingId);
        var relationship = await verify.Relationships.IgnoreQueryFilters().SingleAsync(r => r.Id == relationshipId);

        imported.DeletedAt.Should().NotBeNull("the batch-created person must be rolled back");
        preExisting.DeletedAt.Should().BeNull("only the batch's own person should be touched, not who they're related to");
        relationship.DeletedAt.Should().NotBeNull(
            "a relationship pointing at a now-deleted person must not survive as a live ghost row");
    }

    [Fact]
    public async Task RollbackBatchAsync_RelationshipEntirelyInsideBatch_IsStillCascadeDeleted()
    {
        var factory = new TestDbContextFactory();
        var batchId = Guid.NewGuid();
        var personAId = Guid.NewGuid();
        var personBId = Guid.NewGuid();
        Guid relationshipId;

        await using (var ctx = factory.CreateDbContext())
        {
            ctx.ImportBatches.Add(new ImportBatch { Id = batchId, CreatedAt = DateTime.UtcNow });
            ctx.People.Add(new Person { Id = personAId, FirstName = "A", LastName = "Test", ImportBatchId = batchId, CreatedAt = DateTime.UtcNow });
            ctx.People.Add(new Person { Id = personBId, FirstName = "B", LastName = "Test", ImportBatchId = batchId, CreatedAt = DateTime.UtcNow });
            relationshipId = Guid.NewGuid();
            ctx.Relationships.Add(new Relationship
            {
                Id = relationshipId, PersonAId = personAId, PersonBId = personBId,
                Type = RelationshipType.Sibling, CreatedAt = DateTime.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        var service = CreateService(factory);
        await service.RollbackBatchAsync(batchId);

        await using var verify = factory.CreateDbContext();
        var relationship = await verify.Relationships.IgnoreQueryFilters().SingleAsync(r => r.Id == relationshipId);
        relationship.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RollbackBatchAsync_UnrelatedRelationship_IsUntouched()
    {
        var factory = new TestDbContextFactory();
        var batchId = Guid.NewGuid();
        var importedId = Guid.NewGuid();
        var unrelatedAId = Guid.NewGuid();
        var unrelatedBId = Guid.NewGuid();
        Guid unrelatedRelationshipId;

        await using (var ctx = factory.CreateDbContext())
        {
            ctx.ImportBatches.Add(new ImportBatch { Id = batchId, CreatedAt = DateTime.UtcNow });
            ctx.People.Add(new Person { Id = importedId, FirstName = "Elliot", LastName = "Rosenberg", ImportBatchId = batchId, CreatedAt = DateTime.UtcNow });
            ctx.People.Add(new Person { Id = unrelatedAId, FirstName = "Bud", LastName = "Rosenberg", CreatedAt = DateTime.UtcNow });
            ctx.People.Add(new Person { Id = unrelatedBId, FirstName = "Florence", LastName = "Rosenberg", CreatedAt = DateTime.UtcNow });
            unrelatedRelationshipId = Guid.NewGuid();
            ctx.Relationships.Add(new Relationship
            {
                Id = unrelatedRelationshipId, PersonAId = unrelatedAId, PersonBId = unrelatedBId,
                Type = RelationshipType.Spouse, CreatedAt = DateTime.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        var service = CreateService(factory);
        await service.RollbackBatchAsync(batchId);

        await using var verify = factory.CreateDbContext();
        var unrelatedRelationship = await verify.Relationships.IgnoreQueryFilters().SingleAsync(r => r.Id == unrelatedRelationshipId);
        unrelatedRelationship.DeletedAt.Should().BeNull("a relationship touching no one from this batch must not be affected");
    }
}

internal sealed class ThrowingHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => throw new NotImplementedException();
}
