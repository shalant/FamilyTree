using FamilyTree.Core.Models;
using FamilyTree.Core.Services;
using FamilyTree.Core.Tests.Helpers;
using FamilyTree.Shared.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
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
        string? registrationMode = "Open")
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
        var logger = new NullLogger<AuthService>();
        var service = new AuthService(userManager, config, factory, new FakeEmailSender(), logger, new FakeCurrentUserService());
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

    // Since 2026-10-10 tree linking stays inside the user's own family (finding S4,
    // docs/security.md), so linking tests put the user and the seeded people in one
    // family, as RegisterAsync does for every real account.
    private static readonly Guid TestFamilyId = Guid.Parse("0000000f-0000-0000-0000-00000000000f");

    private static async Task PutInFamilyAsync(TestDbContextFactory factory, Guid userId, Guid? familyId = null)
    {
        await using var ctx = factory.CreateDbContext();
        ctx.UserFamilies.Add(new UserFamily { UserId = userId, FamilyId = familyId ?? TestFamilyId, Role = "Member", JoinedAt = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
    }

    // ── RegisterAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterAsync_OpenMode_CreatesUserAndUserFamilyRow()
    {
        var (service, factory, _) = CreateSut("Open");

        var result = await service.RegisterAsync("Alice", "Smith", "alice@example.com", ValidPassword);

        result.Success.Should().BeTrue();
        result.Data.Should().NotBe(Guid.Empty);
        await using var ctx = factory.CreateDbContext();
        var uf = ctx.UserFamilies.Single(u => u.UserId == result.Data);
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

    // S9 (docs/security.md): a missing or mistyped mode used to mean Open.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Invite-Only")]
    [InlineData("closd")]
    public async Task RegisterAsync_UnrecognisedMode_FailsClosedToInviteOnly(string? configured)
    {
        var (service, _, _) = CreateSut(registrationMode: configured);

        service.GetRegistrationMode().Should().Be("InviteOnly");
        var result = await service.RegisterAsync("New", "User", "new@example.com", ValidPassword);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("invitation is required");
    }

    [Theory]
    [InlineData("open", "Open")]
    [InlineData(" INVITEONLY ", "InviteOnly")]
    [InlineData("closed", "Closed")]
    public void GetRegistrationMode_ForgivesCaseAndSpaces(string configured, string expected)
    {
        var (service, _, _) = CreateSut(registrationMode: configured);

        service.GetRegistrationMode().Should().Be(expected);
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
        var uf = checkCtx.UserFamilies.Single(u => u.UserId == result.Data);
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

    // ── UserExistsAsync ──────────────────────────────────────────────────────
    // Backs the story-invite-response "sign in" vs. "create account" decision
    // (StoryInviteService.SubmitResponseAsync) — found 2026-07-06 when Ellen, who
    // already had an account, was still redirected to /register after submitting a
    // story about Morton.

    [Fact]
    public async Task UserExistsAsync_ExistingEmail_ReturnsTrue()
    {
        var (service, _, userManager) = CreateSut();
        await CreateExistingUserAsync(userManager, "ellen@example.com");

        var exists = await service.UserExistsAsync("ellen@example.com");
        exists.Data.Should().BeTrue();
    }

    [Fact]
    public async Task UserExistsAsync_UnknownEmail_ReturnsFalse()
    {
        var (service, _, _) = CreateSut();

        var exists = await service.UserExistsAsync("nobody@example.com");
        exists.Data.Should().BeFalse();
    }

    // ── RegisterExternalAsync (first Google sign-in) ─────────────────────────
    // A6, 2026-10-25: in invite-only mode an invitee couldn't sign up with Google at all;
    // the callback refused every new account.

    private static async Task<UserInvite> SeedInviteAsync(
        TestDbContextFactory factory, string email, DateTime? expiresAt = null)
    {
        await using var ctx = factory.CreateDbContext();
        var invite = new UserInvite
        {
            Id = Guid.NewGuid(), Email = email, FamilyId = TestFamilyId, RoleToGrant = "Member",
            Token = "t", ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(7), CreatedAt = DateTime.UtcNow,
        };
        ctx.UserInvites.Add(invite);
        await ctx.SaveChangesAsync();
        return invite;
    }

    [Fact]
    public async Task RegisterExternalAsync_InviteOnly_InvitedVerifiedAddress_JoinsTheInvitesFamily()
    {
        var (service, factory, userManager) = CreateSut("InviteOnly");
        var invite = await SeedInviteAsync(factory, "willa@example.com");

        var result = await service.RegisterExternalAsync("Willa@Example.com", "Willa", "Small", emailVerified: true);

        result.Success.Should().BeTrue(result.Message);
        (await userManager.FindByIdAsync(result.Data.ToString()))!.DisplayName.Should().Be("Willa Small");
        await using var ctx = factory.CreateDbContext();
        ctx.UserFamilies.Should().ContainSingle(uf => uf.UserId == result.Data && uf.FamilyId == TestFamilyId);
        (await ctx.UserInvites.FindAsync(invite.Id))!.AcceptedAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData("stranger@example.com", true, false)]   // no invite
    [InlineData("willa@example.com", false, false)]     // Google hasn't verified the address
    [InlineData("willa@example.com", true, true)]       // the invite has expired
    public async Task RegisterExternalAsync_InviteOnly_WithoutAUsableInvite_CreatesNothing(
        string email, bool verified, bool expired)
    {
        var (service, factory, userManager) = CreateSut("InviteOnly");
        await SeedInviteAsync(factory, "willa@example.com", expired ? DateTime.UtcNow.AddDays(-1) : null);

        var result = await service.RegisterExternalAsync(email, "Willa", "Small", verified);

        result.Success.Should().BeFalse();
        result.Message.Should().Be(AuthService.NoInviteForExternalSignIn);
        (await userManager.FindByEmailAsync(email)).Should().BeNull();
    }

    [Fact]
    public async Task RegisterExternalAsync_Open_NeedsNoInvite()
    {
        var (service, factory, _) = CreateSut("Open");

        var result = await service.RegisterExternalAsync("anyone@example.com", "Any", "One", emailVerified: false);

        result.Success.Should().BeTrue(result.Message);
        await using var ctx = factory.CreateDbContext();
        ctx.UserFamilies.Should().ContainSingle(uf => uf.UserId == result.Data);
    }

    [Fact]
    public async Task RegisterExternalAsync_Closed_Refuses()
    {
        var (service, factory, _) = CreateSut("Closed");
        await SeedInviteAsync(factory, "willa@example.com");

        var result = await service.RegisterExternalAsync("willa@example.com", "Willa", "Small", emailVerified: true);

        result.Message.Should().Be(AuthService.RegistrationClosed);
    }

    // ── CreateInviteForStoryResponseAsync ────────────────────────────────────

    // A6, 2026-10-25: someone already sent a link from Admin > Users who then answered a
    // story invite got a failure here, and the response page's "Create a free account"
    // pointed at /register?invite= with no ID: a dead end in invite-only mode.
    [Fact]
    public async Task CreateInviteForStoryResponseAsync_OpenInviteExists_ReturnsIt()
    {
        var (service, factory, _) = CreateSut("InviteOnly");
        var existing = await SeedInviteAsync(factory, "willa@example.com");

        var result = await service.CreateInviteForStoryResponseAsync("Willa@example.com", TestFamilyId);

        result.Success.Should().BeTrue();
        result.Data.Should().Be(existing.Id);
        await using var ctx = factory.CreateDbContext();
        ctx.UserInvites.Should().ContainSingle();
    }

    // ── LinkUserToTreeAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task LinkUserToTreeAsync_ExistingPerson_SetsUserPersonId()
    {
        var (service, factory, userManager) = CreateSut();
        var user = await CreateExistingUserAsync(userManager, "link@example.com");
        await PutInFamilyAsync(factory, user.Id);
        Guid personId;
        await using (var ctx = factory.CreateDbContext())
        {
            var person = new Person { FirstName = "Rose", LastName = "Small", FamilyId = TestFamilyId, CreatedAt = DateTime.UtcNow };
            ctx.People.Add(person);
            await ctx.SaveChangesAsync();
            personId = person.Id;
        }

        var result = await service.LinkUserToTreeAsync(user.Id, personId, null, null);

        result.Success.Should().BeTrue();
        result.Data.Should().Be(personId);
        var reloaded = await userManager.FindByIdAsync(user.Id.ToString());
        reloaded!.PersonId.Should().Be(personId);
    }

    // Regression for 2026-07-08: two different accounts both named "Elliot Rosenberg"
    // meant the duplicate-detection modal could offer an already-claimed Person as a
    // "is this you?" match, and LinkUserToTreeAsync used to let SQL Server's
    // IX_AspNetUsers_PersonId unique index reject it with a raw DbUpdateException
    // instead of a clean failure. See PersonNameDuplicateMatcher/LinkToTreeModal's
    // GetLinkedPersonIdsAsync filtering for the other half of the fix.
    [Fact]
    public async Task LinkUserToTreeAsync_PersonAlreadyLinkedToAnotherUser_FailsCleanly_DoesNotThrow()
    {
        var (service, factory, userManager) = CreateSut();
        var firstUser = await CreateExistingUserAsync(userManager, "first@example.com");
        await PutInFamilyAsync(factory, firstUser.Id);
        var secondUser = await CreateExistingUserAsync(userManager, "second@example.com");
        await PutInFamilyAsync(factory, secondUser.Id);
        Guid personId;
        await using (var ctx = factory.CreateDbContext())
        {
            var person = new Person { FirstName = "Elliot", LastName = "Rosenberg", FamilyId = TestFamilyId, CreatedAt = DateTime.UtcNow };
            ctx.People.Add(person);
            await ctx.SaveChangesAsync();
            personId = person.Id;
        }
        (await service.LinkUserToTreeAsync(firstUser.Id, personId, null, null)).Success.Should().BeTrue();

        var result = await service.LinkUserToTreeAsync(secondUser.Id, personId, null, null);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("already linked");
        var reloadedSecond = await userManager.FindByIdAsync(secondUser.Id.ToString());
        reloadedSecond!.PersonId.Should().BeNull("the second user must not end up half-linked after a failed claim");
    }

    // The same person re-linking themselves (e.g. re-running the flow) must still work —
    // only a *different* user holding the PersonId should be treated as a conflict.
    [Fact]
    public async Task LinkUserToTreeAsync_SameUserRelinkingSamePerson_StillSucceeds()
    {
        var (service, factory, userManager) = CreateSut();
        var user = await CreateExistingUserAsync(userManager, "relink@example.com");
        await PutInFamilyAsync(factory, user.Id);
        Guid personId;
        await using (var ctx = factory.CreateDbContext())
        {
            var person = new Person { FirstName = "Elliot", LastName = "Rosenberg", FamilyId = TestFamilyId, CreatedAt = DateTime.UtcNow };
            ctx.People.Add(person);
            await ctx.SaveChangesAsync();
            personId = person.Id;
        }
        (await service.LinkUserToTreeAsync(user.Id, personId, null, null)).Success.Should().BeTrue();

        var result = await service.LinkUserToTreeAsync(user.Id, personId, null, null);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task LinkUserToTreeAsync_ExistingPersonId_NotFound_Fails()
    {
        var (service, factory, userManager) = CreateSut();
        var user = await CreateExistingUserAsync(userManager, "link2@example.com");
        await PutInFamilyAsync(factory, user.Id);

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
        var (service, factory, userManager) = CreateSut();
        var user = await CreateExistingUserAsync(userManager, "link3@example.com");
        await PutInFamilyAsync(factory, user.Id);

        var result = await service.LinkUserToTreeAsync(user.Id, null, "Willa", "Kuh");

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task LinkUserToTreeAsync_NewPerson_ChildOfConnected_CreatesPersonRelationshipAndLinksUser()
    {
        var (service, factory, userManager) = CreateSut();
        var user = await CreateExistingUserAsync(userManager, "willa@example.com");
        var familyId = Guid.NewGuid();
        await PutInFamilyAsync(factory, user.Id, familyId);
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
        result.Data.Should().NotBe(Guid.Empty);
        await using var checkCtx = factory.CreateDbContext();
        var willa = checkCtx.People.Single(p => p.Id == result.Data);
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
        await PutInFamilyAsync(factory, user.Id);
        Guid childId;
        await using (var ctx = factory.CreateDbContext())
        {
            var child = new Person { FirstName = "Child", LastName = "Small", FamilyId = TestFamilyId, CreatedAt = DateTime.UtcNow };
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
        rel.PersonAId.Should().Be(result.Data!.Value, "'ParentOfConnected' means the newly-linked subject is the parent, PersonAId");
        rel.PersonBId.Should().Be(childId);
    }

    [Theory]
    [InlineData("Spouse", RelationshipType.Spouse)]
    [InlineData("Sibling", RelationshipType.Sibling)]
    public async Task LinkUserToTreeAsync_NewPerson_SpouseOrSibling_CanonicallyOrdered(string token, RelationshipType expectedType)
    {
        var (service, factory, userManager) = CreateSut();
        var user = await CreateExistingUserAsync(userManager, $"{token}@example.com");
        await PutInFamilyAsync(factory, user.Id);
        Guid connectedId;
        await using (var ctx = factory.CreateDbContext())
        {
            var connected = new Person { FirstName = "Connected", LastName = "Person", FamilyId = TestFamilyId, CreatedAt = DateTime.UtcNow };
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
        await PutInFamilyAsync(factory, user.Id);
        Guid connectedId;
        await using (var ctx = factory.CreateDbContext())
        {
            var connected = new Person { FirstName = "Connected", LastName = "Person", FamilyId = TestFamilyId, CreatedAt = DateTime.UtcNow };
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
        var (service, factory, userManager) = CreateSut();
        var user = await CreateExistingUserAsync(userManager, "noconn@example.com");
        await PutInFamilyAsync(factory, user.Id);

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
        await PutInFamilyAsync(factory, createdBy, familyId);
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
        var bill = checkCtx.People.Single(p => p.Id == result.Data);
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
            var rose = new Person { FirstName = "Rose", LastName = "Small", FamilyId = TestFamilyId, CreatedAt = DateTime.UtcNow };
            ctx.People.Add(rose);
            await ctx.SaveChangesAsync();
            roseId = rose.Id;
        }

        var creator = Guid.NewGuid();
        await PutInFamilyAsync(factory, creator);
        var result = await service.CreateUnlinkedPersonAsync("Bill", "Small", roseId, "Parent", creator);

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
