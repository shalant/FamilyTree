using FamilyTree.Core.Models;
using FamilyTree.Core.Services;
using FamilyTree.Core.Tests.Helpers;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Shared.DTOs.Relationship;
using FamilyTree.Shared.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FamilyTree.Core.Tests;

// What the B7 write-path fuzz harness (FamilyTree.Web.Tests/WritePath, 2026-10-25) found,
// pinned one case each so a regression names itself instead of turning up as a random
// session transcript.
public class WritePathFindingsTests
{
    private readonly Guid _familyId = Guid.NewGuid();
    private readonly TestDbContextFactory _db = new();
    private readonly FakeCurrentUserService _admin;

    public WritePathFindingsTests() =>
        _admin = new FakeCurrentUserService { UserId = Guid.NewGuid(), FamilyId = _familyId, IsAdmin = true };

    private PersonService People() =>
        new(_db, NullLogger<PersonService>.Instance, new FakeAuditLogService(), _admin);

    private RelationshipService Relationships() =>
        new(_db, NullLogger<RelationshipService>.Instance, new FakeAuditLogService(), _admin);

    private async Task<Guid> SeedAsync(string first, int? born = null, DateOnly? died = null)
    {
        await using var ctx = _db.CreateDbContext();
        var p = new Person
        {
            Id = Guid.NewGuid(), FirstName = first, LastName = "Test", FamilyId = _familyId,
            BirthDate = born is { } y ? new DateOnly(y, 6, 1) : null, DeathDate = died, CreatedAt = DateTime.UtcNow,
        };
        ctx.People.Add(p);
        await ctx.SaveChangesAsync();
        return p.Id;
    }

    private async Task<Relationship> LinkAsync(Guid a, Guid b, RelationshipType type, bool ended = false,
        DateOnly? endDate = null, DateTime? createdAt = null)
    {
        await using var ctx = _db.CreateDbContext();
        if (type != RelationshipType.Parent && a.CompareTo(b) > 0) (a, b) = (b, a);
        var rel = new Relationship
        {
            Id = Guid.NewGuid(), PersonAId = a, PersonBId = b, Type = type, IsEnded = ended || endDate != null,
            EndDate = endDate, CreatedAt = createdAt ?? DateTime.UtcNow,
        };
        ctx.Relationships.Add(rel);
        await ctx.SaveChangesAsync();
        return rel;
    }

    private async Task<PersonUpsertDto> DtoOfAsync(Guid id)
    {
        var p = (await People().GetByIdAsync(id)).Data!;
        return new PersonUpsertDto
        {
            FirstName = p.FirstName, LastName = p.LastName, BirthDate = p.BirthDate, DeathDate = p.DeathDate,
            Gender = p.Gender, ParentIds = [.. p.ParentIds ?? []], ChildIds = [.. p.ChildIds ?? []],
            SpouseIds = [.. p.SpouseIds ?? []], FormerSpouseIds = [.. p.FormerSpouseIds ?? []], SiblingIds = [.. p.SiblingIds ?? []],
        };
    }

    private async Task<List<Relationship>> RelationshipsAsync()
    {
        await using var ctx = _db.CreateDbContext();
        return await ctx.Relationships.IgnoreQueryFilters().AsNoTracking().ToListAsync();
    }

    // ── One current marriage at a time ─────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_MarriedPersonGivenASecondCurrentSpouse_IsRefused()
    {
        var mae = await SeedAsync("Mae");
        var nate = await SeedAsync("Nate");
        var iris = await SeedAsync("Iris");
        await LinkAsync(mae, nate, RelationshipType.Spouse);

        var dto = await DtoOfAsync(mae);
        dto.SpouseIds.Add(iris);
        var result = await People().UpdateAsync(mae, dto);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("can't be married to");
    }

    [Fact]
    public async Task UpdateAsync_SpouseOfSomeoneWithTwoCurrentMarriages_CanStillBeEdited()
    {
        // Saved before the rule (Florence, with Bud and Harvey both current). Every current
        // spouse used to be re-judged on every save, so neither husband could be edited.
        var florence = await SeedAsync("Florence");
        var bud = await SeedAsync("Bud");
        var harvey = await SeedAsync("Harvey");
        await LinkAsync(florence, bud, RelationshipType.Spouse);
        await LinkAsync(florence, harvey, RelationshipType.Spouse);

        var dto = await DtoOfAsync(bud);
        dto.BiographyNotes = "Served in the navy.";
        var result = await People().UpdateAsync(bud, dto);

        result.Success.Should().BeTrue(result.Message);
    }

