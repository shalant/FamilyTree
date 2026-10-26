using FamilyTree.Core.Data;
using FamilyTree.Core.Models;
using FamilyTree.Core.Services;
using FamilyTree.Core.Tests.Helpers;
using FamilyTree.Shared.DTOs.Medium;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Shared.DTOs.Relationship;
using FamilyTree.Shared.DTOs.Story;
using FamilyTree.Shared.DTOs.StoryInvite;
using FamilyTree.Shared.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FamilyTree.Core.Tests;

// Cross-tenant test matrix (Leg 3 of docs/Testing/fuzz-and-adversarial-review-plan.md,
// added 2026-10-10). Seeds two families, then calls every public method of the scoped
// services as a family-A caller against family-B IDs and asserts the call fails or
// returns nothing, and that family B's rows are unchanged. Findings are numbered S1-S9
// in docs/security.md ("Scoping and auth adversarial review").
//
// Each row runs as both a family-A Member and a family-A Admin unless the method is
// role-specific: the Admin role is global (not per-family), so the Admin case is the one
// that catches a missing family check behind a role check.
//
// The PositiveControl_* tests call the same methods as family B and must SUCCEED. Without
// them every "A can't see B" row would also pass against a broken seed.
//
// When adding a public method to one of these services, add a row here.
public class CrossTenantMatrixTests
{
    public enum Caller { MemberA, AdminA }

    // Rows that failed against the code as of 2026-10-10 (57 of 90). Each fix PR deletes
    // its constant and the Skips that use it. Never skip a new row to get green.

    // ── Seed ─────────────────────────────────────────────────────────────────────

    private sealed class World
    {
        public required TestDbContextFactory Db { get; init; }
        public required UserManager<AppUser> Users { get; init; }
        public Guid FamilyA, FamilyB;
        public Guid UserA, UserB, AdminB, SuperUserId;
        public Guid A1, A2, B1, B2, BDeleted;
        public Guid RelB, RelCrossLegacy;
        public Guid MediumB;
        public Guid StoryBLinked, StoryBUnlinked, StoryBPending, StoryBHidden, StoryAUnlinked;
        public Guid StoryInviteB;
        public string StoryInviteBToken = "";
        public Guid UserInviteB, ResetRequestB, AuditB;

        public FakeCurrentUserService As(Caller c) => c switch
        {
            Caller.MemberA => new FakeCurrentUserService { UserId = UserA, FamilyId = FamilyA },
            Caller.AdminA  => new FakeCurrentUserService { UserId = UserA, FamilyId = FamilyA, IsAdmin = true },
            _ => throw new ArgumentOutOfRangeException(nameof(c)),
        };

        public FakeCurrentUserService MemberB => new() { UserId = UserB, FamilyId = FamilyB };
        public FakeCurrentUserService AdminBUser => new() { UserId = AdminB, FamilyId = FamilyB, IsAdmin = true };
        public FakeCurrentUserService SuperUser => new() { UserId = SuperUserId, IsSuperUser = true };
        public static FakeCurrentUserService Anonymous => new();
    }

