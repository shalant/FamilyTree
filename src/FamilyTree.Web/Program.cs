using FamilyTree.Core.Data;
using FamilyTree.Core.Services;
using FamilyTree.Web.App;
using FamilyTree.Web.Services;
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
    return new Azure.Storage.Blobs.BlobServiceClient(connectionString);
});

// ── Auth ──────────────────────────────────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();

if (builder.Configuration.GetValue<bool>("DevAuth:Enabled"))
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
    // Placeholder — replaced with Identity + Google OAuth in auth iteration
    builder.Services.AddAuthentication();
}

// ── Pipeline ──────────────────────────────────────────────────
var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.Run();