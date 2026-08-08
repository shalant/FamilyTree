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
    [Fact]
    public void TappingIdentityBlock_TogglesMobilePanel()
    {
        var treeContext = Services.GetRequiredService<TreeContextService>();
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
