using FamilyTree.Core.Data;
using FamilyTree.Core.Models;
using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Story;
using FamilyTree.Shared.DTOs.StoryInvite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FamilyTree.Core.Services;

public class StoryInviteService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<StoryInviteService> logger,
    IAuditLogService auditLog,
    ICurrentUserService currentUser,
    IEmailSender emailSender,
    IConfiguration config) : IStoryInviteService
{
    // ─────────────────────────────────────────────────────────────
    //  CREATE INVITE
    // ─────────────────────────────────────────────────────────────
    public async Task<ServiceResponse> CreateInviteAsync(
        StoryInviteCreateDto dto, string baseUrl, CancellationToken ct = default)
    {
        try
        {
            var dtoValidation = ValidationHelper.ValidateDtoNonGeneric(dto);
            if (!dtoValidation.Success)
                return ServiceResponse.Fail(dtoValidation.Message);

            if (dto.PersonId is null && string.IsNullOrWhiteSpace(dto.UnlinkedPersonName))
                return ServiceResponse.Fail("Either an existing person or a person's name is required.");

            var invitedByUserId = currentUser.UserId;
            if (invitedByUserId is null)
                return ServiceResponse.Fail("You must be signed in to send an invite.");

            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            Person? person = null;
            if (dto.PersonId.HasValue)
            {
                person = await ctx.People.FirstOrDefaultAsync(p => p.Id == dto.PersonId, ct);
                if (person is null)
                    return ServiceResponse.Fail($"Person {dto.PersonId} not found.");
            }

            var ttlDays = config.GetValue("Stories:InviteTtlDays", 30);
            var token = Convert.ToBase64String(
                    System.Security.Cryptography.RandomNumberGenerator.GetBytes(64))
                .Replace("+", "-").Replace("/", "_").TrimEnd('=');

            var invite = new StoryInvite
            {
                Id = Guid.NewGuid(),
                Token = token,
                PersonId = dto.PersonId,
                UnlinkedPersonName = dto.PersonId.HasValue ? null : dto.UnlinkedPersonName,
                InvitedEmail = dto.InvitedEmail.Trim(),
                InvitedByUserId = invitedByUserId.Value,
                PersonalNote = dto.PersonalNote,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(ttlDays),
                IsUsed = false,
            };
            ctx.StoryInvites.Add(invite);
            await ctx.SaveChangesAsync(ct);

            _ = auditLog.LogAsync("Create", "StoryInvite", invite.Id, userId: invitedByUserId);

            var inviterName = await ctx.Users
                .Where(u => u.Id == invitedByUserId)
                .Select(u => u.DisplayName)
                .FirstOrDefaultAsync(ct);

            var personName = person is not null
                ? $"{person.FirstName} {person.LastName}".Trim()
                : dto.UnlinkedPersonName ?? "a family member";

            var link = $"{baseUrl.TrimEnd('/')}/story/respond/{token}";
            var html = StoryInviteEmailBuilder.Build(
                inviterName: inviterName ?? "A family member",
                personName: personName,
                personPhotoUrl: person?.ProfilePhotoUrl,
                birthYear: person?.BirthDate?.Year,
                deathYear: person?.DeathDate?.Year,
                personalNote: dto.PersonalNote,
                respondLink: link);

            _ = emailSender.SendAsync(
                invite.InvitedEmail,
                $"{inviterName ?? "Someone"} would love to hear your memory of {personName}",
                html,
                ct);

            return ServiceResponse.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating story invite");
            return ServiceResponse.Fail("An error occurred sending this invite.");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  VALIDATE TOKEN
    // ─────────────────────────────────────────────────────────────
    public async Task<ServiceResponse<StoryInviteValidationDto>> ValidateTokenAsync(
        string token, CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var invite = await ctx.StoryInvites
                .AsNoTracking()
                .Include(i => i.Person)
                .Include(i => i.InvitedByUser)
                .FirstOrDefaultAsync(i => i.Token == token, ct);

            if (invite is null)
                return ServiceResponse<StoryInviteValidationDto>.Fail("This invite link is invalid.");

            var isExpired = invite.ExpiresAt < DateTime.UtcNow;

            var result = new StoryInviteValidationDto
            {
                IsValid = !isExpired && !invite.IsUsed,
                IsExpired = isExpired,
                IsUsed = invite.IsUsed,
                PersonId = invite.PersonId,
                PersonName = invite.Person is not null
                    ? $"{invite.Person.FirstName} {invite.Person.LastName}".Trim()
                    : null,
                PersonPhotoUrl = invite.Person?.ProfilePhotoUrl,
                BirthYear = invite.Person?.BirthDate?.Year,
                DeathYear = invite.Person?.DeathDate?.Year,
                UnlinkedPersonName = invite.UnlinkedPersonName,
                InvitedByDisplayName = invite.InvitedByUser?.DisplayName,
                PersonalNote = invite.PersonalNote,
            };

            return ServiceResponse<StoryInviteValidationDto>.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error validating story invite token");
            return ServiceResponse<StoryInviteValidationDto>.Fail(
                "An error occurred validating this invite link.");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  SUBMIT RESPONSE
    // ─────────────────────────────────────────────────────────────
    public async Task<ServiceResponse<StoryDto>> SubmitResponseAsync(
        StoryInviteResponseDto dto, CancellationToken ct = default)
    {
        try
        {
            var dtoValidation = ValidationHelper.ValidateDtoNonGeneric(dto);
            if (!dtoValidation.Success)
                return ServiceResponse<StoryDto>.Fail(dtoValidation.Message);

            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            var invite = await ctx.StoryInvites
                .Include(i => i.Person)
                .FirstOrDefaultAsync(i => i.Token == dto.Token, ct);

            if (invite is null)
                return ServiceResponse<StoryDto>.Fail("This invite link is invalid.");

            if (invite.IsUsed)
                return ServiceResponse<StoryDto>.Fail("A memory has already been submitted for this invite.");

            if (invite.ExpiresAt < DateTime.UtcNow)
                return ServiceResponse<StoryDto>.Fail("This invite link has expired.");

            var displayName = invite.Person is not null
                ? $"{invite.Person.FirstName} {invite.Person.LastName}".Trim()
                : invite.UnlinkedPersonName ?? "this family member";

            var story = new Story
            {
                Id = Guid.NewGuid(),
                PersonId = invite.PersonId,
                UnlinkedPersonName = invite.PersonId.HasValue ? null : invite.UnlinkedPersonName,
                AuthorId = null,
                AuthorName = string.IsNullOrWhiteSpace(dto.AuthorName) ? "Anonymous" : dto.AuthorName.Trim(),
                InviteId = invite.Id,
                Title = string.IsNullOrWhiteSpace(dto.Title) ? $"A memory of {displayName}" : dto.Title.Trim(),
                Body = dto.Body.Trim(),
                IsApproved = false,
                CreatedAt = DateTime.UtcNow,
            };
            ctx.Stories.Add(story);

            invite.IsUsed = true;

            await ctx.SaveChangesAsync(ct);

            _ = auditLog.LogAsync("Create", "Story", story.Id, userId: null);

            return ServiceResponse<StoryDto>.Ok(new StoryDto
            {
                Id = story.Id,
                PersonId = story.PersonId,
                UnlinkedPersonName = story.UnlinkedPersonName,
                AuthorName = story.AuthorName,
                InviteId = story.InviteId,
                Title = story.Title,
                Body = story.Body,
                CreatedAt = story.CreatedAt,
                IsApproved = story.IsApproved,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error submitting story invite response");
            return ServiceResponse<StoryDto>.Fail("An error occurred submitting your memory.");
        }
    }
}