    private static async Task<World> SeedAsync()
    {
        var db = new TestDbContextFactory();
        var w = new World { Db = db, Users = TestUserManagerFactory.Create(db) };
        var now = DateTime.UtcNow;

        await using var ctx = db.CreateDbContext();

        // Family A is the OLDEST family: every "falls back to the default family" bug
        // lands a stray row there, so B-originated actions that end up in A show up.
        w.FamilyA = Guid.NewGuid(); w.FamilyB = Guid.NewGuid();
        ctx.Families.Add(new Family { Id = w.FamilyA, Name = "Family A", CreatedAt = now.AddYears(-2) });
        ctx.Families.Add(new Family { Id = w.FamilyB, Name = "Family B", CreatedAt = now.AddYears(-1) });

        AppUser NewUser(string email, bool superUser = false) => new()
        {
            Id = Guid.NewGuid(), UserName = email, NormalizedUserName = email.ToUpperInvariant(),
            Email = email, NormalizedEmail = email.ToUpperInvariant(), DisplayName = email,
            IsSuperUser = superUser, SecurityStamp = Guid.NewGuid().ToString(), CreatedAt = now,
        };
        var userA = NewUser("a@a.test"); var userB = NewUser("b@b.test");
        var adminB = NewUser("admin@b.test"); var su = NewUser("su@root.test", superUser: true);
        ctx.Users.AddRange(userA, userB, adminB, su);
        w.UserA = userA.Id; w.UserB = userB.Id; w.AdminB = adminB.Id; w.SuperUserId = su.Id;
        ctx.UserFamilies.Add(new UserFamily { UserId = w.UserA, FamilyId = w.FamilyA, Role = "Member", JoinedAt = now });
        ctx.UserFamilies.Add(new UserFamily { UserId = w.UserB, FamilyId = w.FamilyB, Role = "Member", JoinedAt = now });
        ctx.UserFamilies.Add(new UserFamily { UserId = w.AdminB, FamilyId = w.FamilyB, Role = "Admin", JoinedAt = now });

        Person P(string first, string last, Guid family, DateOnly? born = null, DateOnly? died = null) => new()
        {
            Id = Guid.NewGuid(), FirstName = first, LastName = last, FamilyId = family,
            BirthDate = born, DeathDate = died, CreatedAt = now, BiographyNotes = $"{first} private bio",
        };
        var a1 = P("Alice", "Anders", w.FamilyA, new DateOnly(1990, 1, 1));
        var a2 = P("Aaron", "Anders", w.FamilyA);
        var b1 = P("Beatrix", "Brennan", w.FamilyB, new DateOnly(1930, 1, 1), new DateOnly(1980, 1, 1));
        var b2 = P("Bruno", "Brennan", w.FamilyB, new DateOnly(1960, 1, 1));
        var bDeleted = P("Bertha", "Brennan", w.FamilyB);
        bDeleted.DeletedAt = now.AddDays(-1);
        ctx.People.AddRange(a1, a2, b1, b2, bDeleted);
        w.A1 = a1.Id; w.A2 = a2.Id; w.B1 = b1.Id; w.B2 = b2.Id; w.BDeleted = bDeleted.Id;
        ctx.Users.Find(w.UserA)!.PersonId = w.A1;

        var relB = new Relationship { Id = Guid.NewGuid(), PersonAId = w.B1, PersonBId = w.B2, Type = RelationshipType.Parent, CreatedAt = now };
        // A cross-family row as it could exist today via the unscoped PersonService
        // relation lists (S4). Reads must not use it to surface B's IDs to A.
        var (lo, hi) = w.A2 < w.B2 ? (w.A2, w.B2) : (w.B2, w.A2);
        var relCross = new Relationship { Id = Guid.NewGuid(), PersonAId = lo, PersonBId = hi, Type = RelationshipType.Sibling, CreatedAt = now };
        ctx.Relationships.AddRange(relB, relCross);
        w.RelB = relB.Id; w.RelCrossLegacy = relCross.Id;

        var mediumB = new Medium { Id = Guid.NewGuid(), PersonId = w.B1, Url = "https://fake-blob/b1.jpg", FileName = "b1.jpg", MimeType = "image/jpeg", Type = "photo", CreatedAt = now };
        ctx.Media.Add(mediumB);
        w.MediumB = mediumB.Id;

        var inviteB = new StoryInvite
        {
            Id = Guid.NewGuid(), Token = "token-b-" + Guid.NewGuid().ToString("N"), PersonId = w.B1,
            InvitedEmail = "responder@b.test", InvitedByUserId = w.UserB, CreatedAt = now, ExpiresAt = now.AddDays(30),
        };
        ctx.StoryInvites.Add(inviteB);
        w.StoryInviteB = inviteB.Id; w.StoryInviteBToken = inviteB.Token;

        Story S(Guid? personId, Guid? authorId, bool approved = true, bool hidden = false, int sort = 0, string? unlinked = null) => new()
        {
            Id = Guid.NewGuid(), PersonId = personId, AuthorId = authorId, UnlinkedPersonName = unlinked,
            Title = $"Story {Guid.NewGuid():N}", Body = "B family private memory", IsApproved = approved,
            IsHidden = hidden, SortOrder = sort, CreatedAt = now,
        };
        var sLinked = S(w.B1, w.UserB, sort: 1);
        var sUnlinked = S(null, w.UserB, sort: 2, unlinked: "Great-uncle Basil");
        var sPending = S(w.B1, null, approved: false, sort: 3);
        sPending.AuthorName = "Anonymous responder"; sPending.InviteId = w.StoryInviteB;
        var sHidden = S(w.B1, w.UserB, hidden: true, sort: 4);
        var sAUnlinked = S(null, w.UserA, sort: 0, unlinked: "Cousin Ada");
        sAUnlinked.Body = "A family memory";
        ctx.Stories.AddRange(sLinked, sUnlinked, sPending, sHidden, sAUnlinked);
        w.StoryBLinked = sLinked.Id; w.StoryBUnlinked = sUnlinked.Id; w.StoryBPending = sPending.Id;
        w.StoryBHidden = sHidden.Id; w.StoryAUnlinked = sAUnlinked.Id;

        var userInviteB = new UserInvite
        {
            Id = Guid.NewGuid(), Email = "newbie@b.test", FamilyId = w.FamilyB, RoleToGrant = "Member",
            Token = Guid.NewGuid().ToString("N"), ExpiresAt = now.AddDays(7), CreatedAt = now,
        };
        ctx.UserInvites.Add(userInviteB);
        w.UserInviteB = userInviteB.Id;

        var resetB = new PasswordResetRequest
        {
            Id = Guid.NewGuid(), Email = "su@root.test", Token = "reset-token", CreatedAt = now, ExpiresAt = now.AddHours(24),
        };
        ctx.PasswordResetRequests.Add(resetB);
        w.ResetRequestB = resetB.Id;

        var auditB = new AuditLog { Id = Guid.NewGuid(), UserId = w.UserB, Action = "Update", EntityType = "Person", EntityId = w.B1, Timestamp = now };
        ctx.AuditLogs.Add(auditB);
        w.AuditB = auditB.Id;

        await ctx.SaveChangesAsync();
        return w;
    }

    // ── Service construction (one place, so constructor changes touch one spot) ──

    private static PersonService PersonSvc(World w, ICurrentUserService u) =>
        new(w.Db, NullLogger<PersonService>.Instance, new FakeAuditLogService(), u);

    private static RelationshipService RelSvc(World w, ICurrentUserService u) =>
        new(w.Db, NullLogger<RelationshipService>.Instance, new FakeAuditLogService(), u);

    private static MediumService MediumSvc(World w, ICurrentUserService u, FakeBlobStorageService? blob = null) =>
        new(w.Db, blob ?? new FakeBlobStorageService(), NullLogger<MediumService>.Instance, new FakeAuditLogService(), u);

    private static StoryService StorySvc(World w, ICurrentUserService u) =>
        new(w.Db, NullLogger<StoryService>.Instance, new FakeAuditLogService(), u);

    private static GedcomExportService GedcomSvc(World w, ICurrentUserService u) => new(w.Db, u);

