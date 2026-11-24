using System.Text.RegularExpressions;
using FamilyTree.Web.Layout;
using FluentAssertions;
using MudBlazor;
using MudBlazor.Utilities;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

// UX/UI review #9 (2026-11-24): buttons for the same intent were coloured four ways (Color.Primary,
// Color.Success, inline --ft-green-600, inline --ft-green-700), and cancel links were inline
// --ft-gray-400, 3.3:1 on white. In dark mode the inline greens flip to light ones under white
// text: --ft-green-700 measured 1.65:1. One treatment per intent now (docs/uxui/, addendum
// 2026-11-24): primary = Filled + Color.Primary, secondary = Outlined + Color.Primary, a neutral
// answer = Outlined + Color.Default, dismiss = Text with the default colour, destructive =
// Color.Error. Colour comes from the theme, never from an inline style.
public class ButtonTreatmentTests
{
    // Third-party brand colours, deliberately kept: Venmo, and the share targets.
    private static readonly string[] BrandColours =
        ["#3D95CE", "#000", "#1877f2", "#25d366", "--ft-brown-"];

    [Fact]
    public void NoMudButtonSetsItsOwnColourInline()
    {
        var offenders = MudButtonTags()
            .Where(t => Regex.IsMatch(StyleOf(t.Tag), @"(^|;)\s*(background|color|border-color)\s*:", RegexOptions.IgnoreCase))
            .Where(t => !BrandColours.Any(b => StyleOf(t.Tag).Contains(b, StringComparison.OrdinalIgnoreCase)))
            .Select(t => $"{t.File}:{t.Line}");

        offenders.Should().BeEmpty("a button's colour comes from its Color parameter and the theme");
    }

    [Fact]
    public void NoMudButtonUsesColorSuccess()
    {
        // Success is the same green as Primary in this theme; two names for one intent is how the
        // drift started. (ShareDialog's "Copied" state picks it in an expression, which is fine.)
        MudButtonTags().Where(t => t.Tag.Contains("Color=\"Color.Success\""))
            .Select(t => $"{t.File}:{t.Line}")
            .Should().BeEmpty();
    }

    [Fact]
    public void EveryLayoutUsesTheSharedTheme()
    {
        var layouts = Directory.GetFiles(Path.Combine(RepoRoot(), "src", "FamilyTree.Web", "Layout"), "*.razor")
            .Where(f => File.ReadAllText(f).Contains("<MudThemeProvider"))
            .ToList();

        layouts.Should().NotBeEmpty();
        layouts.Should().OnlyContain(f => File.ReadAllText(f).Contains("ArborKinTheme.Create()"));
        layouts.Should().OnlyContain(f => !File.ReadAllText(f).Contains("new PaletteDark"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FilledPrimaryButtonText_MeetsAA_AtRestAndOnHover(bool dark)
    {
        var theme = ArborKinTheme.Create();
        Palette p = dark ? theme.PaletteDark : theme.PaletteLight;

        Contrast(p.PrimaryContrastText, p.Primary).Should().BeGreaterThanOrEqualTo(4.5);
        Contrast(p.PrimaryContrastText, p.PrimaryDarken).Should().BeGreaterThanOrEqualTo(4.5);
    }

    private static double Contrast(object a, object b)
    {
        static double Lum(MudColor c)
        {
            static double Ch(byte v)
            {
                var s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
        }
        var (x, y) = (Lum(new MudColor(a.ToString()!)), Lum(new MudColor(b.ToString()!)));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    private static string StyleOf(string tag) =>
        Regex.Match(tag, "\\bStyle=\"([^\"]*)\"").Groups[1].Value;

    private static IEnumerable<(string File, int Line, string Tag)> MudButtonTags()
    {
        var web = Path.Combine(RepoRoot(), "src", "FamilyTree.Web");
        foreach (var file in Directory.GetFiles(web, "*.razor", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                              && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, "<MudButton\\b"))
            {
                // The tag ends at the first '>' outside a quoted value: OnClick="@(() => …)" has one.
                var end = m.Index;
                var inQuote = false;
                for (; end < text.Length; end++)
                {
                    if (text[end] == '"') inQuote = !inQuote;
                    else if (text[end] == '>' && !inQuote) break;
                }
                var line = text[..m.Index].Count(c => c == '\n') + 1;
                yield return (Path.GetFileName(file), line, text[m.Index..end]);
            }
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FamilyTree.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found");
    }
}
