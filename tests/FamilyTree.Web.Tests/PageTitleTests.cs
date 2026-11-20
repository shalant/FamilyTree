using FluentAssertions;
using Xunit;

namespace FamilyTree.Web.Tests;

// UX/UI review #13-#15 (docs/uxui/, 2026-11-19): browser tabs and history read "Stories", "People",
// "Create account — Arborkin" and so on, with and without the product name. Every page title now
// ends in "ArborKin", spelled that way.
public class PageTitleTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "FamilyTree.Web")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found");
    }

    [Fact]
    public void EveryPageTitle_NamesArborKin()
    {
        var web = Path.Combine(RepoRoot(), "src", "FamilyTree.Web");
        var titles = Directory.EnumerateFiles(web, "*.razor", SearchOption.AllDirectories)
            .SelectMany(f => File.ReadAllLines(f)
                .Where(l => l.Contains("<PageTitle>"))
                .Select(l => (File: Path.GetFileName(f), Line: l.Trim())))
            .ToList();

        titles.Should().NotBeEmpty();
        titles.Where(t => !t.Line.Contains("ArborKin</PageTitle>") && t.Line != "<PageTitle>ArborKin</PageTitle>")
            .Should().BeEmpty("every page title ends in the product name, spelled ArborKin");
    }
}
