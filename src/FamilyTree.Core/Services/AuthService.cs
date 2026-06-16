using FamilyTree.Core.Data;
using FamilyTree.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FamilyTree.Core.Services;

public class AuthService(
    UserManager<AppUser> userManager,
    IConfiguration config,
    IDbContextFactory<AppDbContext> dbFactory,
    IEmailSender emailSender) : IAuthService
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

    public async Task SaveFocusPersonAsync(Guid userId, Guid? personId)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return;
        user.FocusPersonId = personId;
        await userManager.UpdateAsync(user);
    }

    public async Task<Guid?> GetFocusPersonIdAsync(Guid userId)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user?.FocusPersonId;
    }

    public async Task<AuthResult> RequestPasswordResetAsync(string email, string? baseUrl = null)
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

            var requestId = Guid.NewGuid();
            ctx.PasswordResetRequests.Add(new PasswordResetRequest
            {
                Id        = requestId,
                Email     = email.Trim().ToLower(),
                Token     = token,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(24),
            });
            await ctx.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                var link = $"{baseUrl.TrimEnd('/')}/reset-password?id={requestId}";
                _ = emailSender.SendAsync(
                    email.Trim(),
                    "Reset your ArborKin password",
                    $"""
                    <!DOCTYPE html>
                    <html lang="en">
                    <head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
                    <body style="margin:0;padding:0;background:#f0ede8;font-family:Georgia,'Times New Roman',serif;">
                      <table width="100%" cellpadding="0" cellspacing="0" style="background:#f0ede8;padding:40px 16px;">
                        <tr><td align="center">
                          <table width="100%" style="max-width:520px;" cellpadding="0" cellspacing="0">

                            <!-- Header -->
                            <tr>
                              <td style="background:#085041;border-radius:12px 12px 0 0;padding:32px 40px 28px;text-align:center;">
                                <div style="font-family:Georgia,serif;font-size:26px;font-weight:700;color:#ffffff;letter-spacing:0.5px;">
                                  ArborKin
                                </div>
                                <div style="font-size:12px;color:#9FE1CB;letter-spacing:2px;text-transform:uppercase;margin-top:4px;">
                                  Family Tree
                                </div>
                              </td>
                            </tr>

                            <!-- Body -->
                            <tr>
                              <td style="background:#ffffff;padding:40px 40px 32px;">

                                <!-- Lock icon -->
                                <div style="text-align:center;margin-bottom:24px;">
                                  <div style="display:inline-block;background:#E1F5EE;border-radius:50%;width:64px;height:64px;line-height:64px;font-size:28px;text-align:center;">
                                    🔒
                                  </div>
                                </div>

                                <h1 style="margin:0 0 8px;font-family:Georgia,serif;font-size:22px;font-weight:700;color:#1a1a18;text-align:center;">
                                  Password reset request
                                </h1>
                                <p style="margin:0 0 24px;font-size:14px;color:#666662;text-align:center;line-height:1.6;">
                                  We received a request to reset the password on your ArborKin account.
                                  This link expires in <strong style="color:#085041;">24 hours</strong>.
                                </p>

                                <!-- CTA button -->
                                <div style="text-align:center;margin:32px 0;">
                                  <a href="{link}"
                                     style="display:inline-block;background:#085041;color:#ffffff;text-decoration:none;
                                            font-family:Georgia,serif;font-size:15px;font-weight:600;
                                            padding:14px 36px;border-radius:8px;letter-spacing:0.3px;">
                                    Reset my password →
                                  </a>
                                </div>

                                <!-- Link fallback -->
                                <p style="margin:24px 0 0;font-size:12px;color:#999994;text-align:center;line-height:1.6;">
                                  Button not working? Copy and paste this link into your browser:<br>
                                  <a href="{link}" style="color:#0F6E56;word-break:break-all;">{link}</a>
                                </p>
                              </td>
                            </tr>

                            <!-- Security note -->
                            <tr>
                              <td style="background:#f7f5f0;border:1px solid #e8e4dc;border-top:none;padding:20px 40px;">
                                <p style="margin:0;font-size:12px;color:#888884;line-height:1.6;text-align:center;">
                                  🛡️ If you didn't request a password reset, you can safely ignore this email.
                                  Your password will <strong>not</strong> change unless you click the link above.
                                </p>
                              </td>
                            </tr>

                            <!-- Footer -->
                            <tr>
                              <td style="background:#085041;border-radius:0 0 12px 12px;padding:20px 40px;text-align:center;">
                                <p style="margin:0;font-size:11px;color:#9FE1CB;line-height:1.6;">
                                  ArborKin · Family history, beautifully kept
                                </p>
                              </td>
                            </tr>

                          </table>
                        </td></tr>
                      </table>
                    </body>
                    </html>
                    """);
            }
        }
        return new AuthResult(true);
    }

    public async Task<bool> IsResetRequestValidAsync(Guid requestId)
    {
        await using var ctx = await dbFactory.CreateDbContextAsync();
        return await ctx.PasswordResetRequests.AnyAsync(r =>
            r.Id == requestId
            && r.CompletedAt == null
            && r.DismissedAt == null
            && r.ExpiresAt > DateTime.UtcNow);
    }

    public async Task<AuthResult> ResetPasswordAsync(Guid requestId, string newPassword)
    {
        await using var ctx = await dbFactory.CreateDbContextAsync();
        var req = await ctx.PasswordResetRequests.FirstOrDefaultAsync(r =>
            r.Id == requestId
            && r.CompletedAt == null
            && r.DismissedAt == null
            && r.ExpiresAt > DateTime.UtcNow);

        if (req == null)
            return new AuthResult(false, "This reset link has expired or is invalid. Please request a new one.");

        var user = await userManager.FindByEmailAsync(req.Email);
        if (user == null)
            return new AuthResult(false, "Invalid or expired reset link.");

        var result = await userManager.ResetPasswordAsync(user, req.Token, newPassword);
        if (!result.Succeeded)
            return new AuthResult(false, "This reset link has expired or is invalid. Please request a new one.");

        req.CompletedAt = DateTime.UtcNow;
        await ctx.SaveChangesAsync();

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
