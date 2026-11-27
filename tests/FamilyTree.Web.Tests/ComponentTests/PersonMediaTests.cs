using Bunit;
using FamilyTree.Core.Services;
using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Medium;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Web.Modules.Components;
using FamilyTree.Web.Modules.Pages;
using FamilyTree.Web.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

// "Use as profile" saved the person through UpdateAsync with a DTO built by hand that left
// out FormerSpouseIds, SiblingIds and Email, and UpdateAsync replaces every link with what
// it's sent: choosing a profile photo deleted the person's former marriages and sibling
// links, and their email (found in the B7 review, 2026-10-25). The page now starts from
// PersonMapper.ToUpsertDto.
public class PersonMediaTests : ComponentTestBase
{
    private static readonly Guid PersonId = Guid.NewGuid();

    // Since 2026-11-26 it calls SetProfilePhotoAsync, which any member may use and which
    // touches nothing else on the person.
    [Fact]
    public void UseAsProfile_SetsOnlyThePhoto()
    {
        var people = new FakePersonServiceForMedia(
            new PersonDto { Id = PersonId, FirstName = "Ada", LastName = "Arden" });
        Services.AddSingleton<IPersonService>(people);
        Services.AddSingleton<IMediumService>(new FakeMediumServiceForMedia());
        RenderComponent<MudPopoverProvider>();

        var cut = RenderComponent<PersonMedia>(p => p.Add(x => x.Id, PersonId));
        cut.FindAll("button").First(b => b.TextContent.Contains("Use as profile")).Click();

        people.ProfilePhotos.Should().ContainSingle().Which.Should().Be(PersonId);
        people.Updates.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Regression")]
    public async Task UnreadablePhoto_ShowsPlainToast_AndTellsTheAdmin()
    {
        Services.AddSingleton<IPersonService>(new FakePersonServiceForMedia(
            new PersonDto { Id = PersonId, FirstName = "Ada", LastName = "Arden" }));
        Services.AddSingleton<IMediumService>(new FakeMediumServiceForMedia());
        RenderComponent<MudPopoverProvider>();
        var cut = RenderComponent<PersonMedia>(p => p.Add(x => x.Id, PersonId));
        var zone = cut.FindComponent<MediaUploadZone>();

        await cut.InvokeAsync(() => zone.Instance.OnFilesSelected.InvokeAsync([new UnreadableFile()]));

        var toast = Services.GetRequiredService<ToastService>().Toasts.Should().ContainSingle().Subject;
        toast.Message.Should().StartWith("That photo couldn't be opened").And.NotContain("NotReadableError");
        UploadFailures.Reports.Should().ContainSingle();
    }

    // Found live 2026-10-24: a photo that failed to load showed as an empty box with the
    // browser's broken-image icon, still offering "Use as profile".
    [Fact]
    [Trait("Category", "Regression")]
    public void PhotoThatFailsToLoad_ShowsCouldntBeLoadedTile_AndNoUseAsProfile()
    {
        Services.AddSingleton<IPersonService>(new FakePersonServiceForMedia(
            new PersonDto { Id = PersonId, FirstName = "Ada", LastName = "Arden" }));
        Services.AddSingleton<IMediumService>(new FakeMediumServiceForMedia());
        RenderComponent<MudPopoverProvider>();

        var cut = RenderComponent<PersonMedia>(p => p.Add(x => x.Id, PersonId));
        cut.FindAll("button").Should().Contain(b => b.TextContent.Contains("Use as profile"));

        cut.Find("img").TriggerEvent("onerror", new Microsoft.AspNetCore.Components.Web.ErrorEventArgs());

        cut.FindAll("img").Should().BeEmpty();
        cut.Find(".ft-photo-broken-tile").TextContent.Should().Contain("Photo couldn't be loaded");
        cut.FindAll("button").Should().NotContain(b => b.TextContent.Contains("Use as profile"));
    }

    // UX/UI review E1 (2026-11-22): Edit and Delete only appeared on mouse hover, which a phone
    // can't do. Every action is a labelled button under the photo from the first render.
    [Fact]
    public void PhotoActions_AreVisibleWithoutHover_AndNamedForScreenReaders()
    {
        Services.AddSingleton<IPersonService>(new FakePersonServiceForMedia(
            new PersonDto { Id = PersonId, FirstName = "Ada", LastName = "Arden" }));
        Services.AddSingleton<IMediumService>(new FakeMediumServiceForMedia());
        RenderComponent<MudPopoverProvider>();

        var cut = RenderComponent<PersonMedia>(p => p.Add(x => x.Id, PersonId));

        var actions = cut.FindAll(".ft-photo-tile__action").Select(b => b.TextContent.Trim()).ToList();
        actions.Should().Contain("Use as profile");
        actions.Should().Contain(a => a.EndsWith("caption"));
        actions.Should().Contain("Remove");
        cut.Find(".ft-photo-tile__action--remove").GetAttribute("aria-label").Should().StartWith("Remove photo: ");
    }

    [Fact]
    public void BrokenPhoto_CanStillBeRemoved()
    {
        Services.AddSingleton<IPersonService>(new FakePersonServiceForMedia(
            new PersonDto { Id = PersonId, FirstName = "Ada", LastName = "Arden" }));
        Services.AddSingleton<IMediumService>(new FakeMediumServiceForMedia());
        RenderComponent<MudPopoverProvider>();

        var cut = RenderComponent<PersonMedia>(p => p.Add(x => x.Id, PersonId));
        cut.Find("img").TriggerEvent("onerror", new Microsoft.AspNetCore.Components.Web.ErrorEventArgs());

        cut.FindAll(".ft-photo-tile__action").Select(b => b.TextContent.Trim()).Should().Contain("Remove");
    }

    [Fact]
    public void SomeoneElsesPhoto_HasNoRemoveButton()
    {
        Services.AddSingleton<IPersonService>(new FakePersonServiceForMedia(
            new PersonDto { Id = PersonId, FirstName = "Ada", LastName = "Arden" }));
        Services.AddSingleton<IMediumService>(new FakeMediumServiceForMedia { CanDelete = false });
        RenderComponent<MudPopoverProvider>();

        var cut = RenderComponent<PersonMedia>(p => p.Add(x => x.Id, PersonId));

        cut.FindAll(".ft-photo-tile__action--remove").Should().BeEmpty();
        cut.FindAll("button").Should().Contain(b => b.TextContent.Contains("Use as profile"));
    }

    // The Add/Edit Person form's photo tiles had the same 22px unlabelled ✕ in the corner.
    [Fact]
    public void PersonForm_HasNoUnlabelledCornerCloseButtonOnPhotos()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FamilyTree.sln"))) dir = dir.Parent;
        var form = File.ReadAllText(Path.Combine(dir!.FullName, "src", "FamilyTree.Web", "Modules", "Components", "PersonForm.razor"));

        form.Should().NotContain("Icons.Material.Outlined.Close");
        form.Should().Contain("ft-photo-tile__action--remove");
    }

