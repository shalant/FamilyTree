using System.Security.Claims;
using FamilyTree.Core.Models;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace FamilyTree.Web.Services;

/// <summary>
/// Re-checks a signed-in user's security stamp from inside an open Blazor circuit (S7,
/// docs/security.md, 2026-10-25). The cookie's own stamp check only runs on HTTP requests,
/// and a circuit can stay open for hours on one connection. When the stamp no longer
/// matches (password reset, role change) or the account is gone, the circuit's auth state
/// becomes anonymous, so <c>AuthorizeRouteView</c> stops rendering protected pages and
/// <c>Home</c> shows the sign-in card. Same pattern as the .NET Identity template's
/// <c>IdentityRevalidatingAuthenticationStateProvider</c>.
/// </summary>
public sealed class SecurityStampRevalidatingAuthStateProvider(
    ILoggerFactory loggerFactory,
    IServiceScopeFactory scopeFactory,
    IOptions<IdentityOptions> options,
    IOptions<SecurityStampValidatorOptions> stampOptions)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    // Same interval as the cookie check (Auth:SessionCheckInterval), but never a tight
    // loop: the E2E suite sets zero so the cookie is checked on every request.
    protected override TimeSpan RevalidationInterval =>
        stampOptions.Value.ValidationInterval < TimeSpan.FromSeconds(10)
            ? TimeSpan.FromSeconds(10)
            : stampOptions.Value.ValidationInterval;

    protected override async Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        // The circuit's own scope may be busy; use a fresh one for the database read.
        await using var scope = scopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        return await StampStillMatchesAsync(userManager, authenticationState.User, options.Value);
    }

    internal static async Task<bool> StampStillMatchesAsync(
        UserManager<AppUser> userManager, ClaimsPrincipal principal, IdentityOptions options)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null) return false;
        if (!userManager.SupportsUserSecurityStamp) return true;

        var principalStamp = principal.FindFirstValue(options.ClaimsIdentity.SecurityStampClaimType);
        var userStamp = await userManager.GetSecurityStampAsync(user);
        return principalStamp == userStamp;
    }
}
