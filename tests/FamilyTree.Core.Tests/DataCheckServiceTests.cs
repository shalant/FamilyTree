using FamilyTree.Core.Models;
using FamilyTree.Core.Services;
using FamilyTree.Core.Tests.Helpers;
using FamilyTree.Shared.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FamilyTree.Core.Tests;

// B8 (2026-10-27): DataCheckService loads one family's live tree and runs DataCheckRules
// over it. Cross-family scoping is in CrossTenantMatrixTests; this covers the rest.
public class DataCheckServiceTests
{
    private readonly TestDbContextFactory _db = new();
    private readonly Guid _familyA = Guid.NewGuid();
    private readonly Guid _familyB = Guid.NewGuid();

    public DataCheckServiceTests()
    {
        using var ctx = _db.CreateDbContext();
        ctx.Families.Add(new Family { Id = _familyA, Name = "Family A", CreatedAt = DateTime.UtcNow });
        ctx.Families.Add(new Family { Id = _familyB, Name = "Family B", CreatedAt = DateTime.UtcNow });
        ctx.SaveChanges();
    }

    private DataCheckService Svc(FakeCurrentUserService user) => new(_db, user, NullLogger<DataCheckService>.Instance);
    private FakeCurrentUserService AdminOf(Guid family) => new() { UserId = Guid.NewGuid(), FamilyId = family, IsAdmin = true };

    private Guid AddPerson(Guid family, string first, string last, int? born = null, int? died = null, bool deleted = false)
    {
        using var ctx = _db.CreateDbContext();
        var p = new Person
        {
            Id = Guid.NewGuid(), FirstName = first, LastName = last, FamilyId = family,
            BirthDate = born is { } b ? new DateOnly(b, 1, 1) : null,
            DeathDate = died is { } d ? new DateOnly(d, 1, 1) : null,
            CreatedAt = new DateTime(2020, 1, 1), DeletedAt = deleted ? DateTime.UtcNow : null,
        };
        ctx.People.Add(p);
        ctx.SaveChanges();
        return p.Id;
    }

    [Fact]
    public async Task AMember_IsRefused()
    {
        AddPerson(_familyA, "Max", "Fine", 1950, 1940);
        var r = await Svc(new FakeCurrentUserService { UserId = Guid.NewGuid(), FamilyId = _familyA }).GetFindingsAsync();
        r.Success.Should().BeFalse();
        r.Data.Should().BeNull();
    }

    [Fact]
    public async Task AnAdminWithNoFamily_IsRefused()
    {
        var r = await Svc(new FakeCurrentUserService { UserId = Guid.NewGuid(), IsAdmin = true }).GetFindingsAsync();
        r.Success.Should().BeFalse();
    }

    [Fact]
    public async Task AFamilyAdmin_GetsTheirFamilysFindingsWithNames()
    {
        var max = AddPerson(_familyA, "Max", "Fine", 1950, 1940);

        var r = await Svc(AdminOf(_familyA)).GetFindingsAsync();

        r.Success.Should().BeTrue();
        var f = r.Data.Should().ContainSingle().Subject;
        f.Rule.Should().Be("Dates");
        f.IsImpossible.Should().BeTrue();
        f.FamilyName.Should().Be("Family A");
        f.People.Should().ContainSingle().Which.Should().Match<Shared.DTOs.Admin.DataCheckPersonDto>(
            p => p.PersonId == max && p.Name == "Max Fine");
    }

    [Fact]
    public async Task SoftDeletedPeople_AreLeftOut()
    {
        AddPerson(_familyA, "Gone", "Away", 1950, 1940, deleted: true);
        var r = await Svc(AdminOf(_familyA)).GetFindingsAsync();
        r.Data.Should().BeEmpty();
    }

    // The same name in two families is two different people, even for the super-user,
    // who sees both.
    [Fact]
    public async Task TheSuperUser_SeesEveryFamily_ButNeverPairsPeopleAcrossThem()
    {
        AddPerson(_familyA, "John", "Small");
        AddPerson(_familyB, "John", "Small");
        AddPerson(_familyB, "Max", "Fine", 1950, 1940);

        var r = await Svc(new FakeCurrentUserService { UserId = Guid.NewGuid(), IsSuperUser = true }).GetFindingsAsync();

        r.Success.Should().BeTrue();
        r.Data.Should().ContainSingle().Which.FamilyName.Should().Be("Family B");
    }

    [Fact]
    public async Task ALinkToSomeoneInAnotherFamily_IsIgnored()
    {
        var kid = AddPerson(_familyA, "Kid", "Here", 1980);
        var outsider = AddPerson(_familyB, "Out", "Sider", 1990);
        using (var ctx = _db.CreateDbContext())
        {
            ctx.Relationships.Add(new Relationship
            {
                Id = Guid.NewGuid(), PersonAId = outsider, PersonBId = kid, Type = RelationshipType.Parent, CreatedAt = DateTime.UtcNow,
            });
            ctx.SaveChanges();
        }

        var r = await Svc(AdminOf(_familyA)).GetFindingsAsync();
        r.Data.Should().BeEmpty();
    }
}