    // ── An ancestor born after the descendant ──────────────────────────────

    [Fact]
    public async Task CreateAsync_UndatedPersonBetweenAParentBornAfterTheirChild_IsRefused()
    {
        var cora = await SeedAsync("Cora", 1980);
        var ben = await SeedAsync("Ben", 1891);

        var result = await People().CreateAsync(new PersonUpsertDto
        {
            FirstName = "Ada", LastName = "Test", ParentIds = [cora], ChildIds = [ben],
        });

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("can't be Ben Test's grandparent");
    }

    [Fact]
    public async Task UpdateAsync_UndatedPersonGivenAChildOlderThanTheirParent_IsRefused()
    {
        var cora = await SeedAsync("Cora", 1980);
        var ada = await SeedAsync("Ada");
        var ben = await SeedAsync("Ben", 1891);
        await LinkAsync(cora, ada, RelationshipType.Parent);

        var dto = await DtoOfAsync(ada);
        dto.ChildIds.Add(ben);
        var result = await People().UpdateAsync(ada, dto);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("can't be Ben Test's grandparent");
    }

    [Fact]
    public async Task UpdateAsync_ExistingImpossibleGrandparent_DoesNotBlockAnUnrelatedEdit()
    {
        var cora = await SeedAsync("Cora", 1980);
        var ada = await SeedAsync("Ada");
        var ben = await SeedAsync("Ben", 1891);
        await LinkAsync(cora, ada, RelationshipType.Parent);
        await LinkAsync(ada, ben, RelationshipType.Parent);

        var dto = await DtoOfAsync(ada);
        dto.BiographyNotes = "Kept bees.";
        var result = await People().UpdateAsync(ada, dto);

        result.Success.Should().BeTrue(result.Message);
    }

    // ── A parent who is also a spouse or sibling ───────────────────────────

    [Fact]
    public async Task CreateAsync_SamePersonAsParentAndSpouse_IsRefused()
    {
        var nate = await SeedAsync("Nate");

        var result = await People().CreateAsync(new PersonUpsertDto
        {
            FirstName = "Opal", LastName = "Test", ParentIds = [nate], SpouseIds = [nate],
        });

        result.Success.Should().BeFalse();
        result.Message.Should().Be("Nate Test can't be both Opal Test's parent and Opal Test's spouse.");
    }

    [Fact]
    public async Task RelationshipCreate_SiblingLinkBetweenParentAndChild_IsRefused()
    {
        var mae = await SeedAsync("Mae");
        var jude = await SeedAsync("Jude");
        await LinkAsync(mae, jude, RelationshipType.Parent);

        var result = await Relationships().CreateAsync(new RelationshipUpsertDto
        {
            PersonAId = jude, PersonBId = mae, Type = RelationshipType.Sibling,
        });

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("already the other's parent");
    }

    // ── A marriage's end date follows the death date it came from ──────────

