using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Web.Services;
using FluentAssertions;
using Xunit;

namespace FamilyTree.Web.Tests.ServiceTests;

public class TreeStatsTests
{
    private static PersonDto P(string name) => new() { Id = Guid.NewGuid(), FirstName = name, LastName = "T" };

    private static void ParentOf(PersonDto parent, PersonDto child)
    {
        (parent.ChildIds ??= []).Add(child.Id);
        (child.ParentIds ??= []).Add(parent.Id);
    }

    [Fact]
    public void NoPeople_IsZero() =>
        TreeStats.ComputeGenerations([]).Should().Be(0);

    [Fact]
    public void OnePerson_IsOneGeneration() =>
        TreeStats.ComputeGenerations([P("a")]).Should().Be(1);

    [Fact]
    public void StraightLine_CountsEveryGeneration()
    {
        var a = P("a"); var b = P("b"); var c = P("c"); var d = P("d");
        ParentOf(a, b); ParentOf(b, c); ParentOf(c, d);

        TreeStats.ComputeGenerations([a, b, c, d]).Should().Be(4);
    }

    // The live tree showed 3 for a family that is four deep. One parent of the grandchild comes from
    // a long line (great-grandparent -> grandparent -> parent), the other married in with no parents on
    // the tree. The married-in parent counts as generation 1, so a breadth-first walk reached the
    // grandchild through them first, at depth 2, and never went back to count the longer line.
    [Fact]
    [Trait("Category", "Regression")]
    public void MarriedInParentWithNoAncestors_DoesNotShortenTheLongLine()
    {
        var greatGrandparent = P("gg"); var grandparent = P("g"); var parent = P("p");
        var marriedIn = P("in"); var child = P("c");
        ParentOf(greatGrandparent, grandparent);
        ParentOf(grandparent, parent);
        ParentOf(parent, child);
        ParentOf(marriedIn, child);

        TreeStats.ComputeGenerations([greatGrandparent, grandparent, parent, marriedIn, child])
            .Should().Be(4);
    }

    [Fact]
    public void ParentCycle_DoesNotHang()
    {
        var a = P("a"); var b = P("b");
        ParentOf(a, b); ParentOf(b, a);

        var act = () => TreeStats.ComputeGenerations([a, b]);

        act.Should().NotThrow();
    }
}
