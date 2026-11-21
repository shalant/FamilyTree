using Bunit;
using FamilyTree.Core.Services;
using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Web.Modules.Pages;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

// 2026-11-20: the Family members grid showed people in database order, which reads as random.
// It now starts alphabetical by last name, then first name, then oldest first.
public class PeoplePageOrderTests : ComponentTestBase
{
    private readonly OrderedPeople _people = new();

    public PeoplePageOrderTests()
    {
        Services.AddSingleton<IPersonService>(_people);
        RenderComponent<MudPopoverProvider>();
    }

    [Fact]
    public void Grid_StartsAlphabetical_ByLastThenFirstThenOldest()
    {
        _people.All.AddRange(
        [
            new PersonDto { Id = Guid.NewGuid(), FirstName = "Walker", LastName = "Rosenberg", BirthDate = new DateOnly(2000, 1, 1) },
            new PersonDto { Id = Guid.NewGuid(), FirstName = "ellen", LastName = "Rosenberg", BirthDate = new DateOnly(1960, 1, 1) },
            new PersonDto { Id = Guid.NewGuid(), FirstName = "Isador", LastName = "Eisenberg" },
            new PersonDto { Id = Guid.NewGuid(), FirstName = "Marc", LastName = "Rosenberg", BirthDate = new DateOnly(1990, 5, 5) },
            new PersonDto { Id = Guid.NewGuid(), FirstName = "Marc", LastName = "Rosenberg", BirthDate = new DateOnly(1950, 5, 5) },
        ]);

        var cut = RenderComponent<People>();
        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.Should().Be(5));

        var names = cut.FindAll("tbody tr")
            .Select(r => r.QuerySelector("td[data-label='Name'] span")!.TextContent.Trim()).ToList();
        names.Should().Equal("Isador Eisenberg", "ellen Rosenberg", "Marc Rosenberg", "Marc Rosenberg", "Walker Rosenberg");

        var born = cut.FindAll("tbody tr").Select(r => r.QuerySelector("td[data-label='Born']")!.TextContent.Trim()).ToList();
        born[2].Should().Contain("1950", "of two people with one name, the older comes first");
    }

    private sealed class OrderedPeople : IPersonService
    {
        public List<PersonDto> All { get; } = [];

        public Task<ServiceResponse<List<PersonDto>>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult(ServiceResponse<List<PersonDto>>.Ok(All));
        public Task<ServiceResponse<List<PersonDto>>> GetAllForUserAsync(Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse<PersonDto>> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse<PersonDto>> CreateAsync(PersonUpsertDto dto, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse<PersonDto>> UpdateAsync(Guid id, PersonUpsertDto dto, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse> SetProfilePhotoAsync(Guid personId, Guid mediumId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse> DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse> RestoreAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse<List<PersonDto>>> GetDeletedAsync(CancellationToken ct = default) => throw new NotImplementedException();
    }
}
