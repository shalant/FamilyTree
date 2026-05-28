using Azure.Storage.Blobs;
using FamilyTree.Api.Data;
using FamilyTree.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──────────────────────────────────────────────────
builder.Services.AddControllers();

// Swashbuckle 7.0 — the ONLY OpenAPI/Swagger system
builder.Services.AddSwaggerGen();

//builder.Services.AddDbContext<AppDbContext>(options =>
//    options.UseSqlServer(
//        builder.Configuration.GetConnectionString("DefaultConnection"),
//        sql => sql.EnableRetryOnFailure(maxRetryCount: 5)
//    )
//);

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IPersonService, PersonService>();
builder.Services.AddScoped<IRelationshipService, RelationshipService>();

// CORS — allow the Blazor web app to call this API
builder.Services.AddCors(options =>
    options.AddPolicy("BlazorWeb", policy =>
        policy.WithOrigins(
                builder.Configuration["AllowedOrigins:Web"] ?? "https://localhost:7000")
              .AllowAnyHeader()
              .AllowAnyMethod())
);

builder.Services.AddSingleton(x =>
{
    var config = x.GetRequiredService<IConfiguration>();
    return new BlobServiceClient(config["AzureStorage:ConnectionString"]);
});

// ── Pipeline ──────────────────────────────────────────────────
var app = builder.Build();

// Auto-apply EF Core migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    if (app.Environment.IsDevelopment())
        DataSeeder.Seed(db);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "FamilyTree API v1");
        //c.RoutePrefix = string.Empty; // makes Swagger UI the root
        c.RoutePrefix = "swagger"; // makes Swagger UI the root
    });
}

app.UseHttpsRedirection();
app.UseCors("BlazorWeb");
app.UseAuthorization();
app.MapControllers();

app.Run();