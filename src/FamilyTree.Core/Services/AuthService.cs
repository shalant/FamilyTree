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
}
