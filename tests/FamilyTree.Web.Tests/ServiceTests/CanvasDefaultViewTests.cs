using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Web.Services;
using FluentAssertions;
using Xunit;

namespace FamilyTree.Web.Tests.ServiceTests;

// UX/UI review P7 (docs/uxui/, 2026-11-15): on a phone the first view was zoomed in until the parents were
// cut off, because the default zoom fitted only the height of the family. The canvas now also gets the
// horizontal extent of the focus person's immediate family.
public class CanvasDefaultViewTests
{
    private static PersonDto P(string name) => new() { Id = Guid.NewGuid(), FirstName = name, LastName = "T" };

    private static FamilyTreeLayoutEngine.LayoutNode Node(PersonDto p, int x, int y, int depth, int size = 60, bool focus = false) =>
        new(p, x, y, depth, focus, size);

    [Fact]
    public void Horizontal_bounds_cover_the_parents_partner_and_children_only()
    {
        var focus = P("focus"); var partner = P("partner"); var mum = P("mum"); var dad = P("dad");
        var child = P("child"); var uncle = P("uncle");
        focus.ParentIds = [mum.Id, dad.Id];
        focus.SpouseIds = [partner.Id];
        focus.ChildIds = [child.Id];

        var layout = new FamilyTreeLayout(
            [
                Node(focus, 500, 300, 0, size: 80, focus: true),
                Node(partner, 700, 300, 0),
                Node(mum, 400, 100, 1),
                Node(dad, 600, 100, 1),
                Node(uncle, 2000, 100, 1),   // same row as the parents, far away, not the focus's family
                Node(child, 550, 500, -1),
            ],
            [], [], [], [], 2400, 800, FocusDepth: 0);

        var view = CanvasDefaultView.Compute(layout);

        view.LeftX.Should().Be(370);    // mum: 400 - 60/2
        view.RightX.Should().Be(730);   // partner: 700 + 60/2; the uncle at 2000 is not counted
    }

    [Fact]
    public void Vertical_limits_are_unchanged_and_still_use_the_whole_generation_rows()
    {
        var focus = P("focus"); var uncle = P("uncle"); var child = P("child");
        var layout = new FamilyTreeLayout(
            [
                Node(focus, 500, 300, 0, size: 80, focus: true),
                Node(uncle, 2000, 100, 1),
                Node(child, 550, 500, -1),
            ],
            [], [], [], [], 2400, 800, FocusDepth: 0);

        var view = CanvasDefaultView.Compute(layout);

        view.TopY.Should().Be(100 - 30 - 30);    // top of the parents' row, minus 30
        view.BotY.Should().Be(500 + 30 + 30);    // bottom of the children's row, plus 30
        view.FocusX.Should().Be(500);
        view.FocusY.Should().Be(300);
    }

    // Seen in the invitee journey on a phone: a parent and child with no dates are not laid out one
    // generation apart as far as FocusDepth is concerned, so the parents' row was empty and only the focus
    // person was framed; the parent was cut off under the app bar.
    [Fact]
    [Trait("Category", "Regression")]
    public void A_parent_not_in_the_row_above_is_still_inside_the_vertical_band()
    {
        var focus = P("focus"); var parent = P("parent");
        focus.ParentIds = [parent.Id];
        parent.ChildIds = [focus.Id];

        var layout = new FamilyTreeLayout(
            [
                Node(parent, 300, 50, 0),                          // same Depth as the focus person
                Node(focus, 300, 250, 0, size: 80, focus: true),
            ],
            [], [], [], [], 700, 400, FocusDepth: 0);

        var view = CanvasDefaultView.Compute(layout);

        view.TopY.Should().Be(50 - 30 - 30);     // the parent's top, minus 30 (the focus alone would give 180)
        view.BotY.Should().Be(250 + 40 + 30);    // the focus person's bottom, plus 30
    }

    [Fact]
    public void No_focus_person_gives_no_horizontal_bounds()
    {
        var layout = new FamilyTreeLayout([], [], [], [], [], 900, 600, FocusDepth: 0);

        var view = CanvasDefaultView.Compute(layout);

        view.LeftX.Should().BeNull();
        view.RightX.Should().BeNull();
    }
}
