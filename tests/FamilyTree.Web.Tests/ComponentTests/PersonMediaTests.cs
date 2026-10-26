using Bunit;
using FamilyTree.Core.Services;
using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Medium;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Web.Modules.Pages;
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

    [Fact]
    public void UseAsProfile_KeepsTheRestOfThePerson()
    {
        var person = new PersonDto
        {
            Id = PersonId, FirstName = "Ada", LastName = "Arden", Email = "ada@example.com",
            ParentIds = [Guid.NewGuid()], SpouseIds = [Guid.NewGuid()], FormerSpouseIds = [Guid.NewGuid()],
            ChildIds = [Guid.NewGuid()], SiblingIds = [Guid.NewGuid()],
        };
        var people = new FakePersonServiceForMedia(person);
        Services.AddSingleton<IPersonService>(people);
        Services.AddSingleton<IMediumService>(new FakeMediumServiceForMedia());
        RenderComponent<MudPopoverProvider>();

        var cut = RenderComponent<PersonMedia>(p => p.Add(x => x.Id, PersonId));
        cut.FindAll("button").First(b => b.TextContent.Contains("Use as profile")).Click();

        var sent = people.Updates.Should().ContainSingle().Subject;
        sent.ProfilePhotoUrl.Should().Be("/media/photo.jpg");
        sent.FormerSpouseIds.Should().Equal(person.FormerSpouseIds);
        sent.SiblingIds.Should().Equal(person.SiblingIds);
        sent.Email.Should().Be("ada@example.com");
        sent.ParentIds.Should().Equal(person.ParentIds);
        sent.SpouseIds.Should().Equal(person.SpouseIds);
        sent.ChildIds.Should().Equal(person.ChildIds);
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
    public Task<ServiceResponse> SetProfilePhotoAsync(Guid personId, Guid mediumId, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse> DeleteAsync(Guid id, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse> RestoreAsync(Guid id, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse<List<PersonDto>>> GetDeletedAsync(CancellationToken ct = default)
        => throw new NotImplementedException();
}

class FakeMediumServiceForMedia : IMediumService
{
    public Task<ServiceResponse<List<MediumDto>>> GetByPersonIdAsync(Guid personId, CancellationToken ct = default)
        => Task.FromResult(ServiceResponse<List<MediumDto>>.Ok(
            [new MediumDto { Id = Guid.NewGuid(), PersonId = personId, Url = "/media/photo.jpg", FileName = "photo.jpg", Type = "photo" }]));

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
