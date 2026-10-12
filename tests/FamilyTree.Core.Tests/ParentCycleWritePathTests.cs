using FamilyTree.Core.Data;
using FamilyTree.Core.Models;
using FamilyTree.Core.Services;
using FamilyTree.Core.Tests.Helpers;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Shared.DTOs.Relationship;
using FamilyTree.Shared.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FamilyTree.Core.Tests;

// R0 (docs/TodoList.md, "[LAYOUT FUZZ 2026-10-10] R0"): a Parent cycle (someone recorded as
// their own descendant's child) makes FamilyTreeLayoutEngine recurse until the web process
// dies with a StackOverflow, which can't be caught. Nothing stopped a user from recording
// one. These tests pin the write-path half of the fix: every code path that can persist a
// Parent relationship refuses to close a loop.
//
// Shape used throughout:  Grandpa -> Dad -> Kid   (arrows are "is parent of")
public class ParentCycleWritePathTests
{
    private static readonly Guid FamilyId = Guid.Parse("0000000f-0000-0000-0000-00000000000f");

    private sealed class World
    {
        public required TestDbContextFactory Db { get; init; }
        public Guid Grandpa, Dad, Kid;

        public PersonService People(bool admin = true) => new(
            Db, NullLogger<PersonService>.Instance, new FakeAuditLogService(),
            new FakeCurrentUserService { UserId = Guid.NewGuid(), FamilyId = FamilyId, IsAdmin = admin });

        public RelationshipService Relationships() => new(
            Db, NullLogger<RelationshipService>.Instance, new FakeAuditLogService(),
            new FakeCurrentUserService { UserId = Guid.NewGuid(), FamilyId = FamilyId, IsAdmin = true });

        public async Task<List<(Guid Parent, Guid Child)>> ParentEdgesAsync()
        {
            await using var ctx = Db.CreateDbContext();
            return (await ctx.Relationships.Where(r => r.Type == RelationshipType.Parent).ToListAsync())
                .Select(r => (r.PersonAId, r.PersonBId)).ToList();
        }

        public async Task<int> RelationshipCountAsync()
        {
            await using var ctx = Db.CreateDbContext();
            return await ctx.Relationships.CountAsync();
        }

        public async Task<Guid> AddPersonAsync(string name)
        {
            await using var ctx = Db.CreateDbContext();
            var p = new Person { Id = Guid.NewGuid(), FirstName = name, LastName = "Test", FamilyId = FamilyId, CreatedAt = DateTime.UtcNow };
            ctx.People.Add(p);
            await ctx.SaveChangesAsync();
            return p.Id;
        }

