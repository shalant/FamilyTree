using FamilyTree.Core.Models;
using FamilyTree.Core.Services;
using FamilyTree.Core.Tests.Helpers;
using FamilyTree.Shared.DTOs.Import;
using FamilyTree.Shared.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FamilyTree.Core.Tests;

// R0 (docs/TodoList.md): the read-only check for Parent loops already in the data, the
// graph helper behind it and behind the write-path checks, and the import guard.
public class ParentCycleIntegrityTests
{
    private static DataIntegrityService CreateService(
        TestDbContextFactory factory, Guid? userId = null, bool superUser = false) =>
        new(factory, new FakeAuditLogService(),
            new FakeCurrentUserService { UserId = userId, IsSuperUser = superUser },
            NullLogger<DataIntegrityService>.Instance);

    private static async Task<Guid> AddPersonAsync(TestDbContextFactory factory, string name, bool deleted = false)
    {
        await using var ctx = factory.CreateDbContext();
        var p = new Person
        {
            Id = Guid.NewGuid(), FirstName = name, LastName = "Test", CreatedAt = DateTime.UtcNow,
            DeletedAt = deleted ? DateTime.UtcNow : null,
        };
        ctx.People.Add(p);
        await ctx.SaveChangesAsync();
        return p.Id;
    }

    private static async Task<Guid> AddParentAsync(TestDbContextFactory factory, Guid parent, Guid child)
    {
        await using var ctx = factory.CreateDbContext();
        var r = new Relationship
        {
            Id = Guid.NewGuid(), PersonAId = parent, PersonBId = child,
            Type = RelationshipType.Parent, CreatedAt = DateTime.UtcNow,
        };
        ctx.Relationships.Add(r);
        await ctx.SaveChangesAsync();
        return r.Id;
    }

    // ── DataIntegrityService.FindParentCyclesAsync ───────────────────────────

    [Fact]
    public async Task FindParentCycles_ReportsTheLoopAndOnlyTheLoop()
    {
        var factory = new TestDbContextFactory();
        var grandpa = await AddPersonAsync(factory, "Grandpa");
        var dad = await AddPersonAsync(factory, "Dad");
        var kid = await AddPersonAsync(factory, "Kid");
        var bystander = await AddPersonAsync(factory, "Bystander");
        var child = await AddPersonAsync(factory, "Bystander's child");
        await AddParentAsync(factory, grandpa, dad);
        await AddParentAsync(factory, dad, kid);
        var closing = await AddParentAsync(factory, kid, grandpa); // the bad link
        await AddParentAsync(factory, bystander, child);

        var result = await CreateService(factory).FindParentCyclesAsync();

        result.Success.Should().BeTrue(result.Message);
        var loop = result.Data.Should().ContainSingle().Subject;
        loop.People.Select(p => p.PersonId).Should().BeEquivalentTo([grandpa, dad, kid]);
        loop.People.Select(p => p.Name).Should().BeEquivalentTo(["Grandpa Test", "Dad Test", "Kid Test"]);
        loop.Links.Should().HaveCount(3);
        loop.Links.Select(l => l.RelationshipId).Should().Contain(closing);
    }

    [Fact]
    public async Task FindParentCycles_ReportsAPersonRecordedAsTheirOwnParent()
    {
        var factory = new TestDbContextFactory();
        var narcissus = await AddPersonAsync(factory, "Narcissus");
        await AddParentAsync(factory, narcissus, narcissus);

        var result = await CreateService(factory).FindParentCyclesAsync();

        result.Data.Should().ContainSingle().Which.People.Should().ContainSingle()
            .Which.PersonId.Should().Be(narcissus);
    }

