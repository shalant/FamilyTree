using Bunit;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Web.Modules.Components;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

// Canvas pass 2026-11-17 (docs/uxui/): placeholders that don't vanish, life spans that say who is
// living, a full-name tooltip, a keyboard-reachable node, and an add-photo invitation.
public class PersonNodeCanvasPolishTests : ComponentTestBase
{
    private static PersonDto Person(string first = "Marc", string last = "Rosenberg", int? born = 1948, int? died = null, string? photo = null) => new()
    {
        Id = Guid.NewGuid(),
        FirstName = first,
        LastName = last,
        BirthDate = born is null ? null : new DateOnly(born.Value, 6, 1),
        DeathDate = died is null ? null : new DateOnly(died.Value, 3, 2),
        ProfilePhotoUrl = photo,
    };

    private IRenderedComponent<PersonNode> Render(PersonDto p, bool focus = false, Action<PersonDto>? onClick = null) =>
        RenderComponent<PersonNode>(c => c
            .Add(x => x.Person, p).Add(x => x.Left, 10).Add(x => x.Top, 10).Add(x => x.Size, 60)
            .Add(x => x.RelativeDepth, 1).Add(x => x.IsFocus, focus)
            .Add(x => x.OnClick, Microsoft.AspNetCore.Components.EventCallback.Factory.Create<PersonDto>(this, d => onClick?.Invoke(d))));

    [Theory]
    [InlineData(1978, null, "1978")]
    [InlineData(1930, 2005, "1930–2005")]
    [InlineData(null, 2005, "d. 2005")]
    [InlineData(null, null, "")]
    public void LifeSpan_SaysWhoHasDied(int? born, int? died, string expected) =>
        PersonNode.LifeSpan(Person(born: born, died: died)).Should().Be(expected);

    [Fact]
    public void DeceasedPerson_ShowsTheirLifeSpanUnderTheName()
    {
        var cut = Render(Person(born: 1930, died: 2005));

        cut.Find(".person-label--year").TextContent.Trim().Should().Be("1930–2005");
        cut.Find(".person-label--year").ClassList.Should().Contain("person-label--deceased");
    }

    [Fact]
    public void LivingPerson_ShowsOnlyTheBirthYear()
    {
        var cut = Render(Person(born: 1978));

        cut.Find(".person-label--year").TextContent.Trim().Should().Be("1978");
        cut.Find(".person-label--year").ClassList.Should().NotContain("person-label--deceased");
    }

    [Fact]
    public void Node_HasAFullNameTooltip_AndIsAKeyboardReachableButton()
    {
        var cut = Render(Person("Marc", "Rosenberg", 1930, 2005));
        var node = cut.Find(".person-node");

        node.GetAttribute("title").Should().Be("Marc Rosenberg, 1930–2005");
        node.GetAttribute("role").Should().Be("button");
        node.GetAttribute("tabindex").Should().Be("0");
        node.GetAttribute("aria-label").Should().Be("Marc Rosenberg, 1930–2005");
    }

    [Fact]
    public void EnterAndSpace_SelectThePerson_LikeAClick()
    {
        var selected = new List<PersonDto>();
        var p = Person();
        var cut = Render(p, onClick: selected.Add);

        cut.Find(".person-node").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.Find(".person-node").KeyDown(new KeyboardEventArgs { Key = " " });
        cut.Find(".person-node").KeyDown(new KeyboardEventArgs { Key = "a" });

        selected.Should().HaveCount(2).And.OnlyContain(x => x.Id == p.Id);
    }

    [Fact]
    public void PersonWithNoPhoto_IsASolidTintedDisc_NotAGhost()
    {
        var cut = Render(Person());
        var node = cut.Find(".person-node");

        node.ClassList.Should().Contain("person-node--no-photo");
        node.GetAttribute("style").Should().Contain("background:var(--ft-green-200)");
        node.GetAttribute("style").Should().NotContain("rgba(255, 255, 255, 0.45)");
        cut.Find(".person-node > span").GetAttribute("style").Should().Contain("var(--ft-green-800)");
    }

    [Fact]
    public void PersonWithAPhoto_KeepsTheGlassStyle_AndNoAddPhotoBadge()
    {
        var cut = Render(Person(photo: "/media/a.jpg"), focus: true);

        cut.Find(".person-node").ClassList.Should().NotContain("person-node--no-photo");
        cut.FindAll(".person-add-photo").Should().BeEmpty();
    }

    [Fact]
    public void NoPhotoAndFocused_OffersAnAddPhotoLink_ToThePhotoPage()
    {
        var p = Person();
        var cut = Render(p, focus: true);

        cut.Find("a.person-add-photo").GetAttribute("href").Should().Be($"/people/{p.Id}/media");
    }

    [Fact]
    public void NoPhotoAndNotFocused_ShowsTheBadgeOnlyWhileHovered()
    {
        var cut = Render(Person());

        cut.FindAll(".person-add-photo").Should().BeEmpty();
        cut.Find(".person-node").TriggerEvent("onmouseenter", new MouseEventArgs());
        cut.FindAll(".person-add-photo").Should().HaveCount(1);
        cut.Find(".person-node").TriggerEvent("onmouseleave", new MouseEventArgs());
        cut.FindAll(".person-add-photo").Should().BeEmpty();
    }
}
