using FamilyTree.Core.Models;
using FamilyTree.Core.Services;
using FamilyTree.Core.Tests.Helpers;
using FamilyTree.Shared.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FamilyTree.Core.Tests;

// Regression coverage for the self-service tree-linking / registration paths added
// 2026-07-04 (the "Willa scenario") — previously had zero unit tests despite being
// the newest, most novel logic on the branch. Uses a real UserManager<AppUser>
// (TestUserManagerFactory) instead of a mock so Identity's own uniqueness/update
// behavior is exercised, not just AuthService's calls into it.
public class AuthServiceTreeLinkingTests
{
    private static (AuthService service, TestDbContextFactory factory, UserManager<AppUser> userManager) CreateSut(
        string registrationMode = "Open")
    {
        var factory = new TestDbContextFactory();
        var userManager = TestUserManagerFactory.Create(factory);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:RegistrationMode"] = registrationMode,
                ["Auth:InviteTtlDays"] = "7",
            })
            .Build();
        var service = new AuthService(userManager, config, factory, new FakeEmailSender());
        return (service, factory, userManager);
    }

    private const string ValidPassword = "TestPass123!";

    private static async Task<AppUser> CreateExistingUserAsync(UserManager<AppUser> userManager, string email)
    {
        var user = new AppUser { UserName = email, Email = email, DisplayName = "Existing User", CreatedAt = DateTime.UtcNow };
        var result = await userManager.CreateAsync(user, ValidPassword);
        result.Succeeded.Should().BeTrue();
        return user;
    }

    // ── RegisterAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterAsync_OpenMode_CreatesUserAndUserFamilyRow()
    {
        var (service, factory, _) = CreateSut("Open");

        var result = await service.RegisterAsync("Alice", "Smith", "alice@example.com", ValidPassword);

        result.Success.Should().BeTrue();
        result.UserId.Should().NotBeNull();
        await using var ctx = factory.CreateDbContext();
        var uf = ctx.UserFamilies.Single(u => u.UserId == result.UserId);
        uf.Role.Should().Be("Member");
        uf.FamilyId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RegisterAsync_ClosedMode_Fails()
    {
        var (service, _, _) = CreateSut("Closed");

        var result = await service.RegisterAsync("Alice", "Smith", "alice@example.com", ValidPassword);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task RegisterAsync_InviteOnlyMode_NoInviteId_Fails()
    {
        var (service, _, _) = CreateSut("InviteOnly");

        var result = await service.RegisterAsync("Alice", "Smith", "alice@example.com", ValidPassword);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task RegisterAsync_InviteOnlyMode_ValidInvite_UsesInviteFamilyId_AndMarksAccepted()
    {
        var (service, factory, _) = CreateSut("InviteOnly");
        var inviteFamilyId = Guid.NewGuid();
        Guid inviteId;
        await using (var ctx = factory.CreateDbContext())
        {
            ctx.Families.Add(new Family { Id = inviteFamilyId, Name = "Invite Family", CreatedAt = DateTime.UtcNow });
            var invite = new UserInvite
            {
                Id = Guid.NewGuid(),
                Email = "bob@example.com",
                FamilyId = inviteFamilyId,
                RoleToGrant = "Member",
                Token = "tok",
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                CreatedAt = DateTime.UtcNow,
            };
            ctx.UserInvites.Add(invite);
            await ctx.SaveChangesAsync();
            inviteId = invite.Id;
        }

        var result = await service.RegisterAsync("Bob", "Jones", "bob@example.com", ValidPassword, inviteId);

        result.Success.Should().BeTrue();
        await using var checkCtx = factory.CreateDbContext();
        var uf = checkCtx.UserFamilies.Single(u => u.UserId == result.UserId);
        uf.FamilyId.Should().Be(inviteFamilyId, "a registration via invite should join the invite's family, not the default one");
        var invite2 = checkCtx.UserInvites.Single(i => i.Id == inviteId);
        invite2.AcceptedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RegisterAsync_InviteOnlyMode_WrongEmailForInvite_Fails()
    {
        var (service, factory, _) = CreateSut("InviteOnly");
        Guid inviteId;
        await using (var ctx = factory.CreateDbContext())
        {
            var family = new Family { Name = "F", CreatedAt = DateTime.UtcNow };
            ctx.Families.Add(family);
            var invite = new UserInvite
            {
                Id = Guid.NewGuid(),
                Email = "expected@example.com",
                FamilyId = family.Id,
                RoleToGrant = "Member",
                Token = "tok",
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                CreatedAt = DateTime.UtcNow,
            };
            ctx.UserInvites.Add(invite);
            await ctx.SaveChangesAsync();
            inviteId = invite.Id;
        }

        var result = await service.RegisterAsync("Bob", "Jones", "someone-else@example.com", ValidPassword, inviteId);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task RegisterAsync_DuplicateEmail_Fails()
    {
        var (service, _, userManager) = CreateSut("Open");
        await CreateExistingUserAsync(userManager, "dup@example.com");

        var result = await service.RegisterAsync("Alice", "Smith", "dup@example.com", ValidPassword);

        result.Success.Should().BeFalse();
    }

    // ── EnsureUserFamilyAsync ────────────────────────────────────────────────

    [Fact]
    public async Task EnsureUserFamilyAsync_NoExistingRow_CreatesOne()
    {
        var (service, factory, userManager) = CreateSut();
        var user = await CreateExistingUserAsync(userManager, "google@example.com");

        await service.EnsureUserFamilyAsync(user.Id);

        await using var ctx = factory.CreateDbContext();
        ctx.UserFamilies.Should().ContainSingle(uf => uf.UserId == user.Id);
    }

    [Fact]
    public async Task EnsureUserFamilyAsync_AlreadyAssigned_DoesNotDuplicate()
    {
        var (service, factory, userManager) = CreateSut();
        var user = await CreateExistingUserAsync(userManager, "google2@example.com");
        await service.EnsureUserFamilyAsync(user.Id);

        await service.EnsureUserFamilyAsync(user.Id);

        await using var ctx = factory.CreateDbContext();
        ctx.UserFamilies.Count(uf => uf.UserId == user.Id).Should().Be(1);
    }

    // ── LinkUserToTreeAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task LinkUserToTreeAsync_ExistingPerson_SetsUserPersonId()
    {
        var (service, factory, userManager) = CreateSut();
        var user = await CreateExistingUserAsync(userManager, "link@example.com");
        Guid personId;
        await using (var ctx = factory.CreateDbContext())
        {
            var person = new Person { FirstName = "Rose", LastName = "Small", CreatedAt = DateTime.UtcNow };
            ctx.People.Add(person);
            await ctx.SaveChangesAsync();
            personId = person.Id;
        }

        var result = await service.LinkUserToTreeAsync(user.Id, personId, null, null);

        result.Success.Should().BeTrue();
        result.PersonId.Should().Be(personId);
        var reloaded = await userManager.FindByIdAsync(user.Id.ToString());
        reloaded!.PersonId.Should().Be(personId);
    }

    [Fact]
    public async Task LinkUserToTreeAsync_ExistingPersonId_NotFound_Fails()
    {
        var (service, _, userManager) = CreateSut();
        var user = await CreateExistingUserAsync(userManager, "link2@example.com");

        var result = await service.LinkUserToTreeAsync(user.Id, Guid.NewGuid(), null, null);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task LinkUserToTreeAsync_UnknownUser_Fails()
    {
        var (service, _, _) = CreateSut();

        var result = await service.LinkUserToTreeAsync(Guid.NewGuid(), Guid.NewGuid(), null, null);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task LinkUserToTreeAsync_NoPersonId_MissingConnectionParams_Fails()
    {
        var (service, _, userManager) = CreateSut();
        var user = await CreateExistingUserAsync(userManager, "link3@example.com");

        var result = await service.LinkUserToTreeAsync(user.Id, null, "Willa", "Kuh");

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task LinkUserToTreeAsync_NewPerson_ChildOfConnected_CreatesPersonRelationshipAndLinksUser()
    {
        var (service, factory, userManager) = CreateSut();
        var user = await CreateExistingUserAsync(userManager, "willa@example.com");
        var familyId = Guid.NewGuid();
        Guid billId;
        await using (var ctx = factory.CreateDbContext())
        {
            var bill = new Person { FirstName = "Bill", LastName = "Small", FamilyId = familyId, CreatedAt = DateTime.UtcNow };
            ctx.People.Add(bill);
            await ctx.SaveChangesAsync();
            billId = bill.Id;
        }

        var result = await service.LinkUserToTreeAsync(
            user.Id, null, "Willa", "Kuh", connectedPersonId: billId, relationshipType: "ChildOfConnected");

        result.Success.Should().BeTrue();
        result.PersonId.Should().NotBeNull();
        await using var checkCtx = factory.CreateDbContext();
        var willa = checkCtx.People.Single(p => p.Id == result.PersonId);
        willa.FamilyId.Should().Be(familyId, "a newly created person should inherit the connected person's family");

        var rel = checkCtx.Relationships.Single();
        rel.Type.Should().Be(RelationshipType.Parent);
        rel.PersonAId.Should().Be(billId, "'ChildOfConnected' means the connected person (Bill) is the parent, PersonAId");
        rel.PersonBId.Should().Be(willa.Id);

        var reloadedUser = await userManager.FindByIdAsync(user.Id.ToString());
        reloadedUser!.PersonId.Should().Be(willa.Id);
    }

    [Fact]
    public async Task LinkUserToTreeAsync_NewPerson_ParentOfConnected_RelationshipDirectionIsReversed()
    {
        var (service, factory, userManager) = CreateSut();
        var user = await CreateExistingUserAsync(userManager, "parent@example.com");
        Guid childId;
        await using (var ctx = factory.CreateDbContext())
        {
            var child = new Person { FirstName = "Child", LastName = "Small", CreatedAt = DateTime.UtcNow };
            ctx.People.Add(child);
            await ctx.SaveChangesAsync();
            childId = child.Id;
        }

        var result = await service.LinkUserToTreeAsync(
            user.Id, null, "Parent", "Small", connectedPersonId: childId, relationshipType: "ParentOfConnected");

        result.Success.Should().BeTrue();
        await using var checkCtx = factory.CreateDbContext();
        var rel = checkCtx.Relationships.Single();
        rel.Type.Should().Be(RelationshipType.Parent);
        rel.PersonAId.Should().Be(result.PersonId!.Value, "'ParentOfConnected' means the newly-linked subject is the parent, PersonAId");
        rel.PersonBId.Should().Be(childId);
    }

    [Theory]
    [InlineData("Spouse", RelationshipType.Spouse)]
    [InlineData("Sibling", RelationshipType.Sibling)]
    public async Task LinkUserToTreeAsync_NewPerson_SpouseOrSibling_CanonicallyOrdered(string token, RelationshipType expectedType)
    {
        var (service, factory, userManager) = CreateSut();
        var user = await CreateExistingUserAsync(userManager, $"{token}@example.com");
        Guid connectedId;
        await using (var ctx = factory.CreateDbContext())
        {
            var connected = new Person { FirstName = "Connected", LastName = "Person", CreatedAt = DateTime.UtcNow };
            ctx.People.Add(connected);
            await ctx.SaveChangesAsync();
            connectedId = connected.Id;
        }

        var result = await service.LinkUserToTreeAsync(
            user.Id, null, "New", "Person", connectedPersonId: connectedId, relationshipType: token);

        result.Success.Should().BeTrue();
        await using var checkCtx = factory.CreateDbContext();
        var rel = checkCtx.Relationships.Single();
        rel.Type.Should().Be(expectedType);
        (rel.PersonAId.CompareTo(rel.PersonBId) < 0).Should().BeTrue(
            "bidirectional relationship types must always store the lower GUID as PersonAId, regardless of creation order");
    }

    [Fact]
    public async Task LinkUserToTreeAsync_InvalidRelationshipType_Fails()
    {
        var (service, factory, userManager) = CreateSut();
        var user = await CreateExistingUserAsync(userManager, "invalidrel@example.com");
        Guid connectedId;
        await using (var ctx = factory.CreateDbContext())
        {
            var connected = new Person { FirstName = "Connected", LastName = "Person", CreatedAt = DateTime.UtcNow };
            ctx.People.Add(connected);
            await ctx.SaveChangesAsync();
            connectedId = connected.Id;
        }

        var result = await service.LinkUserToTreeAsync(
            user.Id, null, "New", "Person", connectedPersonId: connectedId, relationshipType: "Parent");

        result.Success.Should().BeFalse("'Parent' alone is ambiguous — only the directional tokens are valid");
    }

    [Fact]
    public async Task LinkUserToTreeAsync_ConnectedPersonNotFound_Fails()
    {
        var (service, _, userManager) = CreateSut();
        var user = await CreateExistingUserAsync(userManager, "noconn@example.com");

        var result = await service.LinkUserToTreeAsync(
            user.Id, null, "New", "Person", connectedPersonId: Guid.NewGuid(), relationshipType: "Spouse");

        result.Success.Should().BeFalse();
    }

    // ── CreateUnlinkedPersonAsync ────────────────────────────────────────────

    [Fact]
    public async Task CreateUnlinkedPersonAsync_CreatesPersonAndRelationship_InheritsConnectedFamilyId()
    {
        var (service, factory, _) = CreateSut();
        var familyId = Guid.NewGuid();
        Guid roseId;
        var createdBy = Guid.NewGuid();
        await using (var ctx = factory.CreateDbContext())
        {
            var rose = new Person { FirstName = "Rose", LastName = "Small", FamilyId = familyId, CreatedAt = DateTime.UtcNow };
            ctx.People.Add(rose);
            await ctx.SaveChangesAsync();
            roseId = rose.Id;
        }

        var result = await service.CreateUnlinkedPersonAsync("Bill", "Small", roseId, "ChildOfConnected", createdBy);

        result.Success.Should().BeTrue();
        await using var checkCtx = factory.CreateDbContext();
        var bill = checkCtx.People.Single(p => p.Id == result.PersonId);
        bill.FamilyId.Should().Be(familyId);
        bill.CreatedBy.Should().Be(createdBy);

        var rel = checkCtx.Relationships.Single();
        rel.PersonAId.Should().Be(roseId);
        rel.PersonBId.Should().Be(bill.Id);
        rel.Type.Should().Be(RelationshipType.Parent);
    }

    [Fact]
    public async Task CreateUnlinkedPersonAsync_InvalidRelationshipType_Fails()
    {
        var (service, factory, _) = CreateSut();
        Guid roseId;
        await using (var ctx = factory.CreateDbContext())
        {
            var rose = new Person { FirstName = "Rose", LastName = "Small", CreatedAt = DateTime.UtcNow };
            ctx.People.Add(rose);
            await ctx.SaveChangesAsync();
            roseId = rose.Id;
        }

        var result = await service.CreateUnlinkedPersonAsync("Bill", "Small", roseId, "Parent", Guid.NewGuid());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task CreateUnlinkedPersonAsync_ConnectedPersonNotFound_Fails()
    {
        var (service, _, _) = CreateSut();

        var result = await service.CreateUnlinkedPersonAsync("Bill", "Small", Guid.NewGuid(), "Spouse", Guid.NewGuid());

        result.Success.Should().BeFalse();
    }
}
