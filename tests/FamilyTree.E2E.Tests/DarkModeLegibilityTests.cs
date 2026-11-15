using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace FamilyTree.E2E.Tests;

// UX/UI review 2026-11-15 (docs/uxui/, D1 and D2), both found by eye on the live site in dark mode:
//  - D1: the facts banner's text used --ft-green-200, a DARK green in the inverted dark palette, on a
//    dark green banner (about 1.5:1).
//  - D2: the near-black "ArborKin" wordmark on the dark login card had no recolor, so only the leaves
//    and roots showed.
// Both measure the real stylesheet in a real browser; bUnit has no CSS. The signed-out login page is
// enough: it needs no account, and the banner is built on it from the same markup FamilyFactsBanner
// renders, because the real banner only appears on a tree with people.
[Collection("E2E-InviteOnly")]
public class DarkModeLegibilityTests(InviteOnlyAppFixture fixture) : E2eTestBase(fixture)
{
    private async Task<IPage> LoginPageAsync(string theme)
    {
        var context = await NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
            ViewportSize = new() { Width = 1280, Height = 800 },
        });
        await context.AddInitScriptAsync($"localStorage.setItem('ft-theme','{theme}')");
        var page = await context.NewPageAsync();
        await page.GotoAsync(Fixture.BaseUrl + "/");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.GetByText("Sign in").First.WaitForAsync();
        return page;
    }

    [Fact]
    public async Task DarkMode_FactsBannerText_HasReadableContrast()
    {
        var page = await LoginPageAsync("dark");

        // The banner as FamilyFactsBanner renders it, put over the page's own dark background.
        var colors = await page.EvaluateAsync<string[]>(@"() => {
            const banner = document.createElement('div');
            banner.className = 'facts-banner';
            banner.innerHTML = '<div class=""facts-banner-inner""><div class=""facts-banner-text"">A fact</div></div>';
            document.body.appendChild(banner);
            const text = banner.querySelector('.facts-banner-text');
            const bg = getComputedStyle(banner).backgroundImage;
            return [getComputedStyle(text).color, bg, getComputedStyle(document.body).backgroundColor];
        }");

        var text = ParseRgb(colors[0]);
        // The banner's background is a vertical gradient of one translucent colour (the first rgba
        // stop is the strongest); take that colour over the body background, the worst case for text.
        var bannerRgba = ParseRgba(Regex.Match(colors[1], @"rgba\([^)]*\)").Value);
        var body = ParseRgb(colors[2]);
        var bg = Composite(bannerRgba, body);

        var ratio = Contrast(text, bg);
        Assert.True(ratio >= 4.5,
            $"Dark-mode facts banner text is {ratio:0.0}:1 against its background (needs 4.5:1). " +
            $"text rgb({text[0]},{text[1]},{text[2]}), background rgb({bg[0]:0},{bg[1]:0},{bg[2]:0}).");
    }

    [Fact]
    public async Task DarkMode_LoginLogo_IsRecolouredForTheDarkCard_AndLightModeIsNot()
    {
        var dark = await LoginPageAsync("dark");
        var darkFilter = await dark.Locator(".auth-card img").First.EvaluateAsync<string>("e => getComputedStyle(e).filter");
        Assert.NotEqual("none", darkFilter);

        var light = await LoginPageAsync("light");
        var lightFilter = await light.Locator(".auth-card img").First.EvaluateAsync<string>("e => getComputedStyle(e).filter");
        Assert.Equal("none", lightFilter);
    }

    private static double[] ParseRgb(string css)
    {
        var n = Regex.Matches(css, @"[\d.]+").Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
        return [n[0], n[1], n[2]];
    }

    private static double[] ParseRgba(string css)
    {
        var n = Regex.Matches(css, @"[\d.]+").Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
        return [n[0], n[1], n[2], n.Length > 3 ? n[3] : 1];
    }

    private static double[] Composite(double[] over, double[] under)
    {
        var a = over[3];
        return [over[0] * a + under[0] * (1 - a), over[1] * a + under[1] * (1 - a), over[2] * a + under[2] * (1 - a)];
    }

    private static double Luminance(double[] c)
    {
        double F(double v) { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        return 0.2126 * F(c[0]) + 0.7152 * F(c[1]) + 0.0722 * F(c[2]);
    }

    private static double Contrast(double[] a, double[] b)
    {
        var x = Luminance(a);
        var y = Luminance(b);
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }
}
