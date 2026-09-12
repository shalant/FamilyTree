using Bunit;
using Bunit.TestDoubles;
using FamilyTree.Core.Services;
using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Web.Layout;
using FamilyTree.Web.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

public class CustomAppBarTests : ComponentTestBase
{
    public CustomAppBarTests()
    {
        Services.AddSingleton<IPersonService>(new FakePersonService());
        Services.AddSingleton<ThemeService>();
        this.AddTestAuthorization().SetAuthorized("Test User");

        // MudAutocomplete/MudMenu resolve popovers through the shared PopoverService —
        // it throws "Missing <MudPopoverProvider />" unless one has rendered first.
        RenderComponent<MudPopoverProvider>();
    }

    // Replaces the old dead ToggleContext()/_contextExpanded code path — tapping
    // the mobile identity block now opens MobileControlPanel via TreeContextService.
    // MobileControlPanel only exists on Home's canvas view once a focus person is
    // showing (see Home.razor), so the tap only does anything once that's true —
    // see TappingIdentityBlock_DoesNothing_WhenNoFocusPersonExists below for the
    // complementary case this guard exists for.
    [Fact]
    public void TappingIdentityBlock_TogglesMobilePanel_WhenFocusPersonExists()
    {
        var treeContext = Services.GetRequiredService<TreeContextService>();
        treeContext.SetContext(new PersonDto { FirstName = "Doug", LastName = "Rosenberg" }, 12, 4);
        var cut = RenderComponent<CustomAppBar>();

        treeContext.MobilePanelOpen.Should().BeFalse();

        cut.Find(".ft-appbar-family-title").Click();

        treeContext.MobilePanelOpen.Should().BeTrue();
    }

    // A brand-new user on the "let's get you linked to the tree" screen (or
    // anyone on any other page) has no canvas and no MobileControlPanel to
    // reveal — before this guard, the identity block still looked and acted
    // tappable there but silently did nothing visible, since ToggleMobilePanel()
    // flipped state nothing was listening to.
    [Fact]
    public void TappingIdentityBlock_DoesNothing_WhenNoFocusPersonExists()
    {
        var treeContext = Services.GetRequiredService<TreeContextService>();
        var cut = RenderComponent<CustomAppBar>();

        cut.Find(".ft-appbar-family-title").Click();

        treeContext.MobilePanelOpen.Should().BeFalse();
    }

    // Found live: two browser sessions on the same account rendered the caret
    // differently purely because one had a plain "/" URL and the other had a
    // shared/deep-linked "/?focus=<id>" one. CanRevealMobileToolbar's route
    // check used NavigationManager.ToBaseRelativePath(Uri).TrimEnd('/').Length,
    // but ToBaseRelativePath keeps the query string attached — "?focus=<id>"
    // is never empty after TrimEnd('/'), so any query string on the home
    // route wrongly read as "not home" and hid the caret. Same scenario as
    // TappingIdentityBlock_TogglesMobilePanel_WhenFocusPersonExists above,
    // just with a query string on the URL, which must not change the outcome.
    [Fact]
    public void TappingIdentityBlock_TogglesMobilePanel_WhenUrlHasFocusQueryString()
    {
        var treeContext = Services.GetRequiredService<TreeContextService>();
        treeContext.SetContext(new PersonDto { FirstName = "Doug", LastName = "Rosenberg" }, 12, 4);
        var nav = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        nav.NavigateTo("/?focus=7619ba51-080b-416b-b214-f581549c2e1f");
        var cut = RenderComponent<CustomAppBar>();

        treeContext.MobilePanelOpen.Should().BeFalse();

        cut.Find(".ft-appbar-family-title").Click();

        treeContext.MobilePanelOpen.Should().BeTrue();
    }
}

class FakePersonService : IPersonService
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
    public Task<ServiceResponse> DeleteAsync(Guid id, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse> RestoreAsync(Guid id, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse<List<PersonDto>>> GetDeletedAsync(CancellationToken ct = default)
        => throw new NotImplementedException();
}
