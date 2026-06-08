using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace FamilyTree.Web.Services;

public class DevAuthOptions : AuthenticationSchemeOptions
{
    public string Email { get; set; } = "dev@arborkin.local";
    public string DisplayName { get; set; } = "Dev User";
    public string UserId { get; set; } = "00000000-0000-0000-0000-000000000001";
    public List<string> Roles { get; set; } = ["Admin", "Member"];
}

public class DevAuthHandler(
    IOptionsMonitor<DevAuthOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<DevAuthOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Options.UserId),
            new(ClaimTypes.Name, Options.Email),
            new(ClaimTypes.Email, Options.Email),
            new("DisplayName", Options.DisplayName),
        };

        foreach (var role in Options.Roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