        public async Task AddParentEdgeAsync(Guid parent, Guid child)
        {
            await using var ctx = Db.CreateDbContext();
            ctx.Relationships.Add(new Relationship
            {
                Id = Guid.NewGuid(), PersonAId = parent, PersonBId = child,
                Type = RelationshipType.Parent, CreatedAt = DateTime.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        // The DTO the edit form would post for an unchanged person.
        public async Task<PersonUpsertDto> CurrentDtoAsync(Guid id)
        {
            var p = (await People().GetByIdAsync(id)).Data!;
            return new PersonUpsertDto
            {
                FirstName = p.FirstName, LastName = p.LastName,
                ParentIds = [.. p.ParentIds ?? []], ChildIds = [.. p.ChildIds ?? []],
                SpouseIds = [.. p.SpouseIds ?? []], FormerSpouseIds = [.. p.FormerSpouseIds ?? []],
                SiblingIds = [.. p.SiblingIds ?? []],
            };
        }
    }

    private static async Task<World> SeedAsync()
    {
        var w = new World { Db = new TestDbContextFactory() };
        w.Grandpa = await w.AddPersonAsync("Grandpa");
        w.Dad = await w.AddPersonAsync("Dad");
        w.Kid = await w.AddPersonAsync("Kid");
        await w.AddParentEdgeAsync(w.Grandpa, w.Dad);
        await w.AddParentEdgeAsync(w.Dad, w.Kid);
        return w;
    }

    private const string LoopWording = "loop";

    // ── PersonService.UpdateAsync ────────────────────────────────────────────

    [Fact]
    public async Task Update_AddingADescendantAsParent_IsRejected()
    {
        var w = await SeedAsync();
        var dto = await w.CurrentDtoAsync(w.Grandpa);
        dto.ParentIds = [w.Kid]; // Kid is Grandpa's grandchild

        var result = await w.People().UpdateAsync(w.Grandpa, dto);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain(LoopWording).And.Contain("Kid").And.Contain("Grandpa");
        (await w.ParentEdgesAsync()).Should().BeEquivalentTo([(w.Grandpa, w.Dad), (w.Dad, w.Kid)]);
    }

    [Fact]
    public async Task Update_AddingAnAncestorAsChild_IsRejected()
    {
        var w = await SeedAsync();
        var dto = await w.CurrentDtoAsync(w.Kid);
        dto.ChildIds = [w.Grandpa]; // Grandpa is Kid's grandfather

        var result = await w.People().UpdateAsync(w.Kid, dto);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain(LoopWording).And.Contain("Grandpa").And.Contain("Kid");
        (await w.ParentEdgesAsync()).Should().BeEquivalentTo([(w.Grandpa, w.Dad), (w.Dad, w.Kid)]);
    }

    [Fact]
    public async Task Update_PersonAsTheirOwnParent_IsRejected()
    {
        var w = await SeedAsync();
        var dto = await w.CurrentDtoAsync(w.Kid);
        dto.ParentIds = [.. dto.ParentIds, w.Kid];

        var result = await w.People().UpdateAsync(w.Kid, dto);

        result.Success.Should().BeFalse();
        (await w.ParentEdgesAsync()).Should().HaveCount(2);
    }

    // The exact shape from Invariant7b_ParentCycleDoesNotCrash: two root parents, a child A
    // with a grandchild-side B, then B ALSO recorded as one of A's parents (A has 3 parents).
    [Fact]
    public async Task Update_ThirdParentWhoIsTheirOwnChild_IsRejected()
    {
        var w = new World { Db = new TestDbContextFactory() };
        var root = await w.AddPersonAsync("Root");
        var root2 = await w.AddPersonAsync("Root2");
        var a = await w.AddPersonAsync("A");
        var b = await w.AddPersonAsync("B");
        await w.AddParentEdgeAsync(root, a);
        await w.AddParentEdgeAsync(root2, a);
        await w.AddParentEdgeAsync(a, b);

        var dto = await w.CurrentDtoAsync(a);
        dto.ParentIds = [root, root2, b];
        var result = await w.People().UpdateAsync(a, dto);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain(LoopWording);
        (await w.ParentEdgesAsync()).Should().HaveCount(3);
    }

    // Edited from the other side: on B, "child" A is also recorded as B's parent chain's top.
    [Fact]
    public async Task Update_AddingTheirOwnChildsAncestorAsChild_IsRejected()
    {
        var w = await SeedAsync();
        var dto = await w.CurrentDtoAsync(w.Dad);
        dto.ParentIds = [.. dto.ParentIds];
        dto.ChildIds = [w.Kid, w.Grandpa]; // Grandpa is Dad's own parent

        var result = await w.People().UpdateAsync(w.Dad, dto);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain(LoopWording);
    }

    [Fact]
    public async Task Update_LegitimateNewParentAndChild_StillSucceed()
    {
        var w = await SeedAsync();
        var mom = await w.AddPersonAsync("Mom");
        var sibling = await w.AddPersonAsync("Sibling");

        // A second parent for Kid (a blended family) and a brand-new child for Dad.
        var kid = await w.CurrentDtoAsync(w.Kid);
        kid.ParentIds = [.. kid.ParentIds, mom];
        (await w.People().UpdateAsync(w.Kid, kid)).Success.Should().BeTrue();

        var dad = await w.CurrentDtoAsync(w.Dad);
        dad.ChildIds = [.. dad.ChildIds, sibling];
        (await w.People().UpdateAsync(w.Dad, dad)).Success.Should().BeTrue();

        // A shortcut that is NOT a loop: Grandpa also recorded directly as Kid's parent.
        var kid2 = await w.CurrentDtoAsync(w.Kid);
        kid2.ParentIds = [.. kid2.ParentIds, w.Grandpa];
        (await w.People().UpdateAsync(w.Kid, kid2)).Success.Should().BeTrue();
    }

    // A loop already in the database (it got there before this check existed) must not lock
    // the people on it out of the edit form: saving without touching their parents works,
    // and removing the bad parent works. Only ADDING a loop-closing link is refused.
    [Fact]
    public async Task Update_OnPersonInsideAnExistingLoop_CanStillSaveAndCanRemoveTheBadLink()
    {
        var w = await SeedAsync();
        await w.AddParentEdgeAsync(w.Kid, w.Grandpa); // legacy bad data: Grandpa -> Dad -> Kid -> Grandpa

        var untouched = await w.CurrentDtoAsync(w.Grandpa);
        untouched.BiographyNotes = "Edited while the loop exists.";
        (await w.People().UpdateAsync(w.Grandpa, untouched)).Success.Should().BeTrue();

        var fix = await w.CurrentDtoAsync(w.Grandpa);
        fix.ParentIds = [];
        (await w.People().UpdateAsync(w.Grandpa, fix)).Success.Should().BeTrue();
        (await w.ParentEdgesAsync()).Should().BeEquivalentTo([(w.Grandpa, w.Dad), (w.Dad, w.Kid)]);
    }

    // ── PersonService.CreateAsync ────────────────────────────────────────────

    [Fact]
    public async Task Create_NewPersonWhoWouldSitBetweenAKidAndTheirGrandparent_IsRejected()
    {
        var w = await SeedAsync();

        // New person X: child of Kid AND parent of Grandpa => Grandpa -> Dad -> Kid -> X -> Grandpa.
        var result = await w.People().CreateAsync(new PersonUpsertDto
        {
            FirstName = "Xavier", LastName = "Test", ParentIds = [w.Kid], ChildIds = [w.Grandpa],
        });

        result.Success.Should().BeFalse();
        result.Message.Should().Contain(LoopWording);
        (await w.ParentEdgesAsync()).Should().HaveCount(2);
        await using var ctx = w.Db.CreateDbContext();
        ctx.People.Should().HaveCount(3, "a rejected create must not leave a half-created person behind");
    }

    [Fact]
    public async Task Create_ParentAlsoListedAsChild_IsRejected()
    {
        var w = await SeedAsync();

        var result = await w.People().CreateAsync(new PersonUpsertDto
        {
            FirstName = "Xavier", LastName = "Test", ParentIds = [w.Dad], ChildIds = [w.Dad],
        });

        result.Success.Should().BeFalse();
        result.Message.Should().Contain(LoopWording);
    }

    [Fact]
    public async Task Create_LegitimateParentsAndChildren_StillSucceed()
    {
        var w = await SeedAsync();

        // A new person slotted between Dad and Kid (Dad -> X -> Kid) is fine.
        var result = await w.People().CreateAsync(new PersonUpsertDto
        {
            FirstName = "Xavier", LastName = "Test", ParentIds = [w.Dad], ChildIds = [w.Kid],
        });

        result.Success.Should().BeTrue(result.Message);
    }

    // ── PersonService.RestoreAsync (resurrects cascade-deleted Parent edges) ──

    [Fact]
    public async Task Restore_ThatWouldBringBackALoop_IsRejected()
    {
        var w = await SeedAsync();

        // Delete Dad (soft-deletes Grandpa->Dad and Dad->Kid), then legitimately record Kid as
        // Grandpa's parent -- no loop exists while Dad is gone. Restoring Dad would close
        // Grandpa -> Dad -> Kid -> Grandpa.
        (await w.People().DeleteAsync(w.Dad)).Success.Should().BeTrue();
        var grandpa = await w.CurrentDtoAsync(w.Grandpa);
        grandpa.ParentIds = [w.Kid];
        (await w.People().UpdateAsync(w.Grandpa, grandpa)).Success.Should().BeTrue();

        var result = await w.People().RestoreAsync(w.Dad);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain(LoopWording);
        await using var ctx = w.Db.CreateDbContext();
        (await ctx.People.IgnoreQueryFilters().SingleAsync(p => p.Id == w.Dad)).DeletedAt
            .Should().NotBeNull("a refused restore must leave Dad soft-deleted");
    }

    [Fact]
    public async Task Restore_WithNoLoop_StillWorks()
    {
        var w = await SeedAsync();
        (await w.People().DeleteAsync(w.Dad)).Success.Should().BeTrue();

        (await w.People().RestoreAsync(w.Dad)).Success.Should().BeTrue();
        (await w.ParentEdgesAsync()).Should().HaveCount(2);
    }

    // ── RelationshipService.CreateAsync / UpdateAsync ────────────────────────

    [Fact]
    public async Task RelationshipCreate_ParentLinkThatClosesALoop_IsRejected()
    {
        var w = await SeedAsync();

        // "Kid is a parent of Grandpa".
        var result = await w.Relationships().CreateAsync(new RelationshipUpsertDto
        {
            PersonAId = w.Kid, PersonBId = w.Grandpa, Type = RelationshipType.Parent,
        });

        result.Success.Should().BeFalse();
        result.Message.Should().Contain(LoopWording);
        (await w.RelationshipCountAsync()).Should().Be(2);
    }

    // RelationshipService canonicalises EVERY type to lower-GUID-first, so a Parent row is
    // stored with whichever direction the GUID order dictates -- not necessarily the one
    // requested. The loop check must judge the edge that is actually persisted. Here the
    // requested "Grandpa is a parent of Kid" is a harmless shortcut, but with Kid's GUID
    // forced lower it is STORED as Kid -> Grandpa, which closes a loop.
    // (The direction bug itself is logged separately in docs/TodoList.md, not fixed here.)
    [Fact]
    public async Task RelationshipCreate_ParentLinkWhoseStoredDirectionClosesALoop_IsRejected()
    {
        var db = new TestDbContextFactory();
        var w = new World { Db = db };
        w.Kid = Guid.Parse("00000000-0000-0000-0000-00000000000a");
        w.Dad = Guid.Parse("00000000-0000-0000-0000-00000000000b");
        w.Grandpa = Guid.Parse("00000000-0000-0000-0000-00000000000c");
        await using (var ctx = db.CreateDbContext())
        {
            foreach (var (id, name) in new[] { (w.Kid, "Kid"), (w.Dad, "Dad"), (w.Grandpa, "Grandpa") })
                ctx.People.Add(new Person { Id = id, FirstName = name, LastName = "Test", FamilyId = FamilyId, CreatedAt = DateTime.UtcNow });
            await ctx.SaveChangesAsync();
        }
        await w.AddParentEdgeAsync(w.Grandpa, w.Dad);
        await w.AddParentEdgeAsync(w.Dad, w.Kid);

        var result = await w.Relationships().CreateAsync(new RelationshipUpsertDto
        {
            PersonAId = w.Grandpa, PersonBId = w.Kid, Type = RelationshipType.Parent,
        });

        result.Success.Should().BeFalse();
        result.Message.Should().Contain(LoopWording);
    }

    [Fact]
    public async Task RelationshipCreate_LegitimateParentLink_StillSucceeds()
    {
        var w = await SeedAsync();
        var mom = await w.AddPersonAsync("Mom");

        var result = await w.Relationships().CreateAsync(new RelationshipUpsertDto
        {
            PersonAId = mom, PersonBId = w.Kid, Type = RelationshipType.Parent,
        });

        result.Success.Should().BeTrue(result.Message);
    }

    [Fact]
    public async Task RelationshipUpdate_ChangingASiblingLinkIntoALoopClosingParentLink_IsRejected()
    {
        var w = await SeedAsync();
        var cousin = await w.AddPersonAsync("Cousin");
        var sibling = await w.Relationships().CreateAsync(new RelationshipUpsertDto
        {
            PersonAId = cousin, PersonBId = w.Grandpa, Type = RelationshipType.Sibling,
        });
        sibling.Success.Should().BeTrue(sibling.Message);

        // Re-point that row at Kid -> Grandpa, as a Parent link.
        var result = await w.Relationships().UpdateAsync(sibling.Data!.Id, new RelationshipUpsertDto
        {
            PersonAId = w.Kid, PersonBId = w.Grandpa, Type = RelationshipType.Parent,
        });

        result.Success.Should().BeFalse();
        result.Message.Should().Contain(LoopWording);
    }

    [Fact]
    public async Task RelationshipUpdate_RepointingAnExistingParentLinkIntoALoop_IsRejected()
    {
        var w = await SeedAsync();
        var mom = await w.AddPersonAsync("Mom");
        var momToKid = await w.Relationships().CreateAsync(new RelationshipUpsertDto
        {
            PersonAId = mom, PersonBId = w.Kid, Type = RelationshipType.Parent,
        });
        momToKid.Success.Should().BeTrue(momToKid.Message);

        // Re-point that harmless row at "Kid is a parent of Grandpa".
        var result = await w.Relationships().UpdateAsync(momToKid.Data!.Id, new RelationshipUpsertDto
        {
            PersonAId = w.Kid, PersonBId = w.Grandpa, Type = RelationshipType.Parent,
        });

        result.Success.Should().BeFalse();
        result.Message.Should().Contain(LoopWording);
    }

    [Fact]
    public async Task RelationshipUpdate_LegitimateEditOfAnExistingParentLink_StillSucceeds()
    {
        var w = await SeedAsync();
        var mom = await w.AddPersonAsync("Mom");
        var created = await w.Relationships().CreateAsync(new RelationshipUpsertDto
        {
            PersonAId = mom, PersonBId = w.Kid, Type = RelationshipType.Parent,
        });

        var result = await w.Relationships().UpdateAsync(created.Data!.Id, new RelationshipUpsertDto
        {
            PersonAId = mom, PersonBId = w.Kid, Type = RelationshipType.Parent, Notes = "Stepmother",
        });

        result.Success.Should().BeTrue(result.Message);
    }
}

// The two tree-linking flows (AuthService.LinkUserToTreeAsync / CreateUnlinkedPersonAsync)
// also write Parent rows, but only ever between a person they have JUST created and an
// existing one. A brand-new person has no other relationships, so one new edge touching
// them cannot close a loop, and no cycle check is needed there. These are characterization
// tests (they pass before and after the R0 fix): they fail if either flow ever starts
// writing an edge that does NOT involve the freshly created person, which is the point at
// which a loop would become possible and a check would be needed.
public class ParentCycleTreeLinkingTests
{
    private static readonly Guid FamilyId = Guid.Parse("0000000f-0000-0000-0000-00000000000f");

    private static async Task<(AuthService service, TestDbContextFactory factory, Guid userId, Guid grandpa, Guid dad, Guid kid)> SeedAsync()
    {
        var factory = new TestDbContextFactory();
        var userManager = TestUserManagerFactory.Create(factory);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:RegistrationMode"] = "Open", ["Auth:InviteTtlDays"] = "7" })
            .Build();
        var service = new AuthService(userManager, config, factory, new FakeEmailSender(),
            NullLogger<AuthService>.Instance, new FakeCurrentUserService());

        var user = new AppUser { UserName = "u@example.com", Email = "u@example.com", DisplayName = "U", CreatedAt = DateTime.UtcNow };
        (await userManager.CreateAsync(user, "TestPass123!")).Succeeded.Should().BeTrue();

        Guid Person(AppDbContext ctx, string name)
        {
            var p = new Person { Id = Guid.NewGuid(), FirstName = name, LastName = "Test", FamilyId = FamilyId, CreatedAt = DateTime.UtcNow };
            ctx.People.Add(p);
            return p.Id;
        }

        await using var ctx = factory.CreateDbContext();
        ctx.UserFamilies.Add(new UserFamily { UserId = user.Id, FamilyId = FamilyId, Role = "Member", JoinedAt = DateTime.UtcNow });
        var (grandpa, dad, kid) = (Person(ctx, "Grandpa"), Person(ctx, "Dad"), Person(ctx, "Kid"));
        ctx.Relationships.Add(new Relationship { Id = Guid.NewGuid(), PersonAId = grandpa, PersonBId = dad, Type = RelationshipType.Parent, CreatedAt = DateTime.UtcNow });
        ctx.Relationships.Add(new Relationship { Id = Guid.NewGuid(), PersonAId = dad, PersonBId = kid, Type = RelationshipType.Parent, CreatedAt = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        return (service, factory, user.Id, grandpa, dad, kid);
    }

    [Theory]
    [InlineData("ParentOfConnected", 0)]
    [InlineData("ParentOfConnected", 2)]
    [InlineData("ChildOfConnected", 0)]
    [InlineData("ChildOfConnected", 2)]
    public async Task LinkUserToTreeAsync_ParentLinkAlwaysInvolvesTheNewPerson(string token, int anchorIndex)
    {
        var (service, factory, userId, grandpa, dad, kid) = await SeedAsync();
        var anchor = new[] { grandpa, dad, kid }[anchorIndex];

        var result = await service.LinkUserToTreeAsync(userId, null, "New", "Person", anchor, token);

        result.Success.Should().BeTrue(result.Message);
        await using var ctx = factory.CreateDbContext();
        var added = ctx.Relationships.AsEnumerable().Single(r => r.PersonAId == result.Data || r.PersonBId == result.Data);
        added.Type.Should().Be(RelationshipType.Parent);
        ctx.Relationships.Count().Should().Be(3, "exactly one new edge, and it is the one touching the new person");
    }

    [Theory]
    [InlineData("ParentOfConnected")]
    [InlineData("ChildOfConnected")]
    public async Task CreateUnlinkedPersonAsync_ParentLinkAlwaysInvolvesTheNewPerson(string token)
    {
        var (service, factory, userId, grandpa, _, kid) = await SeedAsync();

        var result = await service.CreateUnlinkedPersonAsync("New", "Person", token == "ParentOfConnected" ? grandpa : kid, token, userId);

        result.Success.Should().BeTrue(result.Message);
        await using var ctx = factory.CreateDbContext();
        ctx.Relationships.AsEnumerable().Single(r => r.PersonAId == result.Data || r.PersonBId == result.Data)
            .Type.Should().Be(RelationshipType.Parent);
        ctx.Relationships.Count().Should().Be(3);
    }
}
