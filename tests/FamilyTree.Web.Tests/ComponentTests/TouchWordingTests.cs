using Bunit;
using FamilyTree.Web.Modules.Components;
using FluentAssertions;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

// Phone walkthrough, 2026-11-20: the photo drop zone said "Drop photos here or click to browse" on
// a phone, which can do neither. Both phrasings are rendered; app.css shows the one that fits the
// device's primary pointer (.ft-on-pointer / .ft-on-touch under (hover: none) and (pointer: coarse)).
public class TouchWordingTests : ComponentTestBase
{
    [Fact]
    public void UploadZone_RendersSeparateWordingForTouchScreens()
    {
        var cut = RenderComponent<MediaUploadZone>(p => p
            .Add(z => z.Label, "Drop photos here or click to browse")
            .Add(z => z.TouchLabel, "Tap to choose photos"));

        cut.Find(".ft-on-pointer").TextContent.Should().Be("Drop photos here or click to browse");
        cut.Find(".ft-on-touch").TextContent.Should().Be("Tap to choose photos");
    }

    [Fact]
    public void UploadZone_WithNoTouchLabel_ShowsTheLabelEverywhere()
    {
        var cut = RenderComponent<MediaUploadZone>(p => p
            .Add(z => z.Label, "Drop your .ged file here")
            .Add(z => z.TouchLabel, ""));

        cut.FindAll(".ft-on-pointer, .ft-on-touch").Should().BeEmpty();
        cut.Markup.Should().Contain("Drop your .ged file here");
    }

    [Fact]
    public void Stylesheet_HidesThePointerWordingOnTouchScreens()
    {
        var css = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "FamilyTree.Web", "wwwroot", "css", "app.css"));

        css.Should().Contain(".ft-on-touch { display: none; }");
        var touchBlock = css.IndexOf("@media (hover: none) and (pointer: coarse)", StringComparison.Ordinal);
        touchBlock.Should().BePositive();
        css[touchBlock..].Should().Contain(".ft-on-pointer { display: none; }");
    }

    // The photo pages a relative actually uses: both upload zones on a person's media page, and the
    // Add/Edit Person form's own photo box.
    [Fact]
    public void PhotoPages_GiveTouchScreensTheirOwnWording()
    {
        var modules = Path.Combine(FindRepoRoot(), "src", "FamilyTree.Web", "Modules");
        var media = File.ReadAllText(Path.Combine(modules, "Pages", "PersonMedia.razor"));
        var zones = media.Split("<MediaUploadZone").Skip(1).Select(z => z[..z.IndexOf('>')]).ToList();
        zones.Should().NotBeEmpty();
        zones.Should().OnlyContain(z => z.Contains("TouchLabel="));

        File.ReadAllText(Path.Combine(modules, "Components", "PersonForm.razor"))
            .Should().Contain("<span class=\"ft-on-touch\">Tap to choose</span>");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FamilyTree.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found");
    }
}