    private static IConfiguration Config(string mode = "InviteOnly") => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:RegistrationMode"] = mode,
            ["Auth:InviteTtlDays"] = "7",
            ["Stories:InviteTtlDays"] = "30",
        }).Build();

    private static AuthService AuthSvc(World w, ICurrentUserService u, string mode = "InviteOnly", IEmailSender? email = null) =>
        new(w.Users, Config(mode), w.Db, email ?? new FakeEmailSender(), NullLogger<AuthService>.Instance, u);

    private static StoryInviteService StoryInviteSvc(World w, ICurrentUserService u, IEmailSender? email = null) =>
        new(w.Db, NullLogger<StoryInviteService>.Instance, new FakeAuditLogService(), u,
            email ?? new FakeEmailSender(), Config(), AuthSvc(w, u));

    private static AdminUserService AdminUserSvc(World w, ICurrentUserService u) =>
        new(w.Db, NullLogger<AdminUserService>.Instance, u);

    private static AdminAuditService AdminAuditSvc(World w, ICurrentUserService u) =>
        new(w.Db, NullLogger<AdminAuditService>.Instance, u);

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<(string To, string Subject, string Body)> Sent { get; } = [];
        public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
        {
            Sent.Add((to, subject, htmlBody));
            return Task.CompletedTask;
        }
    }

    private static async Task<T> Read<T>(World w, Func<AppDbContext, Task<T>> q)
    {
        await using var ctx = w.Db.CreateDbContext();
        return await q(ctx);
    }

    private static PersonUpsertDto Dto(string first, string last) => new() { FirstName = first, LastName = last };

    private static readonly string[] BNames = ["Beatrix", "Bruno", "Bertha", "Brennan"];

    // ── PersonService ───────────────────────────────────────────────────────────

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Person_GetAllAsync_ReturnsNoFamilyBPeople(Caller c)
    {
        var w = await SeedAsync();
        var r = await PersonSvc(w, w.As(c)).GetAllAsync();
        r.Success.Should().BeTrue();
        r.Data!.Select(p => p.Id).Should().NotContain([w.B1, w.B2, w.BDeleted]);
    }

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Person_GetByIdAsync_FamilyBPerson_Fails(Caller c)
    {
        var w = await SeedAsync();
        var r = await PersonSvc(w, w.As(c)).GetByIdAsync(w.B1);
        r.Success.Should().BeFalse();
        r.Data.Should().BeNull();
    }

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Person_GetByIdAsync_OwnPerson_DoesNotExposeFamilyBRelationIds(Caller c)
    {
        var w = await SeedAsync();
        var r = await PersonSvc(w, w.As(c)).GetByIdAsync(w.A2);
        r.Success.Should().BeTrue();
        r.Data!.SiblingIds.Should().NotContain(w.B2);
    }

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Person_GetDeletedAsync_ReturnsNoFamilyBPeople(Caller c)
    {
        var w = await SeedAsync();
        var r = await PersonSvc(w, w.As(c)).GetDeletedAsync();
        r.Success.Should().BeTrue();
        r.Data!.Select(p => p.Id).Should().NotContain(w.BDeleted);
    }

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Person_UpdateAsync_FamilyBPerson_FailsAndLeavesItUnchanged(Caller c)
    {
        var w = await SeedAsync();
        var r = await PersonSvc(w, w.As(c)).UpdateAsync(w.B1, Dto("Hacked", "Name"));
        r.Success.Should().BeFalse();
        var b1 = await Read(w, ctx => ctx.People.SingleAsync(p => p.Id == w.B1));
        b1.FirstName.Should().Be("Beatrix");
    }

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Person_DeleteAsync_FamilyBPerson_FailsAndLeavesItLive(Caller c)
    {
        var w = await SeedAsync();
        var r = await PersonSvc(w, w.As(c)).DeleteAsync(w.B1);
        r.Success.Should().BeFalse();
        (await Read(w, ctx => ctx.People.AnyAsync(p => p.Id == w.B1))).Should().BeTrue();
    }

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Person_RestoreAsync_FamilyBPerson_FailsAndLeavesItDeleted(Caller c)
    {
        var w = await SeedAsync();
        var r = await PersonSvc(w, w.As(c)).RestoreAsync(w.BDeleted);
        r.Success.Should().BeFalse();
        var p = await Read(w, ctx => ctx.People.IgnoreQueryFilters().SingleAsync(x => x.Id == w.BDeleted));
        p.DeletedAt.Should().NotBeNull();
    }

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Person_SetProfilePhotoAsync_FamilyBPerson_FailsAndLeavesItUnchanged(Caller c)
    {
        var w = await SeedAsync();
        var r = await PersonSvc(w, w.As(c)).SetProfilePhotoAsync(w.B1, w.MediumB);
        r.Success.Should().BeFalse();
        r.Message.Should().NotContainAny(BNames);
        var b1 = await Read(w, ctx => ctx.People.SingleAsync(p => p.Id == w.B1));
        b1.ProfilePhotoUrl.Should().BeNull();
    }

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Person_CreateAsync_WithFamilyBRelative_FailsAndLinksNothing(Caller c)
    {
        var w = await SeedAsync();
        var dto = Dto("New", "Kid");
        dto.ParentIds.Add(w.B2);
        var r = await PersonSvc(w, w.As(c)).CreateAsync(dto);
        r.Success.Should().BeFalse();
        (await Read(w, ctx => ctx.Relationships.CountAsync(x => x.PersonAId == w.B2 && x.Type == RelationshipType.Parent)))
            .Should().Be(0, "B2 had no children before the call");
        (await Read(w, ctx => ctx.People.AnyAsync(p => p.FirstName == "New"))).Should().BeFalse();
    }

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Person_CreateAsync_WithFamilyBRelative_DoesNotEchoTheirDetails(Caller c)
    {
        var w = await SeedAsync();
        // Born long after B1's death: the parent validator used to answer with B1's name
        // and death date before any scope check ran.
        var dto = Dto("New", "Kid");
        dto.BirthDate = new DateOnly(2000, 1, 1);
        dto.ParentIds.Add(w.B1);
        var r = await PersonSvc(w, w.As(c)).CreateAsync(dto);
        r.Success.Should().BeFalse();
        r.Message.Should().NotContainAny(BNames);
    }

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Person_CreateAsync_WithFamilyBSpouse_DoesNotRevealMaritalStatus(Caller c)
    {
        var w = await SeedAsync();
        var dto = Dto("New", "Spouse");
        dto.SpouseIds.Add(w.B1);
        var r = await PersonSvc(w, w.As(c)).CreateAsync(dto);
        r.Success.Should().BeFalse();
        r.Message.Should().NotContainAny(BNames);
    }

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Person_UpdateAsync_OwnPersonPointedAtFamilyBRelative_Fails(Caller c)
    {
        var w = await SeedAsync();
        var dto = Dto("Alice", "Anders");
        dto.ChildIds.Add(w.B2);
        var r = await PersonSvc(w, w.As(c)).UpdateAsync(w.A1, dto);
        r.Success.Should().BeFalse();
        (await Read(w, ctx => ctx.Relationships.AnyAsync(x => x.PersonAId == w.A1 && x.PersonBId == w.B2))).Should().BeFalse();
    }

    [Fact]
    public async Task Person_UpdateAsync_MemberGetsSameMessageForForeignAndMissingIds()
    {
        var w = await SeedAsync();
        var svc = PersonSvc(w, w.As(Caller.MemberA));
        var foreign = await svc.UpdateAsync(w.B1, Dto("X", "Y"));
        var missing = await svc.UpdateAsync(Guid.NewGuid(), Dto("X", "Y"));
        foreign.Success.Should().BeFalse();
        missing.Success.Should().BeFalse();
        Normalize(foreign.Message, w.B1).Should().Be(Normalize(missing.Message, null));
    }

    [Fact]
    public async Task Person_GetAllForUserAsync_ForFamilyAUser_ReturnsNoFamilyBPeople()
    {
        var w = await SeedAsync();
        var r = await PersonSvc(w, w.As(Caller.MemberA)).GetAllForUserAsync(w.UserA);
        r.Success.Should().BeTrue();
        r.Data!.Select(p => p.Id).Should().NotContain([w.B1, w.B2]);
    }

    // ── RelationshipService ─────────────────────────────────────────────────────

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Relationship_GetAllAsync_ReturnsNoFamilyBRelationships(Caller c)
    {
        var w = await SeedAsync();
        var r = await RelSvc(w, w.As(c)).GetAllAsync();
        r.Success.Should().BeTrue();
        r.Data!.Select(x => x.Id).Should().NotContain([w.RelB, w.RelCrossLegacy]);
    }

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Relationship_GetForPersonAsync_FamilyBPerson_ReturnsNothing(Caller c)
    {
        var w = await SeedAsync();
        var r = await RelSvc(w, w.As(c)).GetForPersonAsync(w.B1);
        (r.Data ?? []).Should().BeEmpty();
    }

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Relationship_GetForPersonAsync_OwnPerson_DoesNotExposeFamilyBLinks(Caller c)
    {
        var w = await SeedAsync();
        var r = await RelSvc(w, w.As(c)).GetForPersonAsync(w.A2);
        r.Success.Should().BeTrue();
        r.Data!.Select(x => x.Id).Should().NotContain(w.RelCrossLegacy);
    }

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Relationship_CreateAsync_BetweenFamilyBPeople_Fails(Caller c)
    {
        var w = await SeedAsync();
        var r = await RelSvc(w, w.As(c)).CreateAsync(new RelationshipUpsertDto { PersonAId = w.B1, PersonBId = w.B2, Type = RelationshipType.Sibling });
        r.Success.Should().BeFalse();
    }

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Relationship_CreateAsync_AcrossFamilies_Fails(Caller c)
    {
        var w = await SeedAsync();
        var r = await RelSvc(w, w.As(c)).CreateAsync(new RelationshipUpsertDto { PersonAId = w.A1, PersonBId = w.B1, Type = RelationshipType.Sibling });
        r.Success.Should().BeFalse();
        r.Message.Should().NotContainAny(BNames);
    }

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Relationship_UpdateAsync_FamilyBRelationship_Fails(Caller c)
    {
        var w = await SeedAsync();
        var r = await RelSvc(w, w.As(c)).UpdateAsync(w.RelB, new RelationshipUpsertDto { PersonAId = w.A1, PersonBId = w.A2, Type = RelationshipType.Sibling });
        r.Success.Should().BeFalse();
        var rel = await Read(w, ctx => ctx.Relationships.SingleAsync(x => x.Id == w.RelB));
        rel.PersonAId.Should().Be(w.B1);
    }

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Relationship_DeleteAsync_FamilyBRelationship_Fails(Caller c)
    {
        var w = await SeedAsync();
        var r = await RelSvc(w, w.As(c)).DeleteAsync(w.RelB);
        r.Success.Should().BeFalse();
        (await Read(w, ctx => ctx.Relationships.AnyAsync(x => x.Id == w.RelB))).Should().BeTrue();
    }

    // ── MediaAccessService (S8: the /media endpoint) ────────────────────────────

    private static MediaAccessService MediaSvc(World w, ICurrentUserService u) =>
        new(w.Db, new FakeBlobStorageService(), NullLogger<MediaAccessService>.Instance, u);

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Media_OpenAsync_FamilyBFile_Fails(Caller c)
    {
        var w = await SeedAsync();
        (await MediaSvc(w, w.As(c)).OpenAsync("b1.jpg")).Success.Should().BeFalse();
    }

    [Fact]
    public async Task Media_OpenAsync_PositiveControls()
    {
        var w = await SeedAsync();
        (await MediaSvc(w, w.MemberB).OpenAsync("b1.jpg")).Success.Should().BeTrue();
        (await MediaSvc(w, w.SuperUser).OpenAsync("b1.jpg")).Success.Should().BeTrue();
    }

    // ── MediumService ───────────────────────────────────────────────────────────

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Medium_GetAllAsync_ReturnsNoFamilyBMedia(Caller c)
    {
        var w = await SeedAsync();
        var r = await MediumSvc(w, w.As(c)).GetAllAsync();
        r.Success.Should().BeTrue();
        r.Data!.Select(m => m.Id).Should().NotContain(w.MediumB);
    }

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Medium_GetByPersonIdAsync_FamilyBPerson_ReturnsNothing(Caller c)
    {
        var w = await SeedAsync();
        var r = await MediumSvc(w, w.As(c)).GetByPersonIdAsync(w.B1);
        (r.Data ?? []).Should().BeEmpty();
    }

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Medium_GetByIdAsync_FamilyBMedium_Fails(Caller c)
    {
        var w = await SeedAsync();
        var r = await MediumSvc(w, w.As(c)).GetByIdAsync(w.MediumB);
        r.Success.Should().BeFalse();
    }

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Medium_CreateAsync_OnFamilyBPerson_FailsAndUploadsNothing(Caller c)
    {
        var w = await SeedAsync();
        var blob = new FakeBlobStorageService();
        using var stream = new MemoryStream([1, 2, 3]);
        var r = await MediumSvc(w, w.As(c), blob).CreateAsync(
            new MediumUpsertDto { PersonId = w.B1, FileName = "x.jpg", MimeType = "image/jpeg", Type = "photo" }, stream);
        r.Success.Should().BeFalse();
        blob.LastUploadedBytes.Should().BeNull();
        (await Read(w, ctx => ctx.Media.CountAsync(m => m.PersonId == w.B1))).Should().Be(1);
    }

    [Fact]
    public async Task Medium_CreateAsync_SameMessageForForeignAndMissingPerson()
    {
        var w = await SeedAsync();
        var svc = MediumSvc(w, w.As(Caller.MemberA));
        using var s1 = new MemoryStream([1]);
        using var s2 = new MemoryStream([1]);
        var foreign = await svc.CreateAsync(new MediumUpsertDto { PersonId = w.B1, FileName = "x.jpg", MimeType = "image/jpeg" }, s1);
        var missingId = Guid.NewGuid();
        var missing = await svc.CreateAsync(new MediumUpsertDto { PersonId = missingId, FileName = "x.jpg", MimeType = "image/jpeg" }, s2);
        foreign.Success.Should().BeFalse();
        missing.Success.Should().BeFalse();
        Normalize(foreign.Message, w.B1).Should().Be(Normalize(missing.Message, missingId));
    }

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Medium_UpdateAsync_FamilyBMedium_Fails(Caller c)
    {
        var w = await SeedAsync();
        var r = await MediumSvc(w, w.As(c)).UpdateAsync(w.MediumB, new MediumUpsertDto { PersonId = w.B1, FileName = "b1.jpg", Caption = "pwned" });
        r.Success.Should().BeFalse();
        (await Read(w, ctx => ctx.Media.SingleAsync(m => m.Id == w.MediumB))).Caption.Should().BeNull();
    }

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Medium_DeleteAsync_FamilyBMedium_Fails(Caller c)
    {
        var w = await SeedAsync();
        var r = await MediumSvc(w, w.As(c)).DeleteAsync(w.MediumB);
        r.Success.Should().BeFalse();
        (await Read(w, ctx => ctx.Media.AnyAsync(m => m.Id == w.MediumB))).Should().BeTrue();
    }

    // ── StoryService ────────────────────────────────────────────────────────────

    private Guid[] AllBStories(World w) => [w.StoryBLinked, w.StoryBUnlinked, w.StoryBPending, w.StoryBHidden];

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Story_GetByPersonAsync_FamilyBPerson_ReturnsNothing(Caller c)
    {
        var w = await SeedAsync();
        var r = await StorySvc(w, w.As(c)).GetByPersonAsync(w.B1);
        (r.Data ?? []).Should().BeEmpty();
    }

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Story_GetAllApprovedAsync_ReturnsNoFamilyBStories(Caller c)
    {
        var w = await SeedAsync();
        var r = await StorySvc(w, w.As(c)).GetAllApprovedAsync();
        r.Success.Should().BeTrue();
        r.Data!.Select(s => s.Id).Should().NotContain(AllBStories(w));
    }

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Story_GetAllAsync_ReturnsNoFamilyBStories(Caller c)
    {
        var w = await SeedAsync();
        var r = await StorySvc(w, w.As(c)).GetAllAsync();
        (r.Data ?? []).Select(s => s.Id).Should().NotContain(AllBStories(w));
    }

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Story_GetUnlinkedAsync_ReturnsNoFamilyBStories(Caller c)
    {
        var w = await SeedAsync();
        var r = await StorySvc(w, w.As(c)).GetUnlinkedAsync();
        (r.Data ?? []).Select(s => s.Id).Should().NotContain(w.StoryBUnlinked);
    }

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Story_CreateAsync_AboutFamilyBPerson_Fails(Caller c)
    {
        var w = await SeedAsync();
        var r = await StorySvc(w, w.As(c)).CreateAsync(new StoryUpsertDto { PersonId = w.B1, Title = "t", Body = "b" });
        r.Success.Should().BeFalse();
        (await Read(w, ctx => ctx.Stories.CountAsync(s => s.PersonId == w.B1))).Should().Be(3);
    }

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Story_UpdateAsync_FamilyBStories_Fail(Caller c)
    {
        var w = await SeedAsync();
        var svc = StorySvc(w, w.As(c));
        foreach (var id in new[] { w.StoryBLinked, w.StoryBUnlinked })
        {
            var r = await svc.UpdateAsync(id, new StoryUpsertDto { Title = "pwned", Body = "pwned" });
            r.Success.Should().BeFalse($"story {id} belongs to family B");
        }
        (await Read(w, ctx => ctx.Stories.AnyAsync(s => s.Title == "pwned"))).Should().BeFalse();
    }

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Story_DeleteAsync_FamilyBStories_Fail(Caller c)
    {
        var w = await SeedAsync();
        var svc = StorySvc(w, w.As(c));
        foreach (var id in new[] { w.StoryBLinked, w.StoryBUnlinked })
            (await svc.DeleteAsync(id)).Success.Should().BeFalse($"story {id} belongs to family B");
        (await Read(w, ctx => ctx.Stories.CountAsync(s => s.Id == w.StoryBLinked || s.Id == w.StoryBUnlinked))).Should().Be(2);
    }

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Story_SetHiddenAsync_FamilyBStory_Fails(Caller c)
    {
        var w = await SeedAsync();
        var r = await StorySvc(w, w.As(c)).SetHiddenAsync(w.StoryBLinked, true);
        r.Success.Should().BeFalse();
        (await Read(w, ctx => ctx.Stories.SingleAsync(s => s.Id == w.StoryBLinked))).IsHidden.Should().BeFalse();
    }

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Story_ApproveAsync_FamilyBStory_Fails(Caller c)
    {
        var w = await SeedAsync();
        var r = await StorySvc(w, w.As(c)).ApproveAsync(w.StoryBPending);
        r.Success.Should().BeFalse();
        (await Read(w, ctx => ctx.Stories.SingleAsync(s => s.Id == w.StoryBPending))).IsApproved.Should().BeFalse();
    }

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Story_MoveAsync_DoesNotTouchFamilyBOrdering(Caller c)
    {
        var w = await SeedAsync();
        var before = await Read(w, ctx => ctx.Stories.Where(s => s.Id != w.StoryAUnlinked).ToDictionaryAsync(s => s.Id, s => s.SortOrder));
        // A's only story sits at SortOrder 0 next to B's stories in the global list.
        await StorySvc(w, w.As(c)).MoveAsync(w.StoryAUnlinked, +1);
        await StorySvc(w, w.As(c)).MoveAsync(w.StoryBLinked, -1);
        var after = await Read(w, ctx => ctx.Stories.Where(s => s.Id != w.StoryAUnlinked).ToDictionaryAsync(s => s.Id, s => s.SortOrder));
        after.Should().BeEquivalentTo(before);
    }

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Story_LinkToPersonAsync_AcrossFamilies_Fails(Caller c)
    {
        var w = await SeedAsync();
        var svc = StorySvc(w, w.As(c));
        (await svc.LinkToPersonAsync(w.StoryBUnlinked, w.A1)).Success.Should().BeFalse("B's story must not move into A");
        (await svc.LinkToPersonAsync(w.StoryAUnlinked, w.B1)).Success.Should().BeFalse("A's story must not attach to B's person");
        var stories = await Read(w, ctx => ctx.Stories.Where(s => s.Id == w.StoryBUnlinked || s.Id == w.StoryAUnlinked).ToListAsync());
        stories.Should().OnlyContain(s => s.PersonId == null);
    }

    // Same-family story rules from the same review (2026-10-10), not cross-tenant but
    // found alongside: the person drawer bypassed moderation, and any member could edit
    // or delete anyone's unlinked story.

    [Fact]
    [Trait("Category", "Regression")]
    public async Task Story_GetByPersonAsync_OwnFamily_HidesPendingAndHiddenStories()
    {
        var w = await SeedAsync();
        var ids = (await StorySvc(w, w.MemberB).GetByPersonAsync(w.B1)).Data!.Select(s => s.Id).ToList();
        ids.Should().Contain(w.StoryBLinked);
        ids.Should().NotContain([w.StoryBPending, w.StoryBHidden]);
    }

    [Fact]
    [Trait("Category", "Regression")]
    public async Task Story_UpdateAndDelete_OnlyAuthorOrAdminInSameFamily()
    {
        var w = await SeedAsync();
        var otherMemberOfB = new FakeCurrentUserService { UserId = Guid.NewGuid(), FamilyId = w.FamilyB };
        var svc = StorySvc(w, otherMemberOfB);
        (await svc.UpdateAsync(w.StoryBUnlinked, new StoryUpsertDto { Title = "pwned", Body = "pwned" })).Success.Should().BeFalse();
        (await svc.DeleteAsync(w.StoryBUnlinked)).Success.Should().BeFalse();

        (await StorySvc(w, w.MemberB).UpdateAsync(w.StoryBUnlinked, new StoryUpsertDto { Title = "mine", Body = "mine" }))
            .Success.Should().BeTrue("UserB wrote this story");
    }

    [Fact]
    public async Task PositiveControl_PostSignupLinking_AnonymousCircuitLinksInviteStoryWithinItsFamily()
    {
        // The "Willa scenario": LinkToTreeModal runs before the new user is signed in and
        // links a legacy unlinked invite-response story to the subject it just created.
        var w = await SeedAsync();
        var storyId = Guid.NewGuid();
        await using (var ctx = w.Db.CreateDbContext())
        {
            ctx.Stories.Add(new Story
            {
                Id = storyId, PersonId = null, AuthorId = null, InviteId = w.StoryInviteB,
                UnlinkedPersonName = "Bill", Title = "A memory", Body = "...", CreatedAt = DateTime.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        var anon = StorySvc(w, World.Anonymous);
        (await anon.LinkToPersonAsync(storyId, w.A1)).Success.Should().BeFalse("the invite came from family B");
        (await anon.LinkToPersonAsync(storyId, w.B2)).Success.Should().BeTrue();
        (await anon.LinkToPersonAsync(w.StoryBLinked, w.B2)).Success.Should().BeFalse("already-linked stories can't be relinked anonymously");
    }

    // ── StoryInviteService ──────────────────────────────────────────────────────

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task StoryInvite_CreateInviteAsync_AboutFamilyBPerson_FailsAndSendsNothing(Caller c)
    {
        var w = await SeedAsync();
        var email = new RecordingEmailSender();
        var r = await StoryInviteSvc(w, w.As(c), email).CreateInviteAsync(
            new StoryInviteCreateDto { PersonId = w.B1, InvitedEmail = "attacker@evil.test" }, "https://arborkin.test");
        r.Success.Should().BeFalse();
        email.Sent.Should().BeEmpty();
        (await Read(w, ctx => ctx.StoryInvites.CountAsync())).Should().Be(1);
    }

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task StoryInvite_GetAllAsync_ReturnsNoFamilyBInvites(Caller c)
    {
        var w = await SeedAsync();
        var r = await StoryInviteSvc(w, w.As(c)).GetAllAsync();
        (r.Data ?? []).Select(i => i.Id).Should().NotContain(w.StoryInviteB);
    }

    [Fact]
    [Trait("Category", "Regression")]
    public async Task StoryInvite_SubmitResponse_OffersRegistrationIntoTheStorysFamily()
    {
        // Finding S5: the respondent's registration invite always targeted the OLDEST
        // family, so answering a family-B story invite onboarded the person into family A.
        var w = await SeedAsync();
        var r = await StoryInviteSvc(w, World.Anonymous).SubmitResponseAsync(
            new StoryInviteResponseDto { Token = w.StoryInviteBToken, Body = "A memory", AuthorName = "Cousin" });
        r.Success.Should().BeTrue();
        var invite = await Read(w, ctx => ctx.UserInvites.SingleAsync(i => i.Email == "responder@b.test"));
        invite.FamilyId.Should().Be(w.FamilyB);
    }

    // ── GedcomExportService ─────────────────────────────────────────────────────

    [Theory, InlineData(Caller.MemberA), InlineData(Caller.AdminA)]
    public async Task Gedcom_ExportAsync_ContainsNoFamilyBPeople(Caller c)
    {
        var w = await SeedAsync();
        var ged = await GedcomSvc(w, w.As(c)).ExportAsync();
        ged.Should().Contain("Alice");
        ged.Should().NotContainAny(BNames);
    }

    // ── AuthService (tree linking) ──────────────────────────────────────────────

    [Fact]
    public async Task Auth_LinkUserToTreeAsync_ClaimFamilyBPerson_FailsWithoutNamingThem()
    {
        var w = await SeedAsync();
        var r = await AuthSvc(w, w.As(Caller.MemberA)).LinkUserToTreeAsync(w.UserA, w.B1, "Alice", "Anders");
        r.Success.Should().BeFalse();
        r.Message.Should().NotContainAny(BNames);
        (await Read(w, ctx => ctx.Users.SingleAsync(u => u.Id == w.UserA))).PersonId.Should().Be(w.A1);
    }

    [Fact]
    public async Task Auth_LinkUserToTreeAsync_ConnectToFamilyBPerson_CreatesNothingInFamilyB()
    {
        var w = await SeedAsync();
        var r = await AuthSvc(w, w.As(Caller.MemberA)).LinkUserToTreeAsync(w.UserA, null, "Intruder", "Node", w.B1, "ChildOfConnected");
        r.Success.Should().BeFalse();
        (await Read(w, ctx => ctx.People.AnyAsync(p => p.FirstName == "Intruder"))).Should().BeFalse();
    }

    [Fact]
    public async Task Auth_CreateUnlinkedPersonAsync_ConnectToFamilyBPerson_CreatesNothing()
    {
        var w = await SeedAsync();
        var r = await AuthSvc(w, w.As(Caller.MemberA)).CreateUnlinkedPersonAsync("Intruder", "Node", w.B1, "Sibling", w.UserA);
        r.Success.Should().BeFalse();
        (await Read(w, ctx => ctx.People.AnyAsync(p => p.FirstName == "Intruder"))).Should().BeFalse();
    }

    [Fact]
    public async Task Auth_RegisterAsync_OpenModeWithInvite_JoinsTheInvitesFamily()
    {
        // Finding S5: in Open mode RegisterAsync ignored a supplied invite, so a family-B
        // invitee landed in the oldest family (A) and the invite stayed reusable.
        var w = await SeedAsync();
        var r = await AuthSvc(w, World.Anonymous, mode: "Open")
            .RegisterAsync("New", "Bie", "newbie@b.test", "LongEnough123!", w.UserInviteB);
        r.Success.Should().BeTrue(r.Message);
        (await Read(w, ctx => ctx.UserFamilies.SingleAsync(uf => uf.UserId == r.Data))).FamilyId.Should().Be(w.FamilyB);
        (await Read(w, ctx => ctx.UserInvites.SingleAsync(i => i.Id == w.UserInviteB))).AcceptedAt.Should().NotBeNull();
    }

    // ── Admin surfaces (S2: SuperUser-only) ─────────────────────────────────────
    // Decision 2026-10-10: the Users tab, invite/reset management and the audit log are
    // SuperUser-only. A family Admin gets nothing, even for their own family.

    [Fact]
    public async Task AdminUsers_AllMethods_DenyFamilyAdmin()
    {
        var w = await SeedAsync();
        var svc = AdminUserSvc(w, w.As(Caller.AdminA));
        (await svc.GetAllUsersAsync()).Data.Should().BeNullOrEmpty();
        (await svc.GetPendingInvitesAsync()).Data.Should().BeNullOrEmpty();
        (await svc.GetPendingPasswordResetsAsync()).Data.Should().BeNullOrEmpty();
        (await svc.GetPeopleAsync()).Data.Should().BeNullOrEmpty();
        (await svc.CancelInviteAsync(w.UserInviteB)).Success.Should().BeFalse();
        (await svc.DismissPasswordResetAsync(w.ResetRequestB)).Success.Should().BeFalse();
        (await svc.SetAdminAsync(w.UserB, true)).Success.Should().BeFalse();
        (await svc.SetAdminAsync(w.UserA, true)).Success.Should().BeFalse();
        (await Read(w, ctx => ctx.Users.AnyAsync(u => (u.Id == w.UserA || u.Id == w.UserB) && u.IsAdmin))).Should().BeFalse();
        (await Read(w, ctx => ctx.UserInvites.AnyAsync(i => i.Id == w.UserInviteB))).Should().BeTrue();
        (await Read(w, ctx => ctx.PasswordResetRequests.AnyAsync(r => r.Id == w.ResetRequestB))).Should().BeTrue();
    }

    [Fact]
    public async Task AdminActivity_DeniesFamilyAdmin()
    {
        // The Activity tab reads the same unscoped audit rows as the Audit log tab.
        var w = await SeedAsync();
        var svc = new AdminActivityService(w.Db, NullLogger<AdminActivityService>.Instance, w.As(Caller.AdminA));
        (await svc.GetActivityAuditLogsAsync(w.UserB, DateOnly.FromDateTime(DateTime.UtcNow))).Data.Should().BeNullOrEmpty();
        (await svc.GetRecentActivityAsync()).Success.Should().BeFalse();

        var su = new AdminActivityService(w.Db, NullLogger<AdminActivityService>.Instance, w.SuperUser);
        (await su.GetActivityAuditLogsAsync(w.UserB, DateOnly.FromDateTime(DateTime.UtcNow))).Data!
            .Select(a => a.Id).Should().Contain(w.AuditB);
    }

    [Fact]
    public async Task AdminAudit_DeniesFamilyAdmin()
    {
        var w = await SeedAsync();
        (await AdminAuditSvc(w, w.As(Caller.AdminA)).GetAllAuditLogsAsync()).Data.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task Auth_AdminInviteAndResetMethods_DenyFamilyAdmin()
    {
        var w = await SeedAsync();
        var svc = AuthSvc(w, w.As(Caller.AdminA));
        (await svc.CreateInviteAsync("someone@x.test")).Success.Should().BeFalse();
        (await svc.GetPendingInvitesAsync()).Data.Should().BeNullOrEmpty();
        (await svc.CancelInviteAsync(w.UserInviteB)).Success.Should().BeFalse();
        (await svc.GetPendingResetRequestsAsync()).Data.Should().BeNullOrEmpty();
        (await svc.DismissResetRequestAsync(w.ResetRequestB)).Success.Should().BeFalse();
        (await svc.LinkPersonAsync(w.UserB, w.A1)).Success.Should().BeFalse();
        (await Read(w, ctx => ctx.UserInvites.CountAsync())).Should().Be(1);
        (await Read(w, ctx => ctx.UserInvites.SingleAsync())).CancelledAt.Should().BeNull();
        (await Read(w, ctx => ctx.PasswordResetRequests.SingleAsync())).DismissedAt.Should().BeNull();
    }

    [Fact]
    public async Task Auth_SendInviteEmail_DeniesFamilyAdmin_AndSendsNothing()
    {
        var w = await SeedAsync();
        var email = new RecordingEmailSender();
        var r = await AuthSvc(w, w.As(Caller.AdminA), email: email).SendInviteEmailAsync(w.UserInviteB, "https://site.test");
        r.Success.Should().BeFalse();
        email.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Auth_SendInviteEmail_SuperUserEmailsTheInvitedAddressWithTheRegisterLink()
    {
        var w = await SeedAsync();
        var email = new RecordingEmailSender();
        var r = await AuthSvc(w, w.SuperUser, email: email).SendInviteEmailAsync(w.UserInviteB, "https://site.test/");
        r.Success.Should().BeTrue();

        var invite = await Read(w, ctx => ctx.UserInvites.SingleAsync(i => i.Id == w.UserInviteB));
        var sent = email.Sent.Should().ContainSingle().Subject;
        sent.To.Should().Be(invite.Email);
        sent.Body.Should().Contain($"https://site.test/register?invite={w.UserInviteB}");
    }

    [Fact]
    public async Task Auth_SendInviteEmail_RefusesACancelledInvite_AndSendsNothing()
    {
        var w = await SeedAsync();
        var email = new RecordingEmailSender();
        var svc = AuthSvc(w, w.SuperUser, email: email);
        (await svc.CancelInviteAsync(w.UserInviteB)).Success.Should().BeTrue();

        (await svc.SendInviteEmailAsync(w.UserInviteB, "https://site.test")).Success.Should().BeFalse();
        email.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Auth_SendInviteEmail_WhenTheSendFails_SaysToCopyTheLink_AndKeepsTheInvite()
    {
        var w = await SeedAsync();
        var r = await AuthSvc(w, w.SuperUser, email: new ThrowingEmailSender()).SendInviteEmailAsync(w.UserInviteB, "https://site.test");

        r.Success.Should().BeFalse();
        r.Message.Should().Contain("copy the link");
        (await Read(w, ctx => ctx.UserInvites.SingleAsync(i => i.Id == w.UserInviteB))).CancelledAt.Should().BeNull();
    }

    private sealed class ThrowingEmailSender : IEmailSender
    {
        public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
            => throw new InvalidOperationException("SMTP down");
    }

    // ── Anonymous caller (S1) ───────────────────────────────────────────────────

    [Fact]
    public async Task Anonymous_CannotCreatePeopleStoriesOrMedia()
    {
        var w = await SeedAsync();
        var anon = World.Anonymous;
        (await PersonSvc(w, anon).CreateAsync(Dto("Anon", "Person"))).Success.Should().BeFalse();
        (await StorySvc(w, anon).CreateAsync(new StoryUpsertDto { UnlinkedPersonName = "Anon", Title = "spam", Body = "spam" })).Success.Should().BeFalse();
        using var stream = new MemoryStream([1, 2, 3]);
        (await MediumSvc(w, anon).CreateAsync(new MediumUpsertDto { PersonId = w.A1, FileName = "x.jpg", MimeType = "image/jpeg" }, stream)).Success.Should().BeFalse();
        (await Read(w, ctx => ctx.People.AnyAsync(p => p.FirstName == "Anon"))).Should().BeFalse();
        (await Read(w, ctx => ctx.Stories.AnyAsync(s => s.Title == "spam"))).Should().BeFalse();
    }

    [Fact]
    public async Task Anonymous_ReadsReturnNothing()
    {
        var w = await SeedAsync();
        var anon = World.Anonymous;
        (await PersonSvc(w, anon).GetAllAsync()).Data.Should().BeNullOrEmpty();
        (await StorySvc(w, anon).GetAllApprovedAsync()).Data.Should().BeNullOrEmpty();
        (await StorySvc(w, anon).GetByPersonAsync(w.A1)).Data.Should().BeNullOrEmpty();
        (await MediumSvc(w, anon).GetAllAsync()).Data.Should().BeNullOrEmpty();
        (await MediumSvc(w, anon).GetByPersonIdAsync(w.B1)).Data.Should().BeNullOrEmpty();
    }

    // ── Positive controls: family B (and the super-user) CAN reach the seeded data ──

    [Fact]
    public async Task PositiveControl_FamilyBMember_SeesOwnData()
    {
        var w = await SeedAsync();
        var u = w.MemberB;
        (await PersonSvc(w, u).GetByIdAsync(w.B1)).Success.Should().BeTrue();
        (await PersonSvc(w, u).GetAllAsync()).Data!.Select(p => p.Id).Should().Contain([w.B1, w.B2]);
        (await RelSvc(w, u).GetForPersonAsync(w.B1)).Data!.Select(r => r.Id).Should().Contain(w.RelB);
        (await MediumSvc(w, u).GetByPersonIdAsync(w.B1)).Data!.Select(m => m.Id).Should().Contain(w.MediumB);
        (await MediumSvc(w, u).GetByIdAsync(w.MediumB)).Success.Should().BeTrue();
        (await StorySvc(w, u).GetAllApprovedAsync()).Data!.Select(s => s.Id).Should().Contain([w.StoryBLinked, w.StoryBUnlinked]);
        (await StorySvc(w, u).GetByPersonAsync(w.B1)).Data!.Select(s => s.Id).Should().Contain(w.StoryBLinked);
        (await GedcomSvc(w, u).ExportAsync()).Should().Contain("Beatrix");
    }

    [Fact]
    public async Task PositiveControl_FamilyBAdmin_CanManageOwnData()
    {
        var w = await SeedAsync();
        var u = w.AdminBUser;
        // First: the Person update/delete below sync B2's relationship lists, which
        // removes RelB as a side effect.
        (await RelSvc(w, u).DeleteAsync(w.RelB)).Success.Should().BeTrue();
        (await StorySvc(w, u).GetAllAsync()).Data!.Select(s => s.Id).Should().Contain(AllBStories(w));
        (await StorySvc(w, u).GetUnlinkedAsync()).Data!.Select(s => s.Id).Should().Contain(w.StoryBUnlinked);
        (await StoryInviteSvc(w, u).GetAllAsync()).Data!.Select(i => i.Id).Should().Contain(w.StoryInviteB);
        (await StorySvc(w, u).ApproveAsync(w.StoryBPending)).Success.Should().BeTrue();
        (await StorySvc(w, u).SetHiddenAsync(w.StoryBLinked, true)).Success.Should().BeTrue();
        (await StorySvc(w, u).UpdateAsync(w.StoryBUnlinked, new StoryUpsertDto { Title = "edited", Body = "edited" })).Success.Should().BeTrue();
        (await PersonSvc(w, u).UpdateAsync(w.B2, Dto("Bruno", "Brennan-Edited"))).Success.Should().BeTrue();
        (await PersonSvc(w, u).RestoreAsync(w.BDeleted)).Success.Should().BeTrue();
        (await PersonSvc(w, u).SetProfilePhotoAsync(w.B1, w.MediumB)).Success.Should().BeTrue();
        (await MediumSvc(w, u).DeleteAsync(w.MediumB)).Success.Should().BeTrue();
        (await PersonSvc(w, u).DeleteAsync(w.B2)).Success.Should().BeTrue();
    }

    [Fact]
    public async Task PositiveControl_SuperUser_ReachesAdminSurfaces()
    {
        var w = await SeedAsync();
        var u = w.SuperUser;
        (await AdminUserSvc(w, u).GetAllUsersAsync()).Data!.Should().HaveCount(4);
        (await AdminUserSvc(w, u).GetPendingInvitesAsync()).Data!.Select(i => i.Id).Should().Contain(w.UserInviteB);
        (await AdminUserSvc(w, u).GetPendingPasswordResetsAsync()).Data!.Select(r => r.Id).Should().Contain(w.ResetRequestB);
        (await AdminUserSvc(w, u).SetAdminAsync(w.UserB, true)).Success.Should().BeTrue();
        (await AdminAuditSvc(w, u).GetAllAuditLogsAsync()).Data!.Select(a => a.Id).Should().Contain(w.AuditB);
        (await AuthSvc(w, u).GetPendingInvitesAsync()).Data!.Select(i => i.Id).Should().Contain(w.UserInviteB);
        (await AuthSvc(w, u).CreateInviteAsync("fresh@x.test")).Success.Should().BeTrue();
    }

    // Masks any echoed GUID so "Person {id} not found." compares equal across IDs.
    private static string Normalize(string? message, Guid? _) =>
        System.Text.RegularExpressions.Regex.Replace(message ?? "", "[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}", "{id}");
}
