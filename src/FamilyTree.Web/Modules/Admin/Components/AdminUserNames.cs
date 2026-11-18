using FamilyTree.Core.Services;

namespace FamilyTree.Web.Modules.Admin.Components;

// The Audit log and Activity tabs only hold a user's id. They used to print its first eight
// characters ("f9b335bf…"), so "who did this" meant a trip to the Users tab (UX/UI review A5,
// 2026-11-17). This turns an id into the name an admin would recognise.
public sealed class AdminUserNames
{
    private readonly Dictionary<Guid, AdminUserDto> _users;

    public AdminUserNames(IEnumerable<AdminUserDto> users) =>
        _users = users.GroupBy(u => u.Id).ToDictionary(g => g.Key, g => g.First());

    public static AdminUserNames Empty { get; } = new([]);

    /// <summary>Display name, else email, else the short id, else a dash for "no user".</summary>
    public string Label(Guid? userId)
    {
        if (userId is not { } id) return "—";
        if (_users.TryGetValue(id, out var u))
        {
            if (!string.IsNullOrWhiteSpace(u.DisplayName)) return u.DisplayName;
            if (!string.IsNullOrWhiteSpace(u.Email)) return u.Email;
        }
        return ShortId(id);
    }

    /// <summary>The full id, for a tooltip, so nothing is lost by showing a name.</summary>
    public static string ShortId(Guid id) => id.ToString()[..8] + "…";
}
