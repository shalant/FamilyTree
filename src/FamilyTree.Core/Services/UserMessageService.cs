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
            var typeLabel = msg.Type == "FeatureRequest" ? "Feature Request" : "Message";
            var from      = string.IsNullOrWhiteSpace(msg.SenderEmail) ? "an anonymous user" : msg.SenderEmail;
            var subject   = $"[ArborKin] New {typeLabel} from {from}";

            var details = msg.Category is not null
                ? $"<p><strong>Category:</strong> {msg.Category}<br><strong>Priority:</strong> {msg.Priority}</p>"
                : "";

            var body = $"""
                <p><strong>{msg.Title}</strong></p>
                {details}
                <p>{msg.Body}</p>
                <hr style="border:none;border-top:1px solid #eee;margin:16px 0;">
                <p style="font-size:12px;color:#888;">From: {from} · {msg.CreatedAt:f} UTC</p>
                """;

            await emailSender.SendAsync(adminEmail, subject, body);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to email admin about message {Id}", msg.Id);
        }
    }
}