    [Fact]
    public async Task FindParentCycles_ReportsSeparateLoopsSeparately()
    {
        var factory = new TestDbContextFactory();
        var a = await AddPersonAsync(factory, "A");
        var b = await AddPersonAsync(factory, "B");
        var c = await AddPersonAsync(factory, "C");
        var d = await AddPersonAsync(factory, "D");
        await AddParentAsync(factory, a, b);
        await AddParentAsync(factory, b, a);
        await AddParentAsync(factory, c, d);
        await AddParentAsync(factory, d, c);

        var result = await CreateService(factory).FindParentCyclesAsync();

        result.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task FindParentCycles_HealthyTreeWithADiamond_ReportsNothing()
    {
        var factory = new TestDbContextFactory();
        var g1 = await AddPersonAsync(factory, "G1");
        var g2 = await AddPersonAsync(factory, "G2");
        var p = await AddPersonAsync(factory, "P");
        var k = await AddPersonAsync(factory, "K");
        await AddParentAsync(factory, g1, p);
        await AddParentAsync(factory, g2, p);
        await AddParentAsync(factory, p, k);
        await AddParentAsync(factory, g1, k); // a person reachable by two routes is not a loop

        var result = await CreateService(factory).FindParentCyclesAsync();

        result.Success.Should().BeTrue();
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task FindParentCycles_IgnoresLoopsThatNeedASoftDeletedPerson()
    {
        var factory = new TestDbContextFactory();
        var a = await AddPersonAsync(factory, "A");
        var gone = await AddPersonAsync(factory, "Gone", deleted: true);
        var c = await AddPersonAsync(factory, "C");
        await AddParentAsync(factory, a, gone);
        await AddParentAsync(factory, gone, c);
        await AddParentAsync(factory, c, a);

        var result = await CreateService(factory).FindParentCyclesAsync();

        result.Data.Should().BeEmpty("the tree is drawn from live people only, and without Gone there is no loop");
    }

    [Fact]
    public async Task FindParentCycles_ChangesNothing()
    {
        var factory = new TestDbContextFactory();
        var a = await AddPersonAsync(factory, "A");
        var b = await AddPersonAsync(factory, "B");
        await AddParentAsync(factory, a, b);
        await AddParentAsync(factory, b, a);

        async Task<string> SnapshotAsync()
        {
            await using var ctx = factory.CreateDbContext();
            var rels = await ctx.Relationships.IgnoreQueryFilters().OrderBy(r => r.Id).ToListAsync();
            var people = await ctx.People.IgnoreQueryFilters().OrderBy(p => p.Id).ToListAsync();
            return string.Join("|", rels.Select(r => $"{r.Id}:{r.PersonAId}:{r.PersonBId}:{r.Type}:{r.DeletedAt}:{r.UpdatedAt}")) +
                   "#" + string.Join("|", people.Select(p => $"{p.Id}:{p.DeletedAt}:{p.UpdatedAt}"));
        }
        var before = await SnapshotAsync();

        (await CreateService(factory).FindParentCyclesAsync()).Data.Should().HaveCount(1);

        (await SnapshotAsync()).Should().Be(before);
    }

    [Fact]
    public async Task FindParentCycles_IsRestrictedToSuperUsersAndUnattendedCallers()
    {
        var factory = new TestDbContextFactory();
        var a = await AddPersonAsync(factory, "A");
        var b = await AddPersonAsync(factory, "B");
        await AddParentAsync(factory, a, b);
        await AddParentAsync(factory, b, a);

        (await CreateService(factory, userId: Guid.NewGuid(), superUser: false).FindParentCyclesAsync())
            .Success.Should().BeFalse("it reports people across every family");
        (await CreateService(factory, userId: Guid.NewGuid(), superUser: true).FindParentCyclesAsync())
            .Success.Should().BeTrue();
        (await CreateService(factory, userId: null).FindParentCyclesAsync())
            .Success.Should().BeTrue("startup runs unattended, with no signed-in user");
    }

    // ── ParentGraph ──────────────────────────────────────────────────────────

    [Fact]
    public void ParentGraph_AncestorsAndDescendants()
    {
        var (g, d, k, other) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var graph = new ParentGraph([(g, d), (d, k), (other, k)]);

        graph.Descendants(g).Should().BeEquivalentTo([d, k]);
        graph.Ancestors(k).Should().BeEquivalentTo([d, g, other]);
        graph.Ancestors(g).Should().BeEmpty();
        graph.WouldCreateLoop(k, g).Should().BeTrue("Kid as Grandpa's parent closes Grandpa -> Dad -> Kid -> Grandpa");
        graph.WouldCreateLoop(g, k).Should().BeFalse("a grandparent shortcut is not a loop");
        graph.WouldCreateLoop(g, g).Should().BeTrue();
        graph.WouldCreateLoop(other, g).Should().BeFalse();
    }

    [Fact]
    public void ParentGraph_WalksVeryDeepChainsWithoutOverflowingTheStack()
    {
        // The check runs on data that may already be bad; it must not be the thing that crashes.
        var ids = Enumerable.Range(0, 200_000).Select(_ => Guid.NewGuid()).ToArray();
        var edges = Enumerable.Range(0, ids.Length - 1).Select(i => (ids[i], ids[i + 1])).ToList();
        edges.Add((ids[^1], ids[0])); // close the whole chain into one giant loop

        var graph = new ParentGraph(edges);

        graph.FindCycles().Should().ContainSingle().Which.Should().HaveCount(ids.Length);
        graph.Descendants(ids[0]).Should().HaveCount(ids.Length);
    }

    // ── ClaudeImportService.CommitAsync (deactivated UI, code still present) ──

    [Fact]
    public async Task ImportCommit_SkipsAParentLinkThatWouldCloseALoop()
    {
        var factory = new TestDbContextFactory();
        var service = new ClaudeImportService(new ThrowingHttpClientFactory(), new ConfigurationBuilder().Build(),
            factory, NullLogger<ClaudeImportService>.Instance);
        var preview = new ImportPreview
        {
            People =
            [
                new ImportPersonDto { Id = 1, FirstName = "One", LastName = "Imp" },
                new ImportPersonDto { Id = 2, FirstName = "Two", LastName = "Imp" },
                new ImportPersonDto { Id = 3, FirstName = "Three", LastName = "Imp" },
            ],
            Relationships =
            [
                new ImportRelationshipDto { Type = "Parent", PersonAId = 1, PersonBId = 2 },
                new ImportRelationshipDto { Type = "Parent", PersonAId = 2, PersonBId = 3 },
                new ImportRelationshipDto { Type = "Parent", PersonAId = 3, PersonBId = 1 }, // closes the loop
            ],
        };

        var result = await service.CommitAsync(preview);

        result.PeopleCreated.Should().Be(3);
        result.RelationshipsCreated.Should().Be(2);
        await using var ctx = factory.CreateDbContext();
        new ParentGraph(ctx.Relationships.AsEnumerable().Select(r => (r.PersonAId, r.PersonBId)))
            .FindCycles().Should().BeEmpty();
    }
}
