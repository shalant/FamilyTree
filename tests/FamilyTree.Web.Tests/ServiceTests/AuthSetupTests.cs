using System.Security.Claims;
using FamilyTree.Web.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace FamilyTree.Web.Tests.ServiceTests;

// Found live 2026-10-25, first Google sign-in on arborkin.com: Google signed in to the
// app's own cookie (the default scheme), so the phone showed "Doug Rosenberg" (the Google
// profile name), signed in with no account and an empty tree, and the callback never saw
// a sign-in to link. Anyone with a Google account got such a session.
public class AuthSetupTests
{
    [Fact]
    [Trait("Category", "Regression")]
    public void Google_SignsInToTheExternalCookie_NotTheAppCookie()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddAuthentication(IdentityConstants.ApplicationScheme)   // as Program.cs
            .AddCookie(IdentityConstants.ApplicationScheme)
            .AddCookie(IdentityConstants.ExternalScheme)
            .AddArborKinGoogle("client-id", "client-secret");

        var google = services.BuildServiceProvider()
            .GetRequiredService<IOptionsMonitor<GoogleOptions>>()
            .Get(GoogleDefaults.AuthenticationScheme);

        google.SignInScheme.Should().Be(IdentityConstants.ExternalScheme);
    }

    // Sessions made before the fix must end on the next request, not after the stamp
    // check's five-minute interval, whose account lookup throws on a Google subject ID.
    [Fact]
    [Trait("Category", "Regression")]
    public async Task AppCookieForAGoogleIdentity_IsSignedOutOnTheNextRequest()
    {
        var auth = new RecordingAuthService();
        var context = ValidateContext(auth,
            new Claim(ClaimTypes.NameIdentifier, "109876543210987654321"),
            new Claim(ClaimTypes.Name, "Doug Rosenberg"));

        await AuthSetup.ValidateAppCookieAsync(context);

        context.Principal.Should().BeNull();
        auth.SignedOut.Should().Equal(IdentityConstants.ApplicationScheme);
    }

    [Fact]
    public void AnAccountIdIsAnAccountPrincipal()
    {
        AuthSetup.IsAccountPrincipal(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "x"))).Should().BeTrue();
        AuthSetup.IsAccountPrincipal(new ClaimsPrincipal(new ClaimsIdentity())).Should().BeFalse();
    }

    private static CookieValidatePrincipalContext ValidateContext(IAuthenticationService auth, params Claim[] claims)
    {
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(auth).BuildServiceProvider(),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, IdentityConstants.ApplicationScheme));
        return new CookieValidatePrincipalContext(
            http,
            new AuthenticationScheme(IdentityConstants.ApplicationScheme, null, typeof(CookieAuthenticationHandler)),
            new CookieAuthenticationOptions(),
            new AuthenticationTicket(principal, IdentityConstants.ApplicationScheme));
    }

    private sealed class RecordingAuthService : IAuthenticationService
    {
        public List<string?> SignedOut { get; } = [];

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            SignedOut.Add(scheme);
            return Task.CompletedTask;
        }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) => throw new NotSupportedException();
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => throw new NotSupportedException();
    }
}
