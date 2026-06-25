using FamilyTree.Shared.DTOs;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Shared.Enums;
using FamilyTree.Web.Services;
using FluentAssertions;
using Xunit;

namespace FamilyTree.Web.Tests;

// Locks in the ADR 002 generation-row behavior: couples share a row (flat marriage
// line) and children sit exactly one generation below their parents (clean buses).
public class FamilyTreeLayoutEngineTests
{
    private readonly FamilyTreeLayoutEngine _engine = new();

    private static PersonDto P(Guid id, string first, int birthYear,
        List<Guid>? parents = null, List<Guid>? children = null,
        List<Guid>? spouses = null, List<Guid>? formerSpouses = null,
        Gender? gender = null) => new()
    {
        Id              = id,
        FirstName       = first,
        LastName        = "Test",
        BirthDate       = new DateOnly(birthYear, 1, 1),
        ParentIds       = parents ?? [],
        ChildIds        = children ?? [],
        SpouseIds       = spouses ?? [],
        FormerSpouseIds = formerSpouses ?? [],
        Gender          = gender,
    };

    [Fact]
    public void SpousesShareARow_AndChildrenSitOneGenerationBelow()
    {
        // Dad + Mom (a couple) with one child Kid.
        var dad = Guid.NewGuid();
        var mom = Guid.NewGuid();
        var kid = Guid.NewGuid();

        var people = new List<PersonDto>
        {
            P(dad, "Dad", 1950, children: [kid], spouses: [mom]),
            P(mom, "Mom", 1952, children: [kid], spouses: [dad]),
            P(kid, "Kid", 1980, parents: [dad, mom]),
        };
        var couples = new List<CoupleDto>
        {
            new() { PersonAId = dad, PersonBId = mom, ChildIds = [kid] },
        };

        var layout = _engine.ComputeLayout(people, couples, dad);

        var dadNode = layout.Nodes.Single(n => n.Person.Id == dad);
        var momNode = layout.Nodes.Single(n => n.Person.Id == mom);
        var kidNode = layout.Nodes.Single(n => n.Person.Id == kid);

        // Couple flat: same Y despite different birth years (would slant in timeline mode).
        momNode.Y.Should().Be(dadNode.Y);

        // Child exactly one generation below the parents.
        (kidNode.Y - dadNode.Y).Should().BePositive();
        kidNode.Depth.Should().Be(dadNode.Depth - 1);
    }

    [Fact]
    public void GenderBasedPlacement_HusbandLeft_WifeRight()
    {
        var husband = Guid.NewGuid();
        var wife    = Guid.NewGuid();

        var people = new List<PersonDto>
        {
            P(husband, "Bob",  1950, gender: Gender.Male,   spouses: [wife]),
            P(wife,    "Sue",  1952, gender: Gender.Female, spouses: [husband]),
        };
        var couples = new List<CoupleDto>
        {
            // Deliberately put wife as PersonA (the "wrong" order) to prove swap works.
            new() { PersonAId = wife, PersonBId = husband, ChildIds = [] },
        };

        var layout = _engine.ComputeLayout(people, couples, husband);

        var husbandNode = layout.Nodes.Single(n => n.Person.Id == husband);
        var wifeNode    = layout.Nodes.Single(n => n.Person.Id == wife);

        husbandNode.X.Should().BeLessThan(wifeNode.X, "husband should be to the left of his wife");
    }

