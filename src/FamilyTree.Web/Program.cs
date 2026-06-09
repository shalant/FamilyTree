using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using FamilyTree.Core.Data;
using FamilyTree.Core.Models;
using FamilyTree.Core.Services;
using FamilyTree.Web.App;
using FamilyTree.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;

Console.WriteLine(">>> Program.cs started");

var builder = WebApplication.CreateBuilder(args);

// Load User Secrets FIRST
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>();
}

// ── Services ──────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

// Typed HTTP clients — point at the API
//var apiBaseUrl = builder.Configuration["ApiSettings:BaseUrl"]
//    ?? "https://localhost:7001";
var apiBaseUrl = builder.Configuration["ApiSettings:BaseUrl"]
    ?? "https://localhost:44367";

//builder.Services.AddHttpClient<IPersonService, PersonService>(client =>
//{
//    client.BaseAddress = new Uri(apiBaseUrl);
//});

//builder.Services.AddHttpClient<IRelationshipService, RelationshipService>(client =>
//{
//    client.BaseAddress = new Uri(apiBaseUrl);
//});



builder.Services.AddDbContextFactory<AppDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
});

// ── Rate limiting ─────────────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    // 5 login attempts per 15 min per IP
    options.AddFixedWindowLimiter("login", opt =>
    {
        opt.PermitLimit = 5;
        opt.Window = TimeSpan.FromMinutes(15);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 0;
    });
    options.OnRejected = async (ctx, _) =>
    {
        ctx.HttpContext.Response.Redirect("/?loginError=toomany");
        await Task.CompletedTask;
    };
});

var identityBuilder = builder.Services.AddIdentityCore<AppUser>(options =>
{
    options.Password.RequiredLength = 10;
    options.Password.RequireDigit = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.User.RequireUniqueEmail = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.AllowedForNewUsers = true;
})
.AddRoles<IdentityRole<Guid>>()
.AddEntityFrameworkStores<AppDbContext>()
.AddClaimsPrincipalFactory<AppUserClaimsPrincipalFactory>();

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IPersonService, PersonService>();
builder.Services.AddScoped<IRelationshipService, RelationshipService>();
builder.Services.AddScoped<IMediumService, MediumService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<IBlobStorageService, BlobStorageService>();
builder.Services.AddScoped<ThemeService>();
builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<FamilyTreeLayoutEngine>();
builder.Services.AddSingleton(_ =>
{
    var connectionString = builder.Configuration["AzureStorage:ConnectionString"];
    // Fall back to local emulator so the app starts even without a storage account configured
    return new Azure.Storage.Blobs.BlobServiceClient(
        connectionString ?? "UseDevelopmentStorage=true");
});

// ── Auth ──────────────────────────────────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();

var devAuthEnabled = builder.Configuration.GetValue<bool>("DevAuth:Enabled");

if (devAuthEnabled)
{
    var dev = builder.Configuration.GetSection("DevAuth");
    builder.Services.AddAuthentication("DevAuth")
        .AddScheme<DevAuthOptions, DevAuthHandler>("DevAuth", opts =>
        {
            opts.Email       = dev["Email"]       ?? "dev@arborkin.local";
            opts.DisplayName = dev["DisplayName"] ?? "Dev User";
            opts.UserId      = dev["UserId"]      ?? "00000000-0000-0000-0000-000000000001";
            opts.Roles       = dev.GetSection("Roles").Get<List<string>>() ?? ["Admin", "Member"];
        });
}
else
{
    identityBuilder.AddSignInManager();

    var authBuilder = builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
        .AddCookie(IdentityConstants.ApplicationScheme, options =>
        {
            options.Cookie.Name = ".ArborKin.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromDays(14);
            options.SlidingExpiration = true;
            options.LoginPath = "/";
        })
        .AddCookie(IdentityConstants.ExternalScheme);

    var googleId     = builder.Configuration["Google:ClientId"];
    var googleSecret = builder.Configuration["Google:ClientSecret"];
    if (!string.IsNullOrWhiteSpace(googleId) && !string.IsNullOrWhiteSpace(googleSecret))
    {
        authBuilder.AddGoogle(options =>
        {
            options.ClientId     = googleId;
            options.ClientSecret = googleSecret;
        });
    }
}

// ── Pipeline ──────────────────────────────────────────────────
var app = builder.Build();

// Auto-run EF migrations on startup (safe — idempotent)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    await db.Database.MigrateAsync(cts.Token);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

