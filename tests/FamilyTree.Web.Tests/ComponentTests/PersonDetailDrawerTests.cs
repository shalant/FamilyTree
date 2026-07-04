using Bunit;
using FamilyTree.Core.Services;
using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Medium;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Shared.DTOs.Story;
using FamilyTree.Web.Modules.Components;   // for HeroOverlayComponent, FamilyTreeCanvas, PersonDetailDrawer
using FamilyTree.Web.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

public class PersonDetailDrawerTests : ComponentTestBase
{
    public PersonDetailDrawerTests()
    {
        Services.AddSingleton<ToastService>();
        Services.AddSingleton<FamilyTreeLayoutEngine>();
        Services.AddSingleton<IMediumService>(new FakeMediumService());
        Services.AddSingleton<IStoryService>(new FakeStoryService());
    }

    [Fact(Skip = "Complex component with many MudBlazor JSInterop dependencies")]
    public void ShouldRenderPersonName()
    {
        var person = new PersonDto { FirstName = "Douglas" };

        var cut = RenderComponent<PersonDetailDrawer>(p => p.Add(x => x.Person, person));

        cut.Markup.Should().Contain("Douglas");
    }
}

class FakeMediumService : IMediumService
{
    public Task<ServiceResponse<List<MediumDto>>> GetAllAsync(CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse<List<MediumDto>>> GetByPersonIdAsync(Guid personId, CancellationToken ct = default)
        => Task.FromResult(ServiceResponse<List<MediumDto>>.Ok(new List<MediumDto>()));
    public Task<ServiceResponse<MediumDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse<MediumDto>> CreateAsync(MediumUpsertDto dto, Stream fileStream, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse<MediumDto>> UpdateAsync(Guid id, MediumUpsertDto dto, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse> DeleteAsync(Guid id, CancellationToken ct = default)
        => throw new NotImplementedException();
}

class FakeStoryService : IStoryService
{
    public Task<ServiceResponse<List<StoryDto>>> GetByPersonAsync(Guid personId, CancellationToken ct = default)
        => Task.FromResult(ServiceResponse<List<StoryDto>>.Ok(new List<StoryDto>()));
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
        => Task.FromResult(ServiceResponse<List<StoryDto>>.Ok(new List<StoryDto>()));
    public Task<ServiceResponse<StoryDto>> LinkToPersonAsync(Guid storyId, Guid personId, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse<StoryDto>> ApproveAsync(Guid storyId, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse<StoryDto>> SetHiddenAsync(Guid storyId, bool hidden, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse> MoveAsync(Guid storyId, int direction, CancellationToken ct = default)
        => throw new NotImplementedException();
}