using FamilyTree.Core.Data;
using FamilyTree.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace FamilyTree.Web.Services;

public class AppUserClaimsPrincipalFactory(
    UserManager<AppUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IOptions<IdentityOptions> optionsAccessor,
    IDbContextFactory<AppDbContext> dbFactory)
    : UserClaimsPrincipalFactory<AppUser, IdentityRole<Guid>>(userManager, roleManager, optionsAccessor)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        if (!string.IsNullOrWhiteSpace(user.DisplayName))
            identity.AddClaim(new Claim("DisplayName", user.DisplayName));

        if (user.IsSuperUser)
            identity.AddClaim(new Claim(ClaimTypes.Role, "SuperUser"));

        if (user.IsAdmin)
            identity.AddClaim(new Claim(ClaimTypes.Role, "Admin"));

        // Emit FamilyId claim for regular users; super users skip this so they see all data.
        if (!user.IsSuperUser)
        {
            await using var ctx = await dbFactory.CreateDbContextAsync();
            var familyId = await ctx.UserFamilies
                .Where(uf => uf.UserId == user.Id)
                .Select(uf => (Guid?)uf.FamilyId)
                .FirstOrDefaultAsync();

            if (familyId.HasValue)
                identity.AddClaim(new Claim("FamilyId", familyId.Value.ToString()));
        }

        return identity;
    }
}
