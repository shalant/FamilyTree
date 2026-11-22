using FamilyTree.Core.Services;
using FluentAssertions;
using Xunit;

namespace FamilyTree.Core.Tests;

// Phone walkthrough, 2026-11-20: on a phone the invite email's button broke before its arrow, so
// "→" sat alone on a second line of the button. The arrow is joined to the last word with a
// non-breaking space and the button doesn't wrap.
public class EmailButtonWrapTests
{
    [Fact]
    public void StoryInviteButton_KeepsItsArrowOnTheSameLine()
    {
        var html = StoryInviteEmailBuilder.Build(
            "Doug", "Rose Garden", null, 1930, 2001, null, "https://example.test/story/respond/abc");

        html.Should().Contain("Share your memory&nbsp;→");
        html.Should().NotContain("Share your memory →");
        CtaStyle(html, "Share your memory").Should().Contain("white-space:nowrap");
    }

    [Fact]
    public void UserInviteButton_KeepsItsArrowOnTheSameLine()
    {
        var html = UserInviteEmailBuilder.Build("Doug", "https://example.test/register?invite=abc", 7);

        html.Should().Contain("Create your account&nbsp;→");
        html.Should().NotContain("Create your account →");
        CtaStyle(html, "Create your account").Should().Contain("white-space:nowrap");
    }

    // The style attribute of the <a> whose text starts with the button label.
    private static string CtaStyle(string html, string label)
    {
        var textAt = html.IndexOf(label + "&nbsp;", StringComparison.Ordinal);
        textAt.Should().BePositive();
        var anchorAt = html.LastIndexOf("<a ", textAt, StringComparison.Ordinal);
        return html[anchorAt..textAt];
    }
}
