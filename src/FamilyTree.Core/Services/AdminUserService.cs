using FamilyTree.Core.Data;
using FamilyTree.Core.Models;
using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Person;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FamilyTree.Core.Services;

public class AdminUserService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<AdminUserService> logger,
    ICurrentUserService currentUser) : IAdminUserService
{
    // Finding S2 (docs/security.md, 2026-10-10): every method here crosses family
    // boundaries (users, invites, password resets), and a pending reset's Id is a working
    // reset link, so a family Admin who could list them could take over the super-user.
    // SuperUser-only, enforced here and not just by hiding the Users tab.
    internal const string Denied = "Only the site owner can manage user accounts.";

    public async Task<ServiceResponse<List<AdminUserDto>>> GetAllUsersAsync(CancellationToken ct = default)
    {
        if (!currentUser.IsSuperUser) return ServiceResponse<List<AdminUserDto>>.Fail(Denied);

        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);
            var users = await ctx.Users
                .AsNoTracking()
                .OrderBy(u => u.Email)
                .ToListAsync(ct);

            var dtos = users.Select(u => new AdminUserDto
            {
                Id = u.Id,
                Email = u.Email ?? "",
                DisplayName = u.DisplayName ?? "",
                IsSuperUser = u.IsSuperUser,
                IsAdmin = u.IsAdmin,
                PersonId = u.PersonId,
                CreatedAt = u.CreatedAt,
                LastLoginAt = u.LastLoginAt,
            }).ToList();

            return ServiceResponse<List<AdminUserDto>>.Ok(dtos);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading users");
            return ServiceResponse<List<AdminUserDto>>.Fail("Failed to load users.");
        }
    }

    public async Task<ServiceResponse<List<AdminInviteDto>>> GetPendingInvitesAsync(CancellationToken ct = default)
    {
        if (!currentUser.IsSuperUser) return ServiceResponse<List<AdminInviteDto>>.Fail(Denied);

        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);
            var invites = await ctx.UserInvites
                .AsNoTracking()
                .Where(i => i.ExpiresAt > DateTime.UtcNow)
                .OrderByDescending(i => i.CreatedAt)
                .Select(i => new AdminInviteDto
                {
                    Id = i.Id,
                    Email = i.Email,
                    ExpiresAt = i.ExpiresAt,
                })
                .ToListAsync(ct);

            return ServiceResponse<List<AdminInviteDto>>.Ok(invites);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading pending invites");
            return ServiceResponse<List<AdminInviteDto>>.Fail("Failed to load pending invites.");
        }
    }

    public async Task<ServiceResponse<List<AdminPasswordResetDto>>> GetPendingPasswordResetsAsync(CancellationToken ct = default)
    {
        if (!currentUser.IsSuperUser) return ServiceResponse<List<AdminPasswordResetDto>>.Fail(Denied);

        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);
            var resets = await ctx.PasswordResetRequests
                .AsNoTracking()
                .Where(r => r.ExpiresAt > DateTime.UtcNow && r.DismissedAt == null)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new AdminPasswordResetDto
                {
                    Id = r.Id,
                    Email = r.Email,
                    CreatedAt = r.CreatedAt,
                    ExpiresAt = r.ExpiresAt,
                })
                .ToListAsync(ct);

            return ServiceResponse<List<AdminPasswordResetDto>>.Ok(resets);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading pending password resets");
            return ServiceResponse<List<AdminPasswordResetDto>>.Fail("Failed to load password resets.");
        }
    }

    public async Task<ServiceResponse<List<PersonDto>>> GetPeopleAsync(CancellationToken ct = default)
    {
        if (!currentUser.IsSuperUser) return ServiceResponse<List<PersonDto>>.Fail(Denied);

        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);
            var people = await ctx.People
                .AsNoTracking()
                .Where(p => p.DeletedAt == null)
                .OrderBy(p => p.FirstName).ThenBy(p => p.LastName)
                .ToListAsync(ct);

            var dtos = people.Select(p => new PersonDto
            {
                Id = p.Id,
                FirstName = p.FirstName,
                LastName = p.LastName,
                BirthDate = p.BirthDate,
                DeathDate = p.DeathDate,
                Gender = p.Gender,
                BiographyNotes = p.BiographyNotes,
                ProfilePhotoUrl = MediaPaths.ToAppPath(p.ProfilePhotoUrl),
                BirthPlace = p.BirthPlace,
                DeathPlace = p.DeathPlace,
            }).ToList();

            return ServiceResponse<List<PersonDto>>.Ok(dtos);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading people");
            return ServiceResponse<List<PersonDto>>.Fail("Failed to load people.");
        }
    }

    public async Task<ServiceResponse> CancelInviteAsync(Guid inviteId, CancellationToken ct = default)
    {
        if (!currentUser.IsSuperUser) return ServiceResponse.Fail(Denied);

        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);
            var invite = await ctx.UserInvites.FirstOrDefaultAsync(i => i.Id == inviteId, ct);
            if (invite is null)
                return ServiceResponse.Fail("Invite not found.");

            ctx.UserInvites.Remove(invite);
            await ctx.SaveChangesAsync(ct);

            return ServiceResponse.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error canceling invite {InviteId}", inviteId);
            return ServiceResponse.Fail("Failed to cancel invite.");
        }
    }

    // AppUser.IsAdmin is the only source of the Admin role (S7, docs/security.md,
    // 2026-10-25): the Users tab used to write Identity roles, which nothing created or
    // read back, while the claims came from IsAdmin, which no screen showed. Changing it
    // rotates the security stamp, so the user's other sessions end at their next check
    // (cookie and Blazor circuit, every 5 minutes; Program.cs) instead of keeping the old
    // role for up to 14 days.
    public async Task<ServiceResponse> SetAdminAsync(Guid userId, bool isAdmin, CancellationToken ct = default)
    {
        if (!currentUser.IsSuperUser) return ServiceResponse.Fail(Denied);

        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);
            var user = await ctx.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null)
                return ServiceResponse.Fail("User not found.");
            if (user.IsSuperUser)
                return ServiceResponse.Fail("The site owner's role can't be changed.");
            if (user.IsAdmin == isAdmin)
                return ServiceResponse.Ok();

            user.IsAdmin = isAdmin;
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            user.ConcurrencyStamp = Guid.NewGuid().ToString();
            await ctx.SaveChangesAsync(ct);

            logger.LogInformation("User {UserId} Admin set to {IsAdmin}", userId, isAdmin);
            return ServiceResponse.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error setting Admin for user {UserId}", userId);
            return ServiceResponse.Fail("Failed to update role.");
        }
    }

    public async Task<ServiceResponse> DismissPasswordResetAsync(Guid resetId, CancellationToken ct = default)
    {
        if (!currentUser.IsSuperUser) return ServiceResponse.Fail(Denied);

        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);
            var reset = await ctx.PasswordResetRequests.FirstOrDefaultAsync(r => r.Id == resetId, ct);
            if (reset is null)
                return ServiceResponse.Fail("Password reset not found.");

            ctx.PasswordResetRequests.Remove(reset);
            await ctx.SaveChangesAsync(ct);

            return ServiceResponse.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error dismissing password reset {ResetId}", resetId);
            return ServiceResponse.Fail("Failed to dismiss password reset.");
        }
    }
}
