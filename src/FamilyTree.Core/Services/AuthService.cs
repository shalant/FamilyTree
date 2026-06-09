using FamilyTree.Core.Data;
using FamilyTree.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FamilyTree.Core.Services;

public class AuthService(
    UserManager<AppUser> userManager,
    IConfiguration config,
    IDbContextFactory<AppDbContext> dbFactory) : IAuthService
{
    public string GetRegistrationMode() =>
        config["Auth:RegistrationMode"] ?? "Open";

    public async Task<AuthResult> RegisterAsync(
        string firstName, string lastName, string email, string password,
        string? inviteToken = null)
    {
        var mode = GetRegistrationMode();

        if (mode == "Closed")
            return new AuthResult(false, "Registration is currently closed. Contact the administrator.");

        UserInvite? matchedInvite = null;
        if (mode == "InviteOnly")
        {
            if (string.IsNullOrWhiteSpace(inviteToken))
                return new AuthResult(false, "An invitation is required. Ask the administrator for an invite link.");

            await using var ctx = await dbFactory.CreateDbContextAsync();
            matchedInvite = await ctx.UserInvites.FirstOrDefaultAsync(i =>
                i.Token == inviteToken
                && i.Email.ToLower() == email.ToLower()
                && i.AcceptedAt == null
                && i.CancelledAt == null
                && i.ExpiresAt > DateTime.UtcNow);

            if (matchedInvite == null)
                return new AuthResult(false, "This invitation is invalid, expired, or for a different email address.");
        }

        var existing = await userManager.FindByEmailAsync(email);
        if (existing != null)
            return new AuthResult(false, "An account with this email already exists.");

        var user = new AppUser
        {
            UserName    = email,
            Email       = email,
            DisplayName = $"{firstName} {lastName}".Trim(),
            CreatedAt   = DateTime.UtcNow,
        };

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            return new AuthResult(false, result.Errors.First().Description);

        if (matchedInvite != null)
        {
            await using var ctx = await dbFactory.CreateDbContextAsync();
            var invite = await ctx.UserInvites.FindAsync(matchedInvite.Id);
            if (invite != null)
            {
                invite.AcceptedAt = DateTime.UtcNow;
                await ctx.SaveChangesAsync();
            }
        }

        return new AuthResult(true, UserId: user.Id);
    }

    public async Task<AuthResult> LinkPersonAsync(Guid userId, Guid? personId)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return new AuthResult(false, "User not found.");

        user.PersonId = personId;
        var result = await userManager.UpdateAsync(user);
        return result.Succeeded
            ? new AuthResult(true)
            : new AuthResult(false, result.Errors.First().Description);
    }

    public async Task<InviteResult> CreateInviteAsync(string email)
    {
        var ttlDays = config.GetValue("Auth:InviteTtlDays", 7);

        await using var ctx = await dbFactory.CreateDbContextAsync();

        var existing = await ctx.UserInvites.FirstOrDefaultAsync(i =>
            i.Email.ToLower() == email.ToLower()
            && i.AcceptedAt == null
            && i.CancelledAt == null
            && i.ExpiresAt > DateTime.UtcNow);

        if (existing != null)
            return new InviteResult(false,
                "An active invite already exists for this email — use the existing link below.",
                existing.Token);

        var family = await ctx.Families.FirstOrDefaultAsync();
        if (family == null)
        {
            family = new Family { Name = "My Family", CreatedAt = DateTime.UtcNow };
            ctx.Families.Add(family);
            await ctx.SaveChangesAsync();
        }

        var token = Convert.ToBase64String(
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');

        ctx.UserInvites.Add(new UserInvite
        {
            Id          = Guid.NewGuid(),
            Email       = email.Trim().ToLower(),
            FamilyId    = family.Id,
            RoleToGrant = "Member",
            Token       = token,
            ExpiresAt   = DateTime.UtcNow.AddDays(ttlDays),
            CreatedAt   = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync();

        return new InviteResult(true, Token: token);
    }

    public async Task<List<UserInvite>> GetPendingInvitesAsync()
    {
        await using var ctx = await dbFactory.CreateDbContextAsync();
        return await ctx.UserInvites
            .Where(i => i.AcceptedAt == null && i.CancelledAt == null && i.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();
    }

    public async Task<AuthResult> CancelInviteAsync(Guid inviteId)
    {
        await using var ctx = await dbFactory.CreateDbContextAsync();
        var invite = await ctx.UserInvites.FindAsync(inviteId);
        if (invite == null)
            return new AuthResult(false, "Invite not found.");
        invite.CancelledAt = DateTime.UtcNow;
        await ctx.SaveChangesAsync();
        return new AuthResult(true);
    }

    public async Task<UserInvite?> ValidateInviteTokenAsync(string token)
    {
        await using var ctx = await dbFactory.CreateDbContextAsync();
        return await ctx.UserInvites.FirstOrDefaultAsync(i =>
            i.Token == token
            && i.AcceptedAt == null
            && i.CancelledAt == null
            && i.ExpiresAt > DateTime.UtcNow);
    }

    public async Task<AuthResult> RequestPasswordResetAsync(string email)
    {
        var user = await userManager.FindByEmailAsync(email.Trim());
        if (user != null)
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);

            await using var ctx = await dbFactory.CreateDbContextAsync();

            var existing = await ctx.PasswordResetRequests
                .Where(r => r.Email == email.Trim().ToLower()
                         && r.CompletedAt == null && r.DismissedAt == null)
                .ToListAsync();
            foreach (var old in existing)
                old.DismissedAt = DateTime.UtcNow;

            ctx.PasswordResetRequests.Add(new PasswordResetRequest
            {
                Id        = Guid.NewGuid(),
                Email     = email.Trim().ToLower(),
                Token     = token,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(24),
            });
            await ctx.SaveChangesAsync();
        }
        return new AuthResult(true);
    }

    public async Task<AuthResult> ResetPasswordAsync(string email, string token, string newPassword)
    {
        var user = await userManager.FindByEmailAsync(email.Trim());
        if (user == null)
            return new AuthResult(false, "Invalid or expired reset link.");

        var result = await userManager.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded)
            return new AuthResult(false, "This reset link has expired or is invalid. Please request a new one.");

        await using var ctx = await dbFactory.CreateDbContextAsync();
        var req = await ctx.PasswordResetRequests
            .Where(r => r.Email == email.Trim().ToLower() && r.CompletedAt == null)
            .FirstOrDefaultAsync();
        if (req != null)
        {
            req.CompletedAt = DateTime.UtcNow;
            await ctx.SaveChangesAsync();
        }

        return new AuthResult(true);
    }

    public async Task<List<PasswordResetRequest>> GetPendingResetRequestsAsync()
    {
        await using var ctx = await dbFactory.CreateDbContextAsync();
        return await ctx.PasswordResetRequests
            .Where(r => r.CompletedAt == null && r.DismissedAt == null && r.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task DismissResetRequestAsync(Guid id)
    {
        await using var ctx = await dbFactory.CreateDbContextAsync();
        var req = await ctx.PasswordResetRequests.FindAsync(id);
        if (req != null)
        {
            req.DismissedAt = DateTime.UtcNow;
            await ctx.SaveChangesAsync();
        }
    }
}
