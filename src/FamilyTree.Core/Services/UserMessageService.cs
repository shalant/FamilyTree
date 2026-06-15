using FamilyTree.Core.Data;
using FamilyTree.Core.Models;
using FamilyTree.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FamilyTree.Core.Services;

public class UserMessageService(
    IDbContextFactory<AppDbContext> dbFactory,
    IEmailSender emailSender,
    ICurrentUserService currentUser,
    IConfiguration config,
    ILogger<UserMessageService> logger) : IUserMessageService
{
    public async Task<ServiceResponse> SubmitFeatureRequestAsync(
        string title, string category, string description, string priority, string? senderEmail)
    {
        try
        {
            var msg = new UserMessage
            {
                Type        = "FeatureRequest",
                UserId      = currentUser.UserId,
                SenderEmail = senderEmail?.Trim() ?? "",
                Title       = title.Trim(),
                Body        = description?.Trim(),
                Category    = category,
                Priority    = priority,
                CreatedAt   = DateTime.UtcNow,
            };
            await SaveAsync(msg);
            _ = NotifyAdminAsync(msg);
            return ServiceResponse.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error saving feature request");
            return ServiceResponse.Fail("Could not save your request. Please try again.");
        }
    }

    public async Task<ServiceResponse> SubmitContactMessageAsync(
        string subject, string body, string? senderEmail)
    {
        try
        {
            var msg = new UserMessage
            {
                Type        = "ContactAdmin",
                UserId      = currentUser.UserId,
                SenderEmail = senderEmail?.Trim() ?? "",
                Title       = subject.Trim(),
                Body        = body.Trim(),
                CreatedAt   = DateTime.UtcNow,
            };
            await SaveAsync(msg);
            _ = NotifyAdminAsync(msg);
            return ServiceResponse.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error saving contact message");
            return ServiceResponse.Fail("Could not send your message. Please try again.");
        }
    }

    public async Task<ServiceResponse<List<UserMessage>>> GetAllAsync()
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync();
            var messages = await ctx.UserMessages
                .OrderByDescending(m => m.CreatedAt)
                .ToListAsync();
            return ServiceResponse<List<UserMessage>>.Ok(messages);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading messages");
            return ServiceResponse<List<UserMessage>>.Fail("Could not load messages.");
        }
    }

    public async Task MarkReadAsync(Guid id)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync();
            var msg = await ctx.UserMessages.FindAsync(id);
            if (msg != null && msg.ReadAt == null)
            {
                msg.ReadAt = DateTime.UtcNow;
                await ctx.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error marking message {Id} as read", id);
        }
    }

    private async Task SaveAsync(UserMessage msg)
    {
        await using var ctx = await dbFactory.CreateDbContextAsync();
        ctx.UserMessages.Add(msg);
        await ctx.SaveChangesAsync();
    }

    private async Task NotifyAdminAsync(UserMessage msg)
    {
        var adminEmail = config["SuperUser:Email"];
        if (string.IsNullOrWhiteSpace(adminEmail)) return;

        try
        {
            var typeLabel  = msg.Type == "FeatureRequest" ? "Feature Request" : "Message";
            var resolvedEmail = !string.IsNullOrWhiteSpace(msg.SenderEmail) ? msg.SenderEmail : currentUser.Email;
            var from       = string.IsNullOrWhiteSpace(resolvedEmail) ? "an anonymous user" : resolvedEmail;
            var subject    = $"[ArborKin] New {typeLabel} from {from}";
            var dateString = msg.CreatedAt.ToString("dddd, MMMM d, yyyy 'at' h:mm tt") + " UTC";

            var metaBadges = msg.Category is not null ? $"""
                <table cellpadding="0" cellspacing="0" border="0" style="margin-top:20px;">
                  <tr>
                    <td style="padding-right:10px;">
                      <span style="display:inline-block; background:#e8f5ee; color:#085041;
                        border:1px solid #b6dfc8; border-radius:20px; padding:4px 14px;
                        font-size:12px; font-weight:600; letter-spacing:0.04em;">
                        {msg.Category}
                      </span>
                    </td>
                    <td>
                      <span style="display:inline-block; background:#fdf6ec; color:#7a4f1e;
                        border:1px solid #f0d9b5; border-radius:20px; padding:4px 14px;
                        font-size:12px; font-weight:600; letter-spacing:0.04em;">
                        {msg.Priority}
                      </span>
                    </td>
                  </tr>
                </table>
                """ : "";

            var bodyContent = !string.IsNullOrWhiteSpace(msg.Body) ? $"""
                <p style="margin:20px 0 0; font-size:15px; line-height:1.65; color:#374151;">
                  {msg.Body}
                </p>
                """ : "";

            var body = $"""
                <!DOCTYPE html>
                <html lang="en">
                <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
                <body style="margin:0; padding:0; background:#f0faf4; font-family:'Georgia',serif;">
                  <table width="100%" cellpadding="0" cellspacing="0" border="0"
                    style="background:#f0faf4; padding:40px 16px;">
                    <tr><td align="center">
                      <table width="580" cellpadding="0" cellspacing="0" border="0"
                        style="max-width:580px; width:100%; border-radius:12px;
                          overflow:hidden; box-shadow:0 4px 24px rgba(0,0,0,0.10);">

                        <!-- Header -->
                        <tr>
                          <td style="background:#085041; padding:28px 36px 24px;">
                            <p style="margin:0; font-size:22px; font-weight:700;
                              color:#ffffff; letter-spacing:0.02em; font-family:'Georgia',serif;">
                              ArborKin
                            </p>
                            <p style="margin:6px 0 0; font-size:13px; color:#9FE1CB;
                              letter-spacing:0.06em; text-transform:uppercase;
                              font-family:Arial,sans-serif; font-weight:600;">
                              New {typeLabel}
                            </p>
                          </td>
                        </tr>

                        <!-- Content card -->
                        <tr>
                          <td style="background:#ffffff; padding:32px 36px 36px;">
                            <p style="margin:0; font-size:20px; font-weight:700;
                              color:#0d1f1a; line-height:1.35; font-family:'Georgia',serif;">
                              {msg.Title}
                            </p>
                            {metaBadges}
                            {bodyContent}
                          </td>
                        </tr>

                        <!-- Footer -->
                        <tr>
                          <td style="background:#f8faf9; padding:18px 36px;
                            border-top:1px solid #e2ede8;">
                            <p style="margin:0; font-size:12px; color:#6b7280;
                              font-family:Arial,sans-serif; line-height:1.6;">
                              Sent by <strong style="color:#085041;">{from}</strong>
                              &nbsp;·&nbsp; {dateString}
                            </p>
                          </td>
                        </tr>

                      </table>
                    </td></tr>
                  </table>
                </body>
                </html>
                """;

            await emailSender.SendAsync(adminEmail, subject, body);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to email admin about message {Id}", msg.Id);
        }
    }
}
