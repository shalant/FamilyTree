using Bunit;
using FamilyTree.Core.Services;
using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Web.Modules.Components;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

// Found on a real phone, 2026-11-19: the sign-up "Who are you?" list offered people who already
// had an account (including the site owner), so a newcomer could claim a relative who was
// already signed up. The list now leaves them out and nothing is preselected.
public class LinkToTreeModalTests : ComponentTestBase
{
    private static readonly PersonDto Taken = new() { Id = Guid.NewGuid(), FirstName = "Douglas", LastName = "Rosenberg" };
    private static readonly PersonDto Free1 = new() { Id = Guid.NewGuid(), FirstName = "Isador", LastName = "Eisenberg" };
    private static readonly PersonDto Free2 = new() { Id = Guid.NewGuid(), FirstName = "Rose", LastName = "Eisenberg" };

    [Fact]
    [Trait("Category", "Regression")]
    public async Task WhoAreYou_ListsOnlyPeopleWithoutAnAccount_AndPreselectsNobody()
    {
        var auth = new FakeAuthService();
        auth.LinkedPersonIds.Add(Taken.Id);
        Services.AddSingleton<IAuthService>(auth);
        Services.AddSingleton<IPersonService>(new TreePeople([Taken, Free1, Free2]));
        Services.AddSingleton<IStoryService>(new FakeStoryServiceForRegister());

        var provider = RenderComponent<MudDialogProvider>();
        var popovers = RenderComponent<MudPopoverProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<LinkToTreeModal> { { d => d.UserId, Guid.NewGuid() } };
        await provider.InvokeAsync(() => dialogs.ShowAsync<LinkToTreeModal>("Link", parameters));

        provider.WaitForAssertion(() => provider.FindAll("button").Should().Contain(b => b.TextContent.Contains("Yes, I'm on this tree")));
        await ClickAsync(provider.FindAll("button").First(b => b.TextContent.Contains("Yes, I'm on this tree")));

        provider.Find(".mud-select").Should().NotBeNull();
        provider.Find("input").GetAttribute("value").Should().BeNullOrEmpty("nobody is preselected");
        provider.Find(".mud-input-control").MouseDown();

        var items = popovers.WaitForElements(".mud-list-item").Select(e => e.TextContent.Trim()).ToList();
        items.Should().BeEquivalentTo("Isador Eisenberg", "Rose Eisenberg");
    }

    private sealed class TreePeople(List<PersonDto> people) : IPersonService
    {
        public Task<ServiceResponse<List<PersonDto>>> GetAllAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse<List<PersonDto>>> GetAllForUserAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult(ServiceResponse<List<PersonDto>>.Ok(people));
        public Task<ServiceResponse<PersonDto>> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse<PersonDto>> CreateAsync(PersonUpsertDto dto, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse<PersonDto>> UpdateAsync(Guid id, PersonUpsertDto dto, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse<FamilyTree.Shared.Enums.PersonEditAccess>> GetEditAccessAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(ServiceResponse<FamilyTree.Shared.Enums.PersonEditAccess>.Ok(FamilyTree.Shared.Enums.PersonEditAccess.Full));
        public Task<ServiceResponse> SetProfilePhotoAsync(Guid personId, Guid mediumId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse> DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse> RestoreAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse<List<PersonDto>>> GetDeletedAsync(CancellationToken ct = default) => throw new NotImplementedException();
    }
}
