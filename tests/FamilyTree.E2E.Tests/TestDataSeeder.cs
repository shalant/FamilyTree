using FamilyTree.Core.Data;
using FamilyTree.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.E2E.Tests;

/// <summary>
/// Direct-DB test data setup, bypassing the UI for the "arrange" half of a scenario so
/// each test only has to drive (and assert on) the part of the flow it actually cares
/// about. Uses the same connection string as the running app-under-test.
/// </summary>
public sealed class TestDataSeeder(string connectionString)
{
    private AppDbContext NewContext() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options);

    public async Task<Guid> CreateFamilyAsync(string name = "Test Family")
    {
        await using var db = NewContext();
        var family = new Family { Id = Guid.NewGuid(), Name = name, CreatedAt = DateTime.UtcNow };
        db.Families.Add(family);
        await db.SaveChangesAsync();
        return family.Id;
    }

    // A minimal Identity row to satisfy a required FK (e.g. StoryInvite.InvitedByUserId)
    // — not meant to ever log in as this user, so no password hash is set.
    public async Task<Guid> CreateUserAsync(string email, string? displayName = null)
    {
        await using var db = NewContext();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            DisplayName = displayName ?? email,
            CreatedAt = DateTime.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    // Mirrors AuthService.CreateInviteAsync's shape directly against the DB, so the
    // golden-path test doesn't need an authenticated admin browser session just to get
    // a valid invite link — the interesting, historically-fragile part of that flow is
    // everything downstream of a valid invite already existing (see docs/TodoList.md's
    // Golden-Path Walkthrough), not the admin-side invite form itself.
    public async Task<Guid> CreateUserInviteAsync(string email, Guid familyId, int ttlDays = 7)
    {
        await using var db = NewContext();
        var invite = new UserInvite
        {
            Id = Guid.NewGuid(),
            Email = email.Trim().ToLowerInvariant(),
            FamilyId = familyId,
            RoleToGrant = "Member",
            Token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
                .Replace("+", "-").Replace("/", "_").TrimEnd('='),
            ExpiresAt = DateTime.UtcNow.AddDays(ttlDays),
            CreatedAt = DateTime.UtcNow,
        };
        db.UserInvites.Add(invite);
        await db.SaveChangesAsync();
        return invite.Id;
    }

    // A real login-capable account, pre-linked to a Person and with a focus already set
    // — bypasses LinkToTreeModal entirely, for tests whose subject is something other
    // than the linking flow itself (that flow gets its own dedicated exercise in the
    // golden-path test). Uses Identity's default PasswordHasher directly (no custom
    // hasher is registered in Program.cs) so a real login POST to /auth/do-login
    // against this account succeeds exactly as it would for a normal user.
    // familyId is required, not optional: PersonService.GetAllAsync() deliberately shows
    // a regular (non-super-user) account NOTHING if their FamilyId claim is missing —
    // "must see NOTHING rather than being accidentally treated the same as a super-user"
    // (PersonService.cs) — and that claim is only emitted from a real UserFamily row
    // (AppUserClaimsPrincipalFactory). A linked user with no UserFamily row would log in
    // successfully but see a permanently empty tree regardless of what People exist.
    public async Task<Guid> CreateLinkedUserAsync(string email, string password, Guid personId, Guid familyId)
    {
        await using var db = NewContext();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            DisplayName = email,
            PersonId = personId,
            FocusPersonId = personId,
            CreatedAt = DateTime.UtcNow,
        };
        user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, password);
        db.Users.Add(user);
        db.UserFamilies.Add(new UserFamily
        {
            UserId = user.Id,
            FamilyId = familyId,
            Role = "Member",
            JoinedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return user.Id;
    }

    // PersonAId = parent, PersonBId = child for RelationshipType.Parent (PersonMapper's
    // convention, documented in CLAUDE.md's "Canonical Relationship Ordering" section).
    public async Task<List<Person>> GetChildrenAsync(Guid parentPersonId)
    {
        await using var db = NewContext();
        var childIds = await db.Relationships
            .Where(r => r.PersonAId == parentPersonId
                && r.Type == FamilyTree.Shared.Enums.RelationshipType.Parent
                && r.DeletedAt == null)
            .Select(r => r.PersonBId)
            .ToListAsync();
        return await db.People.Where(p => childIds.Contains(p.Id)).ToListAsync();
    }

    public async Task<Guid> CreatePersonAsync(string firstName, string lastName, Guid? familyId = null)
    {
        await using var db = NewContext();
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = firstName,
            LastName = lastName,
            FamilyId = familyId,
            CreatedAt = DateTime.UtcNow,
        };
        db.People.Add(person);
        await db.SaveChangesAsync();
        return person.Id;
    }

    public async Task<string> CreateStoryInviteAsync(string invitedEmail, Guid invitedByUserId, string unlinkedPersonName, int ttlDays = 30)
    {
        await using var db = NewContext();
        var token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');
        db.StoryInvites.Add(new StoryInvite
        {
            Id = Guid.NewGuid(),
            Token = token,
            UnlinkedPersonName = unlinkedPersonName,
            InvitedEmail = invitedEmail,
            InvitedByUserId = invitedByUserId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(ttlDays),
        });
        await db.SaveChangesAsync();
        return token;
    }
}
