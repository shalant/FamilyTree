using System.Reflection;
using FamilyTree.Web.Modules.Pages;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

// Regression for finding S1 in docs/security.md (2026-10-10): only /admin carried
// [Authorize], and AuthorizeRouteView only blocks pages that do, so a logged-out visitor
// could open /people/add, /stories, /dashboard, /people/{id}/media and so on (confirmed
// against a local run). _Imports.razor now applies [Authorize] to every page; a page is
// public only if it opts out with [AllowAnonymous] AND is listed here.
public class PageAuthorizationTests
{
    private static readonly HashSet<string> AnonymousRoutes =
    [
        "/",                       // renders LoginOverlay for logged-out visitors
        "/register",
        "/forgot-password",
        "/reset-password",
        "/story/respond/{Token}",  // emailed, no-login story invite
        "/about",
        "/faq",
        "/Error",
        "/not-found",
    ];

    private static IEnumerable<(Type Page, string Route)> RoutablePages() =>
        typeof(Home).Assembly.GetTypes()
            .Where(t => typeof(IComponent).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetCustomAttributes<RouteAttribute>().Select(r => (t, r.Template)));

    [Fact]
    [Trait("Category", "Regression")]
    public void EveryPageRequiresAuthenticationUnlessExplicitlyAllowListed()
    {
        var pages = RoutablePages().ToList();
        pages.Should().NotBeEmpty();

        var open = pages
            .Where(p => !AnonymousRoutes.Contains(p.Route))
            // GetCustomAttributes: /admin carries two [Authorize]s (its own Roles one + _Imports).
            .Where(p => !p.Page.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any()
                     || p.Page.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any())
            .Select(p => p.Route);

        open.Should().BeEmpty("every page outside the allow-list must require a signed-in user");
    }

    [Fact]
    public void AllowListedPagesAreActuallyAnonymous()
    {
        // Keeps the list honest: a listed page that still demands auth would lock people
        // out of sign-in, registration or password reset.
        var listed = RoutablePages().Where(p => AnonymousRoutes.Contains(p.Route)).ToList();
        listed.Select(p => p.Route).Should().BeEquivalentTo(AnonymousRoutes);
        listed.Where(p => !p.Page.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any())
            .Select(p => p.Route).Should().BeEmpty();
    }
}
