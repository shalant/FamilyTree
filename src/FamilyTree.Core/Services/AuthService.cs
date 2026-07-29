using FamilyTree.Core.Data;
using FamilyTree.Core.Models;
using FamilyTree.Shared;
using FamilyTree.Shared.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FamilyTree.Core.Services;

public class AuthService(
    UserManager<AppUser> userManager,
    IConfiguration config,
    IDbContextFactory<AppDbContext> dbFactory,
    IEmailSender emailSender,
    ILogger<AuthService> logger) : IAuthService
{
    public string GetRegistrationMode() =>
        config["Auth:RegistrationMode"] ?? "Open";

    public async Task<ServiceResponse<Guid>> RegisterAsync(
        string firstName, string lastName, string email, string password,
        Guid? inviteId = null)
    {
        try
        {
            var mode = GetRegistrationMode();

            if (mode == "Closed")
                return ServiceResponse<Guid>.Fail("Registration is currently closed. Contact the administrator.");

            UserInvite? matchedInvite = null;
            if (mode == "InviteOnly")
            {
                if (!inviteId.HasValue)
                    return ServiceResponse<Guid>.Fail("An invitation is required. Ask the administrator for an invite link.");

                await using var ctx = await dbFactory.CreateDbContextAsync();
                matchedInvite = await ctx.UserInvites.FirstOrDefaultAsync(i =>
                    i.Id == inviteId.Value
                    && i.Email.ToLower() == email.ToLower()
                    && i.AcceptedAt == null
                    && i.CancelledAt == null
                    && i.ExpiresAt > DateTime.UtcNow);

                if (matchedInvite == null)
                    return ServiceResponse<Guid>.Fail("This invitation is invalid, expired, or for a different email address.");
            }

            var existing = await userManager.FindByEmailAsync(email);
            if (existing != null)
                return ServiceResponse<Guid>.Fail("An account with this email already exists.");

            var user = new AppUser
            {
                UserName    = email,
                Email       = email,
                DisplayName = $"{firstName} {lastName}".Trim(),
                CreatedAt   = DateTime.UtcNow,
            };

            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
                return ServiceResponse<Guid>.Fail(result.Errors.First().Description);

            // Every registered user needs a UserFamily row, or every family-scoping check
            // in the app (PersonService.GetAllAsync etc.) treats them as having no FamilyId
            // claim at all — indistinguishable from a broken account, so they'd see nothing.
            await using (var ctx = await dbFactory.CreateDbContextAsync())
            {
                Guid familyId;
                if (matchedInvite != null)
                {
                    var invite = await ctx.UserInvites.FindAsync(matchedInvite.Id);
                    if (invite != null)
                        invite.AcceptedAt = DateTime.UtcNow;
                    familyId = invite?.FamilyId ?? await GetOrCreateDefaultFamilyIdAsync(ctx);
                }
                else
                {
                    familyId = await GetOrCreateDefaultFamilyIdAsync(ctx);
                }

                ctx.UserFamilies.Add(new UserFamily
                {
                    UserId = user.Id,
                    FamilyId = familyId,
                    Role = "Member",
                    JoinedAt = DateTime.UtcNow,
                });

                await ctx.SaveChangesAsync();
            }

            return ServiceResponse<Guid>.Ok(user.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RegisterAsync failed for email {Email}", email);
            return ServiceResponse<Guid>.Fail("Registration failed. Please try again.");
        }
    }

    public async Task<ServiceResponse> EnsureUserFamilyAsync(Guid userId)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync();

            var alreadyAssigned = await ctx.UserFamilies.AnyAsync(uf => uf.UserId == userId);
            if (alreadyAssigned) return ServiceResponse.Ok();

            var familyId = await GetOrCreateDefaultFamilyIdAsync(ctx);
            ctx.UserFamilies.Add(new UserFamily
            {
                UserId = userId,
                FamilyId = familyId,
                Role = "Member",
                JoinedAt = DateTime.UtcNow,
            });
            await ctx.SaveChangesAsync();
            return ServiceResponse.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "EnsureUserFamilyAsync failed for userId {UserId}", userId);
            return ServiceResponse.Fail("Failed to set up family assignment.");
        }
    }

    private static async Task<Guid> GetOrCreateDefaultFamilyIdAsync(AppDbContext ctx)
    {
        // Deterministically the oldest family — with more than one Family row now
        // possible (e.g. a separate test/demo family), an unordered FirstOrDefault
        // would pick an arbitrary one instead of the original/primary family.
        var family = await ctx.Families.OrderBy(f => f.CreatedAt).FirstOrDefaultAsync();
        if (family == null)
        {
            family = new Family { Name = "My Family", CreatedAt = DateTime.UtcNow };
            ctx.Families.Add(family);
            await ctx.SaveChangesAsync();
        }
        return family.Id;
    }

    public async Task<ServiceResponse<bool>> UserExistsAsync(string email)
    {
        try
        {
            var exists = await userManager.FindByEmailAsync(email) != null;
            return ServiceResponse<bool>.Ok(exists);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "UserExistsAsync failed for email {Email}", email);
            return ServiceResponse<bool>.Fail("Failed to check user existence.");
        }
    }

    public async Task<ServiceResponse> LinkPersonAsync(Guid userId, Guid? personId)
    {
        try
        {
            var user = await userManager.FindByIdAsync(userId.ToString());
            if (user is null)
                return ServiceResponse.Fail("User not found.");

            user.PersonId = personId;
            var result = await userManager.UpdateAsync(user);
            return result.Succeeded
                ? ServiceResponse.Ok()
                : ServiceResponse.Fail(result.Errors.First().Description);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "LinkPersonAsync failed for userId {UserId}", userId);
            return ServiceResponse.Fail("Failed to link person.");
        }
    }

    public async Task<ServiceResponse<Guid>> CreateInviteAsync(string email)
    {
        try
        {
            var ttlDays = config.GetValue("Auth:InviteTtlDays", 7);

            await using var ctx = await dbFactory.CreateDbContextAsync();

            var existing = await ctx.UserInvites.FirstOrDefaultAsync(i =>
                i.Email.ToLower() == email.ToLower()
                && i.AcceptedAt == null
                && i.CancelledAt == null
                && i.ExpiresAt > DateTime.UtcNow);

            if (existing != null)
                return ServiceResponse<Guid>.Fail(
                    "An active invite already exists for this email — use the existing link below.");

            var familyId = await GetOrCreateDefaultFamilyIdAsync(ctx);

            var token = Convert.ToBase64String(
                    System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
                .Replace("+", "-").Replace("/", "_").TrimEnd('=');

            var invite = new UserInvite
            {
                Id          = Guid.NewGuid(),
                Email       = email.Trim().ToLower(),
                FamilyId    = familyId,
                RoleToGrant = "Member",
                Token       = token,
                ExpiresAt   = DateTime.UtcNow.AddDays(ttlDays),
                CreatedAt   = DateTime.UtcNow,
            };
            ctx.UserInvites.Add(invite);
            await ctx.SaveChangesAsync();

            return ServiceResponse<Guid>.Ok(invite.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CreateInviteAsync failed for email {Email}", email);
            return ServiceResponse<Guid>.Fail("Failed to create invite.");
        }
    }

    public async Task<ServiceResponse<List<UserInvite>>> GetPendingInvitesAsync()
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync();
            var invites = await ctx.UserInvites
                .Where(i => i.AcceptedAt == null && i.CancelledAt == null && i.ExpiresAt > DateTime.UtcNow)
                .OrderByDescending(i => i.CreatedAt)
                .ToListAsync();
            return ServiceResponse<List<UserInvite>>.Ok(invites);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetPendingInvitesAsync failed");
            return ServiceResponse<List<UserInvite>>.Fail("Failed to retrieve invites.");
        }
    }

    public async Task<ServiceResponse> CancelInviteAsync(Guid inviteId)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync();
            var invite = await ctx.UserInvites.FindAsync(inviteId);
            if (invite == null)
                return ServiceResponse.Fail("Invite not found.");
            invite.CancelledAt = DateTime.UtcNow;
            await ctx.SaveChangesAsync();
            return ServiceResponse.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CancelInviteAsync failed for inviteId {InviteId}", inviteId);
            return ServiceResponse.Fail("Failed to cancel invite.");
        }
    }

    public async Task<ServiceResponse<UserInvite?>> ValidateInviteAsync(Guid inviteId)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync();
            var invite = await ctx.UserInvites.FirstOrDefaultAsync(i =>
                i.Id == inviteId
                && i.AcceptedAt == null
                && i.CancelledAt == null
                && i.ExpiresAt > DateTime.UtcNow);
            return ServiceResponse<UserInvite?>.Ok(invite);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ValidateInviteAsync failed for inviteId {InviteId}", inviteId);
            return ServiceResponse<UserInvite?>.Fail("Failed to validate invite.");
        }
    }

    public async Task<ServiceResponse> SaveFocusPersonAsync(Guid userId, Guid? personId)
    {
        try
        {
            var user = await userManager.FindByIdAsync(userId.ToString());
            if (user is null)
                return ServiceResponse.Fail("User not found.");
            user.FocusPersonId = personId;
            var result = await userManager.UpdateAsync(user);
            return result.Succeeded
                ? ServiceResponse.Ok()
                : ServiceResponse.Fail(result.Errors.First().Description);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SaveFocusPersonAsync failed for userId {UserId}", userId);
            return ServiceResponse.Fail("Failed to save focus person.");
        }
    }

    public async Task<ServiceResponse<Guid?>> GetFocusPersonIdAsync(Guid userId)
    {
        try
        {
            var user = await userManager.FindByIdAsync(userId.ToString());
            return ServiceResponse<Guid?>.Ok(user?.FocusPersonId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetFocusPersonIdAsync failed for userId {UserId}", userId);
            return ServiceResponse<Guid?>.Fail("Failed to retrieve focus person.");
        }
    }

    public async Task<ServiceResponse> RequestPasswordResetAsync(string email, string? baseUrl = null)
    {
        try
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
            return ServiceResponse.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RequestPasswordResetAsync failed for email {Email}", email);
            return ServiceResponse.Fail("Failed to request password reset.");
        }
    }

    public async Task<ServiceResponse<bool>> IsResetRequestValidAsync(Guid requestId)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync();
            var isValid = await ctx.PasswordResetRequests.AnyAsync(r =>
                r.Id == requestId
                && r.CompletedAt == null
                && r.DismissedAt == null
                && r.ExpiresAt > DateTime.UtcNow);
            return ServiceResponse<bool>.Ok(isValid);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "IsResetRequestValidAsync failed for requestId {RequestId}", requestId);
            return ServiceResponse<bool>.Fail("Failed to validate reset request.");
        }
    }

    public async Task<ServiceResponse> ResetPasswordAsync(Guid requestId, string newPassword)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync();
            var req = await ctx.PasswordResetRequests.FirstOrDefaultAsync(r =>
                r.Id == requestId
                && r.CompletedAt == null
                && r.DismissedAt == null
                && r.ExpiresAt > DateTime.UtcNow);

            if (req == null)
                return ServiceResponse.Fail("This reset link has expired or is invalid. Please request a new one.");

            var user = await userManager.FindByEmailAsync(req.Email);
            if (user == null)
                return ServiceResponse.Fail("Invalid or expired reset link.");

            var result = await userManager.ResetPasswordAsync(user, req.Token, newPassword);
            if (!result.Succeeded)
                return ServiceResponse.Fail("This reset link has expired or is invalid. Please request a new one.");

            req.CompletedAt = DateTime.UtcNow;
            await ctx.SaveChangesAsync();

            return ServiceResponse.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ResetPasswordAsync failed for requestId {RequestId}", requestId);
            return ServiceResponse.Fail("Failed to reset password.");
        }
    }

    public async Task<ServiceResponse<List<PasswordResetRequest>>> GetPendingResetRequestsAsync()
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync();
            var requests = await ctx.PasswordResetRequests
                .Where(r => r.CompletedAt == null && r.DismissedAt == null && r.ExpiresAt > DateTime.UtcNow)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();
            return ServiceResponse<List<PasswordResetRequest>>.Ok(requests);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetPendingResetRequestsAsync failed");
            return ServiceResponse<List<PasswordResetRequest>>.Fail("Failed to retrieve reset requests.");
        }
    }

    public async Task<ServiceResponse> DismissResetRequestAsync(Guid id)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync();
            var req = await ctx.PasswordResetRequests.FindAsync(id);
            if (req != null)
            {
                req.DismissedAt = DateTime.UtcNow;
                await ctx.SaveChangesAsync();
            }
            return ServiceResponse.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "DismissResetRequestAsync failed for id {Id}", id);
            return ServiceResponse.Fail("Failed to dismiss reset request.");
        }
    }

    public async Task<ServiceResponse<Guid?>> LinkUserToTreeAsync(
        Guid userId,
        Guid? personId,
        string? firstName,
        string? lastName,
        Guid? connectedPersonId = null,
        string? relationshipType = null)
    {
        try
        {
            var user = await userManager.FindByIdAsync(userId.ToString());
            if (user is null)
                return ServiceResponse<Guid?>.Fail("User not found.");

            await using var ctx = await dbFactory.CreateDbContextAsync();

            if (personId.HasValue)
            {
                var existingPerson = await ctx.People.FindAsync(personId.Value);
                if (existingPerson is null)
                    return ServiceResponse<Guid?>.Fail("Selected person not found.");

                // IX_AspNetUsers_PersonId is a unique index — one Person can only ever be
                // claimed by one account. Check before writing so a taken person surfaces a
                // clear message instead of a raw DbUpdateException bubbling out of
                // userManager.UpdateAsync (found 2026-07-08: the duplicate-detection modal
                // can legitimately offer an already-claimed person as a "is this you?" match
                // when two different accounts share a name).
                var alreadyLinkedToSomeoneElse = await ctx.Users
                    .AnyAsync(u => u.PersonId == personId.Value && u.Id != userId);
                if (alreadyLinkedToSomeoneElse)
                    return ServiceResponse<Guid?>.Fail(
                        $"{existingPerson.FirstName} {existingPerson.LastName} is already linked to another account.");

                user.PersonId = personId.Value;
                var result = await userManager.UpdateAsync(user);
                return result.Succeeded
                    ? ServiceResponse<Guid?>.Ok(personId.Value)
                    : ServiceResponse<Guid?>.Fail(result.Errors.First().Description);
            }

            if (!connectedPersonId.HasValue || string.IsNullOrWhiteSpace(relationshipType))
                return ServiceResponse<Guid?>.Fail("Invalid link parameters.");

            var connectedPerson = await ctx.People.FindAsync(connectedPersonId.Value);
            if (connectedPerson is null)
                return ServiceResponse<Guid?>.Fail("Connected person not found.");

            if (!IsValidRelationshipToken(relationshipType))
                return ServiceResponse<Guid?>.Fail($"Invalid relationship type: {relationshipType}");

            var newPerson = new Person
            {
                Id = Guid.NewGuid(),
                FirstName = firstName?.Trim() ?? "Unknown",
                LastName = lastName?.Trim() ?? "",
                FamilyId = connectedPerson.FamilyId,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userId,
            };

            ctx.People.Add(newPerson);
            await ctx.SaveChangesAsync();

            user.PersonId = newPerson.Id;
            var updateResult = await userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
                return ServiceResponse<Guid?>.Fail(updateResult.Errors.First().Description);

            var (personAId, personBId, finalType) =
                ResolveRelationshipDirection(newPerson.Id, connectedPersonId.Value, relationshipType);

            ctx.Relationships.Add(new Relationship
            {
                Id = Guid.NewGuid(),
                PersonAId = personAId,
                PersonBId = personBId,
                Type = finalType,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userId,
            });
            await ctx.SaveChangesAsync();

            return ServiceResponse<Guid?>.Ok(newPerson.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "LinkUserToTreeAsync failed for userId {UserId}", userId);
            return ServiceResponse<Guid?>.Fail("Failed to link user to tree.");
        }
    }

    public async Task<ServiceResponse<HashSet<Guid>>> GetLinkedPersonIdsAsync(IEnumerable<Guid> personIds)
    {
        try
        {
            var ids = personIds.ToList();
            if (ids.Count == 0) return ServiceResponse<HashSet<Guid>>.Ok([]);

            await using var ctx = await dbFactory.CreateDbContextAsync();
            var linked = await ctx.Users
                .Where(u => u.PersonId != null && ids.Contains(u.PersonId.Value))
                .Select(u => u.PersonId!.Value)
                .ToListAsync();
            return ServiceResponse<HashSet<Guid>>.Ok([.. linked]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetLinkedPersonIdsAsync failed");
            return ServiceResponse<HashSet<Guid>>.Fail("Failed to retrieve linked person IDs.");
        }
    }

    public async Task<ServiceResponse<Guid>> CreateUnlinkedPersonAsync(
        string? firstName,
        string? lastName,
        Guid connectedPersonId,
        string relationshipType,
        Guid createdByUserId)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync();

            var connectedPerson = await ctx.People.FindAsync(connectedPersonId);
            if (connectedPerson is null)
                return ServiceResponse<Guid>.Fail("Connected person not found.");

            if (!IsValidRelationshipToken(relationshipType))
                return ServiceResponse<Guid>.Fail($"Invalid relationship type: {relationshipType}");

            var newPerson = new Person
            {
                Id = Guid.NewGuid(),
                FirstName = firstName?.Trim() ?? "Unknown",
                LastName = lastName?.Trim() ?? "",
                FamilyId = connectedPerson.FamilyId,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdByUserId,
            };

            ctx.People.Add(newPerson);
            await ctx.SaveChangesAsync();

            var (personAId, personBId, finalType) =
                ResolveRelationshipDirection(newPerson.Id, connectedPersonId, relationshipType);

            ctx.Relationships.Add(new Relationship
            {
                Id = Guid.NewGuid(),
                PersonAId = personAId,
                PersonBId = personBId,
                Type = finalType,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdByUserId,
            });
            await ctx.SaveChangesAsync();

            return ServiceResponse<Guid>.Ok(newPerson.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CreateUnlinkedPersonAsync failed");
            return ServiceResponse<Guid>.Fail("Failed to create unlinked person.");
        }
    }

    // "subject" is whichever person was just created (Willa or Bill); "connected" is the
    // existing tree member they're being linked to. Parent direction is explicit in the
    // token because Relationship rows encode PersonAId = parent, PersonBId = child.
    private static bool IsValidRelationshipToken(string token) =>
        token is "ParentOfConnected" or "ChildOfConnected" or "Spouse" or "Sibling";

    private static (Guid PersonAId, Guid PersonBId, RelationshipType Type) ResolveRelationshipDirection(
        Guid subjectId, Guid connectedPersonId, string relationshipType) => relationshipType switch
    {
        "ParentOfConnected" => (subjectId, connectedPersonId, RelationshipType.Parent),
        "ChildOfConnected"  => (connectedPersonId, subjectId, RelationshipType.Parent),
        "Spouse" => subjectId < connectedPersonId
            ? (subjectId, connectedPersonId, RelationshipType.Spouse)
            : (connectedPersonId, subjectId, RelationshipType.Spouse),
        "Sibling" => subjectId < connectedPersonId
            ? (subjectId, connectedPersonId, RelationshipType.Sibling)
            : (connectedPersonId, subjectId, RelationshipType.Sibling),
        _ => throw new ArgumentException($"Invalid relationship type: {relationshipType}")
    };
}
