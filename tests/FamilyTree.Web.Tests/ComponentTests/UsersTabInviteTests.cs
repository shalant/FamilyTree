using Bunit;
using FamilyTree.Core.Models;
using FamilyTree.Core.Services;
using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Web.Modules.Admin.Components;
using FamilyTree.Web.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MudBlazor;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

// Admin > Users used to stop at "Generate link": the admin had to copy the link and send it
// themselves (left over from Outreach A6). "Send invite" now makes the invite and emails
// it; the link stays on screen so a failed send isn't a dead end.
public class UsersTabInviteTests : ComponentTestBase
{
    private static readonly Guid InviteId = Guid.NewGuid();
    private readonly FakeAuthService _auth = new() { CreatedInviteId = InviteId };

    private IRenderedComponent<UsersTab> RenderTab()
    {
        Services.AddSingleton<IAuthService>(_auth);
        Services.AddSingleton<IAdminUserService>(new EmptyAdminUserService());
        Services.AddSingleton<IPersonService>(new EmptyPersonService());
        Services.AddSingleton(new UserManager<AppUser>(new NoUserStore(), Options.Create(new IdentityOptions()),
            new PasswordHasher<AppUser>(), [], [], new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(), null!, NullLogger<UserManager<AppUser>>.Instance));
        RenderComponent<MudPopoverProvider>();
        return RenderComponent<UsersTab>();
    }

    private async Task SendInviteAsync(IRenderedComponent<UsersTab> cut, string email)
    {
        await cut.Find("input[type=email]").InputAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = email });
        await ClickAsync(cut.FindAll("button").First(b => b.TextContent.Contains("Send invite")));
    }

    [Fact]
    public async Task SendInvite_EmailsTheInviteAndSaysSo()
    {
        _auth.SendInviteEmailResult = ServiceResponse.Ok();
        var cut = RenderTab();

        await SendInviteAsync(cut, "cousin@example.com");

        _auth.EmailedInvites.Should().ContainSingle().Which.InviteId.Should().Be(InviteId);
        cut.Markup.Should().Contain("Emailed to cousin@example.com");
        cut.Markup.Should().Contain($"register?invite={InviteId}");
        Services.GetRequiredService<ToastService>().Toasts
            .Should().Contain(t => t.Message.Contains("Invite emailed to cousin@example.com"));
    }

    [Fact]
    public async Task SendInvite_WhenTheEmailFails_ShowsTheReasonAndKeepsTheLink()
    {
        _auth.SendInviteEmailResult = ServiceResponse.Fail("SMTP is down. Copy the link and send it yourself.");
        var cut = RenderTab();

        await SendInviteAsync(cut, "cousin@example.com");

        cut.Markup.Should().Contain("SMTP is down");
        cut.Markup.Should().NotContain("Emailed to cousin@example.com");
        cut.Markup.Should().Contain("Send this link to cousin@example.com yourself");
        cut.Markup.Should().Contain($"register?invite={InviteId}");
    }

    private sealed class EmptyAdminUserService : IAdminUserService
    {
        public Task<ServiceResponse<List<AdminUserDto>>> GetAllUsersAsync(CancellationToken ct = default)
            => Task.FromResult(ServiceResponse<List<AdminUserDto>>.Ok([]));
        public Task<ServiceResponse<List<AdminInviteDto>>> GetPendingInvitesAsync(CancellationToken ct = default)
            => Task.FromResult(ServiceResponse<List<AdminInviteDto>>.Ok([]));
        public Task<ServiceResponse<List<AdminPasswordResetDto>>> GetPendingPasswordResetsAsync(CancellationToken ct = default)
            => Task.FromResult(ServiceResponse<List<AdminPasswordResetDto>>.Ok([]));
        public Task<ServiceResponse<List<PersonDto>>> GetPeopleAsync(CancellationToken ct = default)
            => Task.FromResult(ServiceResponse<List<PersonDto>>.Ok([]));
        public Task<ServiceResponse> CancelInviteAsync(Guid inviteId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ServiceResponse> DismissPasswordResetAsync(Guid resetId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ServiceResponse> SetAdminAsync(Guid userId, bool isAdmin, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    private sealed class EmptyPersonService : IPersonService
    {
        public Task<ServiceResponse<List<PersonDto>>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult(ServiceResponse<List<PersonDto>>.Ok([]));
        public Task<ServiceResponse<List<PersonDto>>> GetAllForUserAsync(Guid userId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ServiceResponse<PersonDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ServiceResponse<PersonDto>> CreateAsync(PersonUpsertDto dto, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ServiceResponse<PersonDto>> UpdateAsync(Guid id, PersonUpsertDto dto, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ServiceResponse> SetProfilePhotoAsync(Guid personId, Guid mediumId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ServiceResponse> DeleteAsync(Guid id, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ServiceResponse> RestoreAsync(Guid id, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ServiceResponse<List<PersonDto>>> GetDeletedAsync(CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    // UsersTab injects UserManager but never touches it on the invite path.
    private sealed class NoUserStore : IUserStore<AppUser>
    {
        public void Dispose() { }
        public Task<string> GetUserIdAsync(AppUser user, CancellationToken ct) => throw new NotImplementedException();
        public Task<string?> GetUserNameAsync(AppUser user, CancellationToken ct) => throw new NotImplementedException();
        public Task SetUserNameAsync(AppUser user, string? userName, CancellationToken ct) => throw new NotImplementedException();
        public Task<string?> GetNormalizedUserNameAsync(AppUser user, CancellationToken ct) => throw new NotImplementedException();
        public Task SetNormalizedUserNameAsync(AppUser user, string? normalizedName, CancellationToken ct) => throw new NotImplementedException();
        public Task<IdentityResult> CreateAsync(AppUser user, CancellationToken ct) => throw new NotImplementedException();
        public Task<IdentityResult> UpdateAsync(AppUser user, CancellationToken ct) => throw new NotImplementedException();
        public Task<IdentityResult> DeleteAsync(AppUser user, CancellationToken ct) => throw new NotImplementedException();
        public Task<AppUser?> FindByIdAsync(string userId, CancellationToken ct) => throw new NotImplementedException();
        public Task<AppUser?> FindByNameAsync(string normalizedUserName, CancellationToken ct) => throw new NotImplementedException();
    }
}
