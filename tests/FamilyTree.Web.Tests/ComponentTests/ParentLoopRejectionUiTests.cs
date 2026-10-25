using Bunit;
using FamilyTree.Core.Services;
using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Medium;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Shared.DTOs.Relationship;
using FamilyTree.Web.Modules.Pages;
using FamilyTree.Web.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

// R0 (docs/TodoList.md): PersonService now refuses a parent/child link that would make
// someone their own ancestor, with a ServiceResponse.Fail message. These tests pin the
// other half: the Add and Edit pages must turn that failure into a visible error toast
// (and stay on the page), never swallow it. They pass whether or not the service rule
// exists, since they only exercise the pages' handling of ANY failed save.
public class ParentLoopRejectionUiTests : ComponentTestBase
{
    private const string LoopMessage =
        "Kid is already Grandpa's descendant, so they can't also be Grandpa's parent. " +
        "That would create a loop in the family tree.";

    private readonly Guid _personId = Guid.NewGuid();
    private readonly RejectingPersonService _people;

    public ParentLoopRejectionUiTests()
    {
        _people = new RejectingPersonService(new PersonDto
        {
            Id = _personId, FirstName = "Grandpa", LastName = "Test", ParentIds = [], ChildIds = [],
            SpouseIds = [], FormerSpouseIds = [], SiblingIds = [],
        }, LoopMessage);
        Services.AddSingleton<IPersonService>(_people);
        Services.AddSingleton<IMediumService>(new NoPhotosMediumService());
        Services.AddSingleton<IRelationshipService>(new UnusedRelationshipService());
        RenderComponent<MudPopoverProvider>();
    }

    [Fact]
    public async Task EditPage_ShowsTheLoopMessageAsAnErrorToast_WhenTheSaveIsRejected()
    {
        var nav = Services.GetRequiredService<Bunit.TestDoubles.FakeNavigationManager>();
        var cut = RenderComponent<PersonEdit>(p => p.Add(x => x.Id, _personId));
        var uriBefore = nav.Uri;

        await ClickAsync(cut.FindAll("button").First(b => b.TextContent.Trim() == "Save changes"));

        _people.UpdateCalls.Should().Be(1);
        var toasts = Services.GetRequiredService<ToastService>().Toasts;
        toasts.Should().ContainSingle(t => t.Type == ToastType.Error && t.Message == LoopMessage);
        toasts.Should().NotContain(t => t.Type == ToastType.Success, "a rejected save must not claim it saved");
        nav.Uri.Should().Be(uriBefore, "the user stays on the edit form to correct the link");
    }

    [Fact]
    public async Task AddPage_ShowsTheLoopMessageAsAnErrorToastAndInline_WhenTheSaveIsRejected()
    {
        var cut = RenderComponent<PersonAdd>();
        await ChangeAsync(cut.Find("input[placeholder='e.g. Margaret']"), "Xavier");
        await ChangeAsync(cut.Find("input[placeholder='e.g. Rosenberg']"), "Test");

        await ClickAsync(cut.FindAll("button").First(b => b.TextContent.Trim() == "Add person"));

        _people.CreateCalls.Should().Be(1);
        Services.GetRequiredService<ToastService>().Toasts
            .Should().ContainSingle(t => t.Type == ToastType.Error && t.Message == LoopMessage);
        cut.Markup.Should().Contain(LoopMessage, "the page also shows the error inline");
    }
}

// Succeeds at every read, fails every write with the supplied message.
class RejectingPersonService(PersonDto person, string failureMessage) : IPersonService
{
    public int UpdateCalls { get; private set; }
    public int CreateCalls { get; private set; }

    public Task<ServiceResponse<List<PersonDto>>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult(ServiceResponse<List<PersonDto>>.Ok([person]));
    public Task<ServiceResponse<PersonDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(ServiceResponse<PersonDto>.Ok(person));
    public Task<ServiceResponse<PersonDto>> CreateAsync(PersonUpsertDto dto, CancellationToken ct = default)
    {
        CreateCalls++;
        return Task.FromResult(ServiceResponse<PersonDto>.Fail(failureMessage));
    }
    public Task<ServiceResponse<PersonDto>> UpdateAsync(Guid id, PersonUpsertDto dto, CancellationToken ct = default)
    {
        UpdateCalls++;
        return Task.FromResult(ServiceResponse<PersonDto>.Fail(failureMessage));
    }

    public Task<ServiceResponse<List<PersonDto>>> GetAllForUserAsync(Guid userId, CancellationToken ct = default)
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

// PersonAdd links extra siblings through this after a successful add; a rejected add
// never gets that far.
class UnusedRelationshipService : IRelationshipService
{
    public Task<ServiceResponse<List<RelationshipDto>>> GetAllAsync(CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse<List<RelationshipDto>>> GetForPersonAsync(Guid personId, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse<RelationshipDto>> CreateAsync(RelationshipUpsertDto dto, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse<RelationshipDto>> UpdateAsync(Guid id, RelationshipUpsertDto request, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse> DeleteAsync(Guid id, CancellationToken ct = default)
        => throw new NotImplementedException();
}

// Edit mode asks for the person's photos; this person has none.
class NoPhotosMediumService : IMediumService
{
    public Task<ServiceResponse<List<MediumDto>>> GetByPersonIdAsync(Guid personId, CancellationToken ct = default)
        => Task.FromResult(ServiceResponse<List<MediumDto>>.Ok([]));

    public Task<ServiceResponse<List<MediumDto>>> GetAllAsync(CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse<MediumDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse<MediumDto>> CreateAsync(MediumUpsertDto dto, Stream fileStream, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse<MediumDto>> UpdateAsync(Guid id, MediumUpsertDto dto, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse> DeleteAsync(Guid id, CancellationToken ct = default)
        => throw new NotImplementedException();
}
