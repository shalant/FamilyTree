using Bunit;
using FamilyTree.Core.Models;
using FamilyTree.Core.Services;
using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Story;
using FamilyTree.Web.Modules.Pages;
using FamilyTree.Web.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

// Regression for a bug found live during the Golden-Path Walkthrough (docs/TodoList.md,
// 2026-09-07): Register.razor's "Welcome! Your memory has been saved" toast fired for
// ANY invite, not just a story-follow-up ("Willa scenario") one — a plain account invite
// wrongly told a brand-new user their memory had been saved when no memory existed yet.
// The fix gates the toast on _storySubjectName != null (Register.razor:274), which is
// only populated when StoryService.GetPendingLinkingSubjectAsync finds a pending story.
public class RegisterToastTests : ComponentTestBase
{
    private static readonly Guid InviteId = Guid.NewGuid();

    private FakeAuthService _authService = null!;
    private FakeStoryServiceForRegister _storyService = null!;

    private void RegisterFakes(StoryDto? pendingStory)
    {
        _authService = new FakeAuthService
        {
            Invite = new UserInvite { Id = InviteId, Email = "family@example.com" }
        };
        _storyService = new FakeStoryServiceForRegister { PendingStory = pendingStory };

        Services.AddSingleton<IAuthService>(_authService);
        Services.AddSingleton<IStoryService>(_storyService);
    }

    // [SupplyParameterFromQuery] parameters can't be set directly via bUnit's parameter
    // builder — they're only populated by navigating the NavigationManager to a URI
    // containing the query string first (matching the pattern in CustomAppBarTests'
    // ?focus= regression test).
    private void NavigateWithInvite() =>
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
            .NavigateTo($"/register?invite={InviteId}");

    [Fact]
    [Trait("Category", "Regression")]
    public void WelcomeToast_Shown_WhenInviteHasPendingStory()
    {
        RegisterFakes(new StoryDto { Id = Guid.NewGuid(), UnlinkedPersonName = "Bill Small" });
        NavigateWithInvite();

        var cut = RenderComponent<Register>();

        var toastService = Services.GetRequiredService<ToastService>();
        toastService.Toasts.Should().ContainSingle(t => t.Message.Contains("memory has been saved"));
    }

    [Fact]
    [Trait("Category", "Regression")]
    public void WelcomeToast_NotShown_WhenInviteHasNoPendingStory()
    {
        RegisterFakes(pendingStory: null);
        NavigateWithInvite();

        var cut = RenderComponent<Register>();

        var toastService = Services.GetRequiredService<ToastService>();
        toastService.Toasts.Should().NotContain(t => t.Message.Contains("memory has been saved"));
    }
}

// Only ValidateInviteAsync/GetRegistrationMode are exercised by Register.razor's
// initialization path; everything else throws so an unstubbed call fails loudly.
class FakeAuthService : IAuthService
{
    public UserInvite? Invite { get; set; }

    public string GetRegistrationMode() => "Open";
    public Task<ServiceResponse<UserInvite?>> ValidateInviteAsync(Guid inviteId)
        => Task.FromResult(ServiceResponse<UserInvite?>.Ok(Invite));

    public Task<ServiceResponse<Guid>> RegisterAsync(string firstName, string lastName, string email, string password, Guid? inviteId = null)
        => throw new NotImplementedException();
    public Task<ServiceResponse> LinkPersonAsync(Guid userId, Guid? personId)
        => throw new NotImplementedException();
    public Task<ServiceResponse<bool>> UserExistsAsync(string email)
        => throw new NotImplementedException();
    public Task<ServiceResponse> EnsureUserFamilyAsync(Guid userId)
        => throw new NotImplementedException();
    public Task<ServiceResponse<Guid>> CreateInviteAsync(string email)
        => throw new NotImplementedException();
    public Task<ServiceResponse<List<UserInvite>>> GetPendingInvitesAsync()
        => throw new NotImplementedException();
    public Task<ServiceResponse> CancelInviteAsync(Guid inviteId)
        => throw new NotImplementedException();
    public Task<ServiceResponse> SaveFocusPersonAsync(Guid userId, Guid? personId)
        => throw new NotImplementedException();
    public Task<ServiceResponse<Guid?>> GetFocusPersonIdAsync(Guid userId)
        => throw new NotImplementedException();
    public Task<ServiceResponse> RequestPasswordResetAsync(string email, string? baseUrl = null)
        => throw new NotImplementedException();
    public Task<ServiceResponse<bool>> IsResetRequestValidAsync(Guid requestId)
        => throw new NotImplementedException();
    public Task<ServiceResponse> ResetPasswordAsync(Guid requestId, string newPassword)
        => throw new NotImplementedException();
    public Task<ServiceResponse<List<PasswordResetRequest>>> GetPendingResetRequestsAsync()
        => throw new NotImplementedException();
    public Task<ServiceResponse> DismissResetRequestAsync(Guid id)
        => throw new NotImplementedException();
    public Task<ServiceResponse<Guid?>> LinkUserToTreeAsync(Guid userId, Guid? personId, string? firstName, string? lastName, Guid? connectedPersonId = null, string? relationshipType = null)
        => throw new NotImplementedException();
    public Task<ServiceResponse<Guid>> CreateUnlinkedPersonAsync(string? firstName, string? lastName, Guid connectedPersonId, string relationshipType, Guid createdByUserId)
        => throw new NotImplementedException();
    public Task<ServiceResponse<HashSet<Guid>>> GetLinkedPersonIdsAsync(IEnumerable<Guid> personIds)
        => throw new NotImplementedException();
}

// Only GetPendingLinkingSubjectAsync is exercised by Register.razor's initialization
// path; everything else throws so an unstubbed call fails loudly. Named distinctly from
// PersonDetailDrawerTests' FakeStoryService (different interface subset, same file-local
// convention used throughout this test project).
class FakeStoryServiceForRegister : IStoryService
{
    public StoryDto? PendingStory { get; set; }

    public Task<ServiceResponse<StoryDto?>> GetPendingLinkingSubjectAsync(string email, CancellationToken ct = default)
        => Task.FromResult(ServiceResponse<StoryDto?>.Ok(PendingStory));

    public Task<ServiceResponse<List<StoryDto>>> GetByPersonAsync(Guid personId, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse<List<StoryDto>>> GetAllApprovedAsync(CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse<List<StoryDto>>> GetAllAsync(CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse<StoryDto>> CreateAsync(StoryUpsertDto dto, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse<StoryDto>> UpdateAsync(Guid id, StoryUpsertDto dto, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse> DeleteAsync(Guid id, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse<List<StoryDto>>> GetUnlinkedAsync(CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse<List<StoryDto>>> GetByAuthorEmailAsync(string email, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse<StoryDto>> LinkToPersonAsync(Guid storyId, Guid personId, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse<StoryDto>> ApproveAsync(Guid storyId, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse<StoryDto>> SetHiddenAsync(Guid storyId, bool hidden, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse> MoveAsync(Guid storyId, int direction, CancellationToken ct = default)
        => throw new NotImplementedException();
}
