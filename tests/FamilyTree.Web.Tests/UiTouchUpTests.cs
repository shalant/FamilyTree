using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Web.Modules.Components;
using FluentAssertions;
using Xunit;

namespace FamilyTree.Web.Tests;

// UX/UI review B2 and T1-T6 (docs/uxui/, 2026-11-24).
public class UiTouchUpTests
{
    private static string Web()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "FamilyTree.Web")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src", "FamilyTree.Web");
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Web(), .. parts]));

    [Fact]
    [Trait("Category", "Regression")]
    public void FactsBanner_NeverQuotesAPersonsNotes()
    {
        var banner = new FamilyFactsBanner
        {
#pragma warning disable BL0005 // set directly: only the fact list is under test
            People = [new PersonDto { Id = Guid.NewGuid(), FirstName = "Ada", LastName = "Example", BiographyNotes = "A private memory." }]
#pragma warning restore BL0005
        };

        var facts = (List<string>)typeof(FamilyFactsBanner)
            .GetMethod("BuildFacts", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(banner, null)!;

        facts.Should().NotBeEmpty();
        facts.Should().NotContain(f => f.Contains("A private memory"));
    }

    [Theory]
    [InlineData(0, "#ffffff")]
    [InlineData(1, "#1a1d21")]
    public void MutedText_MeetsAaContrast(int themeBlock, string background)
    {
        var values = Regex.Matches(Read("wwwroot", "css", "app.css"), @"--ft-text-muted:\s*([^;]+);")
            .Select(m => m.Groups[1].Value.Trim())
            .ToList();

        values.Should().HaveCount(2, "one for light and one for dark");
        Contrast(values[themeBlock], background).Should().BeGreaterThanOrEqualTo(4.5);
    }

    [Fact]
    public void SmallPolish_StaysInPlace()
    {
        var css = Read("wwwroot", "css", "app.css");

        Regex.IsMatch(css, @"\.ft-loading-text\s*\{[^}]*text-align:\s*center").Should().BeTrue();
        Regex.IsMatch(css, @"\.ft-stories-page\s*\{[^}]*max-width").Should().BeTrue();
        Regex.IsMatch(css, @"\[data-theme=""dark""\] \.ft-form-page\s*\{[^}]*var\(--ft-surface\)").Should().BeTrue();
        Read("Modules", "Pages", "Stories.razor").Should().Contain("ft-stories-page");
        Read("Modules", "Pages", "People.razor").Should().Contain("_rowsPerPage = 25");
        Read("Modules", "Admin", "Pages", "Admin.razor").Should().Contain("ArborKin system management");
    }

    private static double Contrast(string a, string b)
    {
        var (x, y) = (Luminance(a), Luminance(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    private static double Luminance(string hex)
    {
        double Channel(int at)
        {
            var c = int.Parse(hex.AsSpan(at, 2), NumberStyles.HexNumber) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
    }
}
