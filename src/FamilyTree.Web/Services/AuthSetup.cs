using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace FamilyTree.Web.Services;

/// <summary>
/// Google sign-in and the app cookie's per-request check, kept out of Program.cs so tests
/// can build the same registration.
/// </summary>
public static class AuthSetup
{
    /// <summary>
    /// Google writes its result to the external cookie, which /auth/google-callback reads
    /// and turns into an account sign-in. Left unset, Google signs in to the default scheme:
    /// <c>AddAuthentication(IdentityConstants.ApplicationScheme)</c> makes that the app's own
    /// cookie (full <c>AddIdentity</c> would have pointed it at the external one). Until
    /// 2026-10-25 every Google sign-in became an app session for the bare Google identity,
    /// invited or not, with no account behind it and so an empty tree, and the callback
    /// never found a sign-in to link or register.
    /// </summary>
    public static AuthenticationBuilder AddArborKinGoogle(
        this AuthenticationBuilder builder, string clientId, string clientSecret) =>
        builder.AddGoogle(options =>
        {
            options.ClientId     = clientId;
            options.ClientSecret = clientSecret;
            options.SignInScheme = IdentityConstants.ExternalScheme;
            // Not mapped by default; an invite is claimed, and an existing account linked,
            // by address alone, so only a verified one may (AuthService.RegisterExternalAsync).
            options.ClaimActions.MapJsonKey("email_verified", "email_verified");
        });

    /// <summary>
    /// Every request: a cookie whose user ID isn't an account ID (one of the Google-identity
    /// sessions above) is signed out at once, rather than reaching the stamp check, whose
    /// user lookup throws on an ID that isn't a GUID. Then the stamp check (S7), which runs
    /// once per <c>Auth:SessionCheckInterval</c>.
    /// </summary>
    public static async Task ValidateAppCookieAsync(CookieValidatePrincipalContext context)
    {
        if (!IsAccountPrincipal(context.Principal))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return;
        }
        await SecurityStampValidator.ValidatePrincipalAsync(context);
    }

    internal static bool IsAccountPrincipal(ClaimsPrincipal? principal) =>
        Guid.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out _);
}
