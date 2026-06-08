using FamilyTree.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace FamilyTree.Web.Services;

public class AppUserClaimsPrincipalFactory(
    UserManager<AppUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IOptions<IdentityOptions> optionsAccessor)
    : UserClaimsPrincipalFactory<AppUser, IdentityRole<Guid>>(userManager, roleManager, optionsAccessor)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        if (!string.IsNullOrWhiteSpace(user.DisplayName))
            identity.AddClaim(new Claim("DisplayName", user.DisplayName));

        if (user.IsSuperUser)
            identity.AddClaim(new Claim(ClaimTypes.Role, "SuperUser"));

        return identity;
    }
}