// ── Auth endpoints (only when real auth is active) ─────────────
if (!devAuthEnabled)
{
    app.MapPost("/auth/do-login", async (
        HttpContext ctx,
        Microsoft.AspNetCore.Identity.SignInManager<FamilyTree.Core.Models.AppUser> signInManager,
        Microsoft.AspNetCore.Identity.UserManager<FamilyTree.Core.Models.AppUser> userManager) =>
    {
        var form = await ctx.Request.ReadFormAsync();
        var email    = form["email"].ToString();
        var password = form["password"].ToString();

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return Results.LocalRedirect("/?loginError=missing");

        var result = await signInManager.PasswordSignInAsync(email, password,
            isPersistent: true, lockoutOnFailure: true);

        if (result.IsLockedOut)
            return Results.LocalRedirect("/?loginError=locked");

        if (!result.Succeeded)
            return Results.LocalRedirect("/?loginError=invalid");

        var user = await userManager.FindByEmailAsync(email);
        var redirect = user?.PersonId.HasValue == true ? $"/?focus={user.PersonId}" : "/";
        return Results.LocalRedirect(redirect);
    }).RequireRateLimiting("login");

    app.MapPost("/auth/do-logout", async (
        Microsoft.AspNetCore.Identity.SignInManager<FamilyTree.Core.Models.AppUser> signInManager) =>
    {
        await signInManager.SignOutAsync();
        return Results.LocalRedirect("/");
    });

    app.MapGet("/auth/logout", async (
        Microsoft.AspNetCore.Identity.SignInManager<FamilyTree.Core.Models.AppUser> signInManager) =>
    {
        await signInManager.SignOutAsync();
        return Results.LocalRedirect("/");
    });

    // ── Google OAuth ───────────────────────────────────────────────
    app.MapGet("/auth/google", async (
        Microsoft.AspNetCore.Identity.SignInManager<FamilyTree.Core.Models.AppUser> signInManager,
        IAuthenticationSchemeProvider schemes) =>
    {
        var scheme = await schemes.GetSchemeAsync("Google");
        if (scheme == null)
            return Results.LocalRedirect("/?loginError=google_unavailable");

        var props = signInManager.ConfigureExternalAuthenticationProperties(
            "Google", "/auth/google-callback");
        return Results.Challenge(props, ["Google"]);
    });

    app.MapGet("/auth/google-callback", async (
        Microsoft.AspNetCore.Identity.SignInManager<FamilyTree.Core.Models.AppUser> signInManager,
        Microsoft.AspNetCore.Identity.UserManager<FamilyTree.Core.Models.AppUser> userManager,
        IAuthService authService) =>
    {
        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info == null)
            return Results.LocalRedirect("/?loginError=google_error");

        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email))
            return Results.LocalRedirect("/?loginError=google_error");

        // Existing external login → sign straight in
        var signInResult = await signInManager.ExternalLoginSignInAsync(
            info.LoginProvider, info.ProviderKey, isPersistent: true, bypassTwoFactor: true);
        if (signInResult.Succeeded)
        {
            var u = await userManager.FindByEmailAsync(email);
            return Results.LocalRedirect(u?.PersonId.HasValue == true ? $"/?focus={u.PersonId}" : "/");
        }

        // Email account exists but Google not yet linked → link and sign in
        var existing = await userManager.FindByEmailAsync(email);
        if (existing != null)
        {
            await userManager.AddLoginAsync(existing, info);
            await signInManager.SignInAsync(existing, isPersistent: true);
            return Results.LocalRedirect(existing.PersonId.HasValue ? $"/?focus={existing.PersonId}" : "/");
        }

        // Brand-new user — check registration mode
        var mode = authService.GetRegistrationMode();
        if (mode == "Closed")   return Results.LocalRedirect("/?loginError=closed");
        if (mode == "InviteOnly") return Results.LocalRedirect("/?loginError=noinvite");

        // Open mode — create account from Google profile
        var firstName = info.Principal.FindFirstValue(ClaimTypes.GivenName) ?? "";
        var lastName  = info.Principal.FindFirstValue(ClaimTypes.Surname)   ?? "";
        if (string.IsNullOrWhiteSpace(firstName) && string.IsNullOrWhiteSpace(lastName))
            firstName = email.Split('@')[0];

        var newUser = new AppUser
        {
            UserName       = email,
            Email          = email,
            DisplayName    = $"{firstName} {lastName}".Trim(),
            EmailConfirmed = true,
            CreatedAt      = DateTime.UtcNow,
        };

        var created = await userManager.CreateAsync(newUser);
        if (!created.Succeeded)
            return Results.LocalRedirect("/?loginError=google_error");

        await userManager.AddLoginAsync(newUser, info);
        await signInManager.SignInAsync(newUser, isPersistent: true);
        return Results.LocalRedirect("/");
    });
}

// ── Bootstrap super-user (idempotent) ─────────────────────────
var superUserEmail = app.Configuration["SuperUser:Email"];
if (!string.IsNullOrWhiteSpace(superUserEmail))
{
    using var scope = app.Services.CreateScope();
    var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
    var su = await userMgr.FindByEmailAsync(superUserEmail);
    if (su != null && !su.IsSuperUser)
    {
        su.IsSuperUser = true;
        await userMgr.UpdateAsync(su);
        app.Logger.LogInformation("Bootstrapped super-user: {Email}", superUserEmail);
    }
}

app.Run();