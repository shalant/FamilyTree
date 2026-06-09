# Deployment Guide — Azure App Service + Azure SQL

## Current setup

| Resource | Azure Service | Tier | Cost |
|---|---|---|---|
| Web app | App Service (Linux) | B1 | ~$13/mo |
| Database | Azure SQL Database | Basic 5 DTU | ~$5/mo |
| Blob Storage | Azure Storage | LRS | ~$0.50/mo |
| **Total** | | | **~$18–20/mo** |

The app is a single Blazor Server project (`FamilyTree.Web`) — there is no separate API service.
Blazor Server requires **Always On** to keep SignalR connections alive; B1 is the minimum viable
tier. F1 (free) cannot be used reliably because it spins down after inactivity, killing all
active WebSocket connections.

---

## CI/CD

Two GitHub Actions workflows:

| Workflow | Trigger | What it does |
|---|---|---|
| `ci.yml` | Push to `master`/`main`, PRs | Build + test only |
| `deploy-web.yml` | Manual `workflow_dispatch` | Build, publish, deploy to App Service |

To deploy: GitHub → Actions → Deploy Web → Run workflow.

### Required GitHub secrets

| Secret | Value |
|---|---|
| `AZURE_CREDENTIALS` | Service principal JSON (`az ad sp create-for-rbac`) |
| `AZURE_WEB_APP_NAME` | Your App Service name (e.g. `arborkin`) |

---

## Database migrations

EF Core migrations run automatically on startup via `ctx.Database.MigrateAsync()` in
`Program.cs`. Every deploy applies any pending migrations to Azure SQL automatically.
You never need to run SQL manually against production — unless emergency hotfixes are needed
(use Azure Portal Query Editor in that case).

Local workflow:
```
1. Change a model in src/FamilyTree.Core/Models/
2. dotnet ef migrations add <MigrationName> --project src/FamilyTree.Core --startup-project src/FamilyTree.Web
3. dotnet ef database update --startup-project src/FamilyTree.Web   ← applies to local SQL
4. git commit + push to master
5. workflow_dispatch deploy → App Service starts → MigrateAsync runs → Azure SQL updated
```

**Important:** When creating migrations manually (without running `dotnet ef`), you must include:
- The `[Migration("timestamp_name")]` attribute (EF won't discover the migration without it)
- A timestamp strictly after the last applied migration (check `__EFMigrationsHistory`)
- A matching snapshot update in `AppDbContextModelSnapshot.cs`

---

## App Service configuration

In Azure Portal → App Service → Configuration → Application settings:

| Name | Value |
|---|---|
| `ConnectionStrings__DefaultConnection` | `Server=...database.windows.net;Database=FamilyTreeDb;User Id=...;Password=...;Encrypt=True` |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `SuperUser__Email` | your admin email |
| `Google__ClientId` | (optional) Google OAuth client ID |
| `Google__ClientSecret` | (optional) Google OAuth client secret |
| `AzureStorage__ConnectionString` | Azure Storage connection string |

Use double-underscore for nested config keys (Azure App Service convention).

Always On: Configuration → General Settings → **Always On: On** (included in B1, required for Blazor Server SignalR).

---

## Connecting to Azure SQL from SSMS

```
Server:   yourserver.database.windows.net
Auth:     SQL Server Authentication
Username: your admin user
Password: your password
```

Make sure your current IP is allowed in the SQL Server firewall (Portal → SQL Server → Networking → Add client IP).

---

## Cost reduction options

- **Azure SQL Serverless** — auto-pauses after 1 hour of inactivity; cheaper at very low usage but adds a 30–60s cold-start delay after pause. Basic DTU is more predictable.
- **No deployment slots needed** — slots require Standard tier ($56/mo). Stick with direct deploy to the single production slot.
- **Disable Application Insights** — if auto-attached, it adds $2–5/mo at low volume. Check App Service → Application Insights in portal.
- **Connection string pool tuning** — add `Min Pool Size=0;` to reduce idle DTU consumption on the Basic tier.
