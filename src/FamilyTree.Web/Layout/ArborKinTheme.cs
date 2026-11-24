using MudBlazor;

namespace FamilyTree.Web.Layout;

/// <summary>
/// The one MudBlazor theme every layout uses (MainLayout, TreeLayout, AuthLayout). Until
/// 2026-11-24 each layout carried its own identical copy, the kind of drift UX/UI review #9 is about.
/// </summary>
/// <remarks>
/// Dark mode: the primary green (#4faa78) is light, so a filled button needs dark text. MudBlazor's
/// default contrast text is white, 2.9:1 on it (WCAG AA asks 4.5:1); #0b1f14 is 6.0:1. A filled
/// button's hover uses <c>PrimaryDarken</c>, which in dark mode is a lighter green, so the hover
/// keeps the dark text readable (7.2:1) instead of dropping to 2.7:1 on a darker one.
/// <c>ArborKinThemeTests</c> checks these ratios.
/// </remarks>
public static class ArborKinTheme
{
    public const string DarkPrimary = "#4faa78";
    public const string DarkPrimaryHover = "#63b886";
    public const string DarkOnPrimary = "#0b1f14";

    public static MudTheme Create() => new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#1e7d50",
            PrimaryDarken = "#0f472c",
            PrimaryLighten = "#4db282",
            Success = "#1e7d50",
            Background = "#ffffff",
            Surface = "#f8faf9",
            DrawerBackground = "#f8faf9",
            AppbarBackground = "#f8faf9",
            AppbarText = "#212529",
        },
        PaletteDark = new PaletteDark
        {
            Primary = DarkPrimary,
            PrimaryDarken = DarkPrimaryHover,
            PrimaryLighten = "#9dd6ba",
            PrimaryContrastText = DarkOnPrimary,
            Success = DarkPrimary,
            SuccessDarken = DarkPrimaryHover,
            SuccessContrastText = DarkOnPrimary,
            Background = "#121417",
            Surface = "#1a1d21",
            DrawerBackground = "#1a1d21",
            AppbarBackground = "#1a1d21",
            AppbarText = "#dee2e6",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = ["Inter", "system-ui", "sans-serif"] }
        }
    };
}