    [Fact]
    public void UploadHint_DoesNotPromiseHeic()
    {
        Services.AddSingleton<IPersonService>(new FakePersonServiceForMedia(
            new PersonDto { Id = PersonId, FirstName = "Ada", LastName = "Arden" }));
        Services.AddSingleton<IMediumService>(new FakeMediumServiceForMedia());
        RenderComponent<MudPopoverProvider>();

        var cut = RenderComponent<PersonMedia>(p => p.Add(x => x.Id, PersonId));

        cut.Markup.Should().NotContain("JPG, PNG, HEIC");
        cut.Markup.Should().Contain("HEIC works only if your browser can convert it");
    }
}

class FakePersonServiceForMedia(PersonDto person) : IPersonService
{
    public List<PersonUpsertDto> Updates { get; } = [];

    public Task<ServiceResponse<PersonDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(ServiceResponse<PersonDto>.Ok(person));

    public Task<ServiceResponse<PersonDto>> UpdateAsync(Guid id, PersonUpsertDto dto, CancellationToken ct = default)
    {
        Updates.Add(dto);
        return Task.FromResult(ServiceResponse<PersonDto>.Ok(person));
    }

    public Task<ServiceResponse<List<PersonDto>>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult(ServiceResponse<List<PersonDto>>.Ok([person]));
    public Task<ServiceResponse<List<PersonDto>>> GetAllForUserAsync(Guid userId, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse<PersonDto>> CreateAsync(PersonUpsertDto dto, CancellationToken ct = default)
        => throw new NotImplementedException();
    public List<Guid> ProfilePhotos { get; } = [];

    public Task<ServiceResponse<FamilyTree.Shared.Enums.PersonEditAccess>> GetEditAccessAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(ServiceResponse<FamilyTree.Shared.Enums.PersonEditAccess>.Ok(FamilyTree.Shared.Enums.PersonEditAccess.Full));
    public Task<ServiceResponse> SetProfilePhotoAsync(Guid personId, Guid mediumId, CancellationToken ct = default)
    {
        ProfilePhotos.Add(personId);
        return Task.FromResult(ServiceResponse.Ok());
    }
    public Task<ServiceResponse> DeleteAsync(Guid id, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse> RestoreAsync(Guid id, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse<List<PersonDto>>> GetDeletedAsync(CancellationToken ct = default)
        => throw new NotImplementedException();
}

class FakeMediumServiceForMedia : IMediumService
{
    public bool CanDelete { get; init; } = true;

    public Task<ServiceResponse<List<MediumDto>>> GetByPersonIdAsync(Guid personId, CancellationToken ct = default)
        => Task.FromResult(ServiceResponse<List<MediumDto>>.Ok(
            [new MediumDto { Id = Guid.NewGuid(), PersonId = personId, Url = "/media/photo.jpg", FileName = "photo.jpg", Type = "photo", CanDelete = CanDelete }]));

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