    [Fact]
    public void RemarriageSatellite_FlanksPrimary_AnchoredPersonInMiddle()
    {
        // Florence (female) married Bud (died), then Harvey. We want:
        //   [Bud] ─❤─ [Florence] ─❤─ [Harvey]
        var bud     = Guid.NewGuid();
        var florence = Guid.NewGuid();
        var harvey  = Guid.NewGuid();
        var marc    = Guid.NewGuid();  // child of Bud+Florence

        var people = new List<PersonDto>
        {
            P(bud,      "Bud",      1922, gender: Gender.Male,   children: [marc],
                formerSpouses: [florence]),
            P(florence, "Florence", 1925, gender: Gender.Female, children: [marc],
                formerSpouses: [bud], spouses: [harvey]),
            P(harvey,   "Harvey",   1925, gender: Gender.Male,   spouses: [florence]),
            P(marc,     "Marc",     1948, parents: [bud, florence]),
        };
        var couples = new List<CoupleDto>
        {
            new() { PersonAId = bud, PersonBId = florence, ChildIds = [marc], IsFormer = true },
            new() { PersonAId = florence, PersonBId = harvey, ChildIds = [] },
        };

        var layout = _engine.ComputeLayout(people, couples, marc);

        var budNode      = layout.Nodes.Single(n => n.Person.Id == bud);
        var florenceNode = layout.Nodes.Single(n => n.Person.Id == florence);
        var harveyNode   = layout.Nodes.Single(n => n.Person.Id == harvey);

        // Florence should be between her two husbands.
        budNode.X.Should().BeLessThan(florenceNode.X,     "Bud left of Florence");
        florenceNode.X.Should().BeLessThan(harveyNode.X,  "Florence left of Harvey");
    }

    [Fact]
    public void MultipleSpouses_DoNotOverlap_WithinTheRow()
    {
        // Mitchell married Toby (with a child) and Linda (childless) — the real case
        // that overlapped. After layout, no two nodes in Mitchell's row may collide.
        var mitchell = Guid.NewGuid();
        var toby     = Guid.NewGuid();
        var linda    = Guid.NewGuid();
        var neal     = Guid.NewGuid();

        var people = new List<PersonDto>
        {
            P(mitchell, "Mitchell", 1944, children: [neal], spouses: [toby, linda]),
            P(toby,     "Toby",     1947, children: [neal], spouses: [mitchell]),
            P(linda,    "Linda",    1943, spouses: [mitchell]),
            P(neal,     "Neal",     1972, parents: [mitchell, toby]),
        };
        var couples = new List<CoupleDto>
        {
            new() { PersonAId = mitchell, PersonBId = toby,  ChildIds = [neal] },
            new() { PersonAId = mitchell, PersonBId = linda, ChildIds = [] },
        };

        var layout = _engine.ComputeLayout(people, couples, mitchell);

        // No two nodes sharing a row may sit within the collision gap of each other.
        foreach (var rowGroup in layout.Nodes.GroupBy(n => n.Y))
        {
            var xs = rowGroup.Select(n => n.X).OrderBy(x => x).ToList();
            for (int i = 1; i < xs.Count; i++)
                (xs[i] - xs[i - 1]).Should().BeGreaterThanOrEqualTo(100,
                    "nodes in the same generation must not overlap");
        }
    }

    [Fact]
    public void SatelliteSpouse_IsPlacedAdjacent_NotAcrossTheCanvas()
    {
        // Mom is anchored by her first marriage + child; Step is a childless second
        // spouse with no other ties — must be pulled next to Mom, not parked far away.
        var dad  = Guid.NewGuid();
        var mom  = Guid.NewGuid();
        var kid  = Guid.NewGuid();
        var step = Guid.NewGuid();

        var people = new List<PersonDto>
        {
            P(dad,  "Dad",  1950, children: [kid], spouses: [mom]),
            P(mom,  "Mom",  1952, children: [kid], spouses: [dad, step]),
            P(kid,  "Kid",  1980, parents: [dad, mom]),
            P(step, "Step", 1951, spouses: [mom]),
        };
        var couples = new List<CoupleDto>
        {
            new() { PersonAId = dad, PersonBId = mom, ChildIds = [kid] },
            new() { PersonAId = mom, PersonBId = step, ChildIds = [] },   // childless second marriage
        };

        var layout = _engine.ComputeLayout(people, couples, dad);

        var momNode  = layout.Nodes.Single(n => n.Person.Id == mom);
        var stepNode = layout.Nodes.Single(n => n.Person.Id == step);

        // Adjacent on the same row, not flung to a distant slot.
        stepNode.Y.Should().Be(momNode.Y);
        Math.Abs(stepNode.X - momNode.X).Should().BeLessThan(400);
    }
}