    [Fact]
    public async Task UpdateAsync_ChangingTheDeathDateAMarriageEndedOn_MovesTheEndDate()
    {
        var ben = await SeedAsync("Ben", 1930, new DateOnly(2006, 6, 1));
        var opal = await SeedAsync("Opal", 1932);
        await LinkAsync(ben, opal, RelationshipType.Spouse, endDate: new DateOnly(2006, 6, 1));

        var dto = await DtoOfAsync(ben);
        dto.DeathDate = new DateOnly(2007, 3, 9);
        (await People().UpdateAsync(ben, dto)).Success.Should().BeTrue();
        (await RelationshipsAsync()).Single().EndDate.Should().Be(new DateOnly(2007, 3, 9));

        dto.DeathDate = null;
        (await People().UpdateAsync(ben, dto)).Success.Should().BeTrue();
        var marriage = (await RelationshipsAsync()).Single();
        marriage.EndDate.Should().BeNull("the death date it came from is gone, and nobody entered another");
        marriage.IsEnded.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_DeathDateAddedAfterAnUndatedDivorce_LeavesTheEndDateUnknown()
    {
        var ben = await SeedAsync("Ben", 1930);
        var opal = await SeedAsync("Opal", 1932);
        await LinkAsync(ben, opal, RelationshipType.Spouse, ended: true);

        var dto = await DtoOfAsync(ben);
        dto.DeathDate = new DateOnly(2007, 3, 9);
        (await People().UpdateAsync(ben, dto)).Success.Should().BeTrue();

        (await RelationshipsAsync()).Single().EndDate.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_DeathDateChangedWhileTheSpouseIsDeleted_TheirMarriageComesBackWithTheNewDate()
    {
        var ben = await SeedAsync("Ben", 1930, new DateOnly(1996, 8, 1));
        var gail = await SeedAsync("Gail", 1930);
        await LinkAsync(ben, gail, RelationshipType.Spouse, endDate: new DateOnly(1996, 8, 1));
        (await People().DeleteAsync(gail)).Success.Should().BeTrue();

        var dto = await DtoOfAsync(ben);
        dto.DeathDate = new DateOnly(1981, 9, 1);
        (await People().UpdateAsync(ben, dto)).Success.Should().BeTrue();
        (await People().RestoreAsync(gail)).Success.Should().BeTrue();

        (await RelationshipsAsync()).Single().EndDate.Should().Be(new DateOnly(1981, 9, 1));
    }

    // ── Restore ────────────────────────────────────────────────────────────

    [Fact]
    public async Task RestoreAsync_LinkToSomeoneStillDeleted_StaysDeletedAndComesBackWithThem()
    {
        var leon = await SeedAsync("Leon");
        var nate = await SeedAsync("Nate");
        await LinkAsync(leon, nate, RelationshipType.Sibling);
        (await People().DeleteAsync(leon)).Success.Should().BeTrue();
        (await People().DeleteAsync(nate)).Success.Should().BeTrue();

        (await People().RestoreAsync(leon)).Success.Should().BeTrue();
        (await RelationshipsAsync()).Single().DeletedAt.Should().NotBeNull("Nate is still deleted");

        (await People().RestoreAsync(nate)).Success.Should().BeTrue();
        (await RelationshipsAsync()).Single().DeletedAt.Should().BeNull();
        (await DtoOfAsync(nate)).SiblingIds.Should().Equal(leon);
    }

    [Fact]
    public async Task RestoreAsync_ParentLinkMadeImpossibleWhileDeleted_IsRefused()
    {
        var cora = await SeedAsync("Cora");
        var gail = await SeedAsync("Gail", 1948);
        await LinkAsync(cora, gail, RelationshipType.Parent);
        (await People().DeleteAsync(gail)).Success.Should().BeTrue();

        var dto = await DtoOfAsync(cora);
        dto.BirthDate = new DateOnly(1964, 4, 14);
        (await People().UpdateAsync(cora, dto)).Success.Should().BeTrue("Gail is deleted, so there is no link to judge");

        var result = await People().RestoreAsync(gail);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("can't be Gail Test's parent");
    }

    [Fact]
    public async Task RestoreAsync_SpouseRemarriedMeanwhile_MarriageComesBackEndedAndSaysSo()
    {
        var opal = await SeedAsync("Opal");
        var elsa = await SeedAsync("Elsa");
        var hugo = await SeedAsync("Hugo");
        await LinkAsync(opal, elsa, RelationshipType.Spouse);
        (await People().DeleteAsync(opal)).Success.Should().BeTrue();
        await LinkAsync(elsa, hugo, RelationshipType.Spouse);

        var result = await People().RestoreAsync(opal);

        result.Success.Should().BeTrue(result.Message);
        result.Message.Should().Contain("Elsa Test is now married to Hugo Test");
        (await DtoOfAsync(opal)).FormerSpouseIds.Should().Equal(elsa);
        (await DtoOfAsync(elsa)).SpouseIds.Should().Equal(hugo);
    }

    [Fact]
    public async Task RestoreAsync_TwoMarriagesComingBackAtOnce_OnlyTheNewerStaysCurrent()
    {
        var nate = await SeedAsync("Nate");
        var leon = await SeedAsync("Leon");
        var mae = await SeedAsync("Mae");
        await LinkAsync(nate, leon, RelationshipType.Spouse, createdAt: DateTime.UtcNow.AddDays(-2));
        (await People().DeleteAsync(leon)).Success.Should().BeTrue();
        await LinkAsync(nate, mae, RelationshipType.Spouse, createdAt: DateTime.UtcNow.AddDays(-1));
        (await People().DeleteAsync(nate)).Success.Should().BeTrue();
        (await People().RestoreAsync(leon)).Success.Should().BeTrue();

        var result = await People().RestoreAsync(nate);

        result.Success.Should().BeTrue(result.Message);
        var restored = await DtoOfAsync(nate);
        restored.SpouseIds.Should().Equal(mae);
        restored.FormerSpouseIds.Should().Equal(leon);
    }
}
