using Bunit;
using FamilyTree.Web.Modules.Components;
using FluentAssertions;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

// UX/UI review #10 (docs/uxui/, 2026-11-18): short pages ended abruptly with blank screen and the
// only way back to the tree was the app bar's icon row. Every ordinary page now ends with this.
public class PageFooterTests : ComponentTestBase
{
    [Fact]
    public void Footer_OffersAWayBackToTheTree()
    {
        var cut = RenderComponent<PageFooter>();

        var back = cut.Find("a.ft-page-footer__back");
        back.GetAttribute("href").Should().Be("/");
        back.TextContent.Should().Contain("Back to the family tree");
        cut.Find(".ft-page-footer__tag").TextContent.Should().Contain("ArborKin");
    }

    // The tree and the sign-in pages must not get it: the tree is a full-screen canvas and the auth
    // pages deliberately have no app navigation (see AuthLayout).
    [Fact]
    public void OnlyMainLayoutRendersTheFooter()
    {
        var root = FindRepoRoot();
        var layouts = Directory.GetFiles(Path.Combine(root, "src", "FamilyTree.Web", "Layout"), "*.razor");

        layouts.Where(f => File.ReadAllText(f).Contains("<PageFooter"))
            .Select(Path.GetFileName)
            .Should().BeEquivalentTo(["MainLayout.razor"]);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FamilyTree.sln")) &&
               !Directory.Exists(Path.Combine(dir.FullName, "src", "FamilyTree.Web")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root not found");
    }
}
