namespace FamilyTree.Web.Services;

/// <summary>
/// Where to send someone after they sign in. A signed-out visit to a protected page is
/// challenged to <c>/?ReturnUrl=/admin</c>; sign-in honours that path, but only when it
/// is a path on this site, so a crafted link can't bounce a fresh session to another site
/// (an open redirect). Anything else falls back to the user's own place on the tree.
/// </summary>
public static class LoginRedirect
{
    /// <summary>The return path if it is safe to redirect to, else null.</summary>
    public static string? SafeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl)) return null;

        // Same test as ASP.NET Core's Url.IsLocalUrl: one leading slash, and not "//" or
        // "/\", which browsers treat as another host. "~/" isn't used by this app.
        if (returnUrl[0] != '/') return null;
        if (returnUrl.Length > 1 && (returnUrl[1] == '/' || returnUrl[1] == '\\')) return null;
        if (returnUrl.Any(char.IsControl)) return null;

        // The sign-in endpoints themselves would loop, and the bare tree is the default anyway.
        var path = returnUrl.Split('?', '#')[0];
        if (path == "/" || path.StartsWith("/auth/", StringComparison.OrdinalIgnoreCase)) return null;

        return returnUrl;
    }

    /// <summary>
    /// After a successful sign-in: the safe return path if there is one, else the user's
    /// saved focus person on the tree, else the tree.
    /// </summary>
    public static string AfterSignIn(string? returnUrl, Guid? focusPersonId) =>
        SafeReturnUrl(returnUrl)
        ?? (focusPersonId.HasValue ? $"/?focus={focusPersonId}" : "/");

    /// <summary>
    /// A sign-in error page that keeps the return path, so a second attempt still lands
    /// where the user was going.
    /// </summary>
    public static string LoginError(string code, string? returnUrl)
    {
        var safe = SafeReturnUrl(returnUrl);
        return safe == null
            ? $"/?loginError={code}"
            : $"/?loginError={code}&ReturnUrl={Uri.EscapeDataString(safe)}";
    }
}
