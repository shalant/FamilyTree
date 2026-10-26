using FamilyTree.Core.Data;
using FamilyTree.Core.Models;
using FamilyTree.Core.Services;
using FamilyTree.Shared.DTOs.Person;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FamilyTree.E2E.Tests;

/// <summary>
/// PersonService.CreateAsync saves the person, then their relationships (the Id comes from
/// the database, so it can't be one save). Without a transaction, a failure in the second
/// save left a person with none of the links they were created with. The EF InMemory
/// provider ignores transactions, so this has to run against real SQL Server, which is
/// why it lives in the E2E project (no browser involved).
/// </summary>
[Collection("E2E")]
public class PersonCreateTransactionTests : E2eTestBase
{
    public PersonCreateTransactionTests(E2eAppFixture fixture) : base(fixture) { }

    [Fact]
    [Trait("Category", "Regression")]
    public async Task CreateAsync_WhenTheRelationshipSaveFails_LeavesNoPersonBehind()
    {
        var parentId = await SeedParentAsync();
        var factory = new Factory(Fixture.ConnectionString, new FailOnSecondSave());
        var name = $"Orphan{Guid.NewGuid():N}";

        var result = await Service(factory).CreateAsync(
            new PersonUpsertDto { FirstName = name, LastName = "Rollback", ParentIds = [parentId] });

        result.Success.Should().BeFalse();
        await using var ctx = new Factory(Fixture.ConnectionString).CreateDbContext();
        (await ctx.People.IgnoreQueryFilters().AnyAsync(p => p.FirstName == name)).Should().BeFalse(
            "the person must roll back with the failed relationship save");
    }

    [Fact]
    public async Task CreateAsync_WhenNothingFails_SavesThePersonAndTheirLinks()
    {
        var parentId = await SeedParentAsync();
        var name = $"Child{Guid.NewGuid():N}";

        var result = await Service(new Factory(Fixture.ConnectionString)).CreateAsync(
            new PersonUpsertDto { FirstName = name, LastName = "Committed", ParentIds = [parentId] });

        result.Success.Should().BeTrue(result.Message);
        await using var ctx = new Factory(Fixture.ConnectionString).CreateDbContext();
        var child = await ctx.People.SingleAsync(p => p.FirstName == name);
        (await ctx.Relationships.AnyAsync(r => r.PersonAId == parentId && r.PersonBId == child.Id)).Should().BeTrue();
    }

    private async Task<Guid> SeedParentAsync()
    {
        var familyId = await Fixture.Seeder.CreateFamilyAsync("Transaction Family");
        await using var ctx = new Factory(Fixture.ConnectionString).CreateDbContext();
        var parent = new Person
        {
            FirstName = "Parent", LastName = "Seeded", FamilyId = familyId,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        ctx.People.Add(parent);
        await ctx.SaveChangesAsync();
        return parent.Id;
    }

    private static PersonService Service(IDbContextFactory<AppDbContext> factory) =>
        new(factory, NullLogger<PersonService>.Instance, new NoAuditLog(), new SuperUser());

    private sealed class Factory(string connectionString, params IInterceptor[] interceptors) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .AddInterceptors(interceptors)
            .Options);
    }

    // Throws on the second SaveChanges of the factory's contexts: the first saves the
    // person, the second would save their relationships.
    private sealed class FailOnSecondSave : SaveChangesInterceptor
    {
        private int _saves;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _saves) == 2)
                throw new InvalidOperationException("Simulated failure saving relationships.");
            return base.SavingChangesAsync(eventData, result, ct);
        }
    }

    private sealed class NoAuditLog : IAuditLogService
    {
        public Task LogAsync(string action, string entityType, Guid? entityId,
            string? oldValue = null, string? newValue = null,
            Guid? userId = null, string? ipAddress = null, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class SuperUser : ICurrentUserService
    {
        public Guid? UserId => null;
        public Guid? FamilyId => null;
        public string? Email => null;
        public bool IsSuperUser => true;
        public bool IsAdmin => false;
    }
}
