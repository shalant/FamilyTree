using Bunit;
using FamilyTree.Core.Services;
using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Admin;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Web.Modules.Admin.Components;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

// Regression for a bug found live 2026-07-07 (DeletedTab.razor:176-184): restoring a
// person used to remove their row from the list immediately, which shifted every row
// below it up by one position. A user who clicked Restore on a second row right after
// the first ended up restoring whoever had shifted into that screen position instead —
// confirmed via the audit log restoring Gladys, then Dora Herskovitz, then Harvey
// Fleishman, none of which were the second click the user remembered making. The fix
// marks a row IsRestored=true in place instead of removing it, keeping every other
// row's position (and identity) stable for the rest of the viewing session.
public class DeletedTabTests : ComponentTestBase
{
    private static readonly Guid AliceId = Guid.NewGuid();
    private static readonly Guid BobId = Guid.NewGuid();
    private static readonly Guid CarolId = Guid.NewGuid();

    private FakePersonServiceForDeletedTab _personService = null!;

    public DeletedTabTests()
    {
        _personService = new FakePersonServiceForDeletedTab
        {
            Deleted =
            [
                new PersonDto { Id = AliceId, FirstName = "Alice", LastName = "Anders" },
                new PersonDto { Id = BobId, FirstName = "Bob", LastName = "Brown" },
                new PersonDto { Id = CarolId, FirstName = "Carol", LastName = "Clark" },
            ]
        };

        Services.AddSingleton<IPersonService>(_personService);
        Services.AddSingleton<IDataIntegrityService>(new FakeDataIntegrityService());

        // MudTable's pager resolves popovers through the shared PopoverService — it
        // throws "Missing <MudPopoverProvider />" unless one has rendered first.
        RenderComponent<MudPopoverProvider>();
    }

    // Deliberately clicks by row POSITION, not by re-finding a row by name each time —
    // that's what the real bug depended on. A human clicking a physical screen position
    // twice in quick succession has no way to know a row instantly shifted after the
    // first click; a test that re-queries "the Bob row" by name every time can't
    // reproduce that, since it would just re-locate Bob wherever he ended up.
    [Fact]
    [Trait("Category", "Regression")]
    public void RestoringOneRow_DoesNotShiftOrMisattributeSubsequentRestoreClicks()
    {
        var cut = RenderComponent<DeletedTab>();

        RestoreButtonAtRow(cut, 0).Click(); // Alice, first data row

        _personService.RestoreCalls.Should().ContainSingle().Which.Should().Be(AliceId);

        // Same physical row position as a second click on "row 1" (Bob) would target —
        // with the bug, Alice's removal shifts Carol up into this position instead.
        RestoreButtonAtRow(cut, 1).Click();

        _personService.RestoreCalls.Should().Equal(AliceId, BobId);
    }

    private static AngleSharp.Dom.IElement RestoreButtonAtRow(IRenderedComponent<DeletedTab> cut, int rowIndex)
    {
        var row = cut.FindAll("tbody tr")[rowIndex];
        return row.QuerySelectorAll("button").First(b => b.TextContent.Contains("Restore"));
    }
}

// Only GetDeletedAsync/RestoreAsync are exercised by DeletedTab; everything else
// throws so an unstubbed call fails loudly rather than silently returning a default.
class FakePersonServiceForDeletedTab : IPersonService
{
    public List<PersonDto> Deleted { get; set; } = [];
    public List<Guid> RestoreCalls { get; } = [];

    public Task<ServiceResponse<List<PersonDto>>> GetDeletedAsync(CancellationToken ct = default)
        => Task.FromResult(ServiceResponse<List<PersonDto>>.Ok(Deleted));

    public Task<ServiceResponse> RestoreAsync(Guid id, CancellationToken ct = default)
    {
        RestoreCalls.Add(id);
        return Task.FromResult(ServiceResponse.Ok());
    }

    public Task<ServiceResponse<List<PersonDto>>> GetAllAsync(CancellationToken ct = default)
        => throw new NotImplementedException();
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
}

class FakeDataIntegrityService : IDataIntegrityService
{
    public Task<ServiceResponse<List<OrphanedRelationshipFixDto>>> FixOrphanedRelationshipsAsync(CancellationToken ct = default)
        => throw new NotImplementedException();
}
