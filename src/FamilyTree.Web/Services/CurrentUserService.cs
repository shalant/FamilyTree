using System.Security.Claims;
using FamilyTree.Core.Services;
using Microsoft.AspNetCore.Http;

namespace FamilyTree.Web.Services;

public class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    public Guid? UserId
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.User
                .FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public Guid? FamilyId
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.User
                .FindFirstValue("FamilyId");
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public string? Email =>
        httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email);

    public bool IsSuperUser =>
        httpContextAccessor.HttpContext?.User.IsInRole("SuperUser") ?? false;
}
