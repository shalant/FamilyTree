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
| `Auth__RegistrationMode` | `InviteOnly` (production default; `Open` for dev) |
| `DevAuth__Enabled` | `false` ← **must be false in production** |
| `Google__ClientId` | (optional) Google OAuth client ID |
| `Google__ClientSecret` | (optional) Google OAuth client secret |
| `AzureStorage__ConnectionString` | Azure Storage connection string |
| `Email__SmtpHost` | SMTP host (e.g. `smtp.gmail.com`) |
| `Email__SmtpPort` | `587` |
| `Email__EnableSsl` | `true` |
| `Email__Username` | SMTP username / Gmail address |
| `Email__Password` | Gmail App Password (not your Google account password) |
| `Email__FromAddress` | From address for outbound email |
| `Email__FromName` | `ArborKin` |

Use double-underscore for nested config keys (Azure App Service convention).

Always On: Configuration → General Settings → **Always On: On** (included in B1, required for Blazor Server SignalR).

**ARR Affinity:** Configuration → General Settings → **ARR Affinity: On** (required — routes returning WebSocket connections back to the same instance that owns the Blazor circuit).

---

## Blazor Server circuit behavior

Blazor Server runs over a persistent WebSocket (SignalR). The server holds a "circuit" in memory for each connected browser tab. This has two known failure modes users may encounter:

### Stale tab after server restart or long idle
**What happens:** If a browser tab is left open overnight (or across a deployment), the server-side circuit is cleaned up. When the tab wakes up it tries to reconnect; the server has no matching circuit and responds with `"The list of component operations is not valid"`, then immediately disconnects. The page freezes.

**Console signature:**
```
Error: The list of component operations is not valid.
Information: Connection disconnected.
```

**Fix for users:** Refresh the page. The app reconnects and works immediately.

**This is expected Blazor Server behavior**, not a bug. It happens on every Azure deployment (the process restarts) and after Azure's idle recycling.

### Circuit timeout
The server discards a circuit after ~3 minutes of WebSocket inactivity by default. For an "always open" admin tab this can trigger the same disconnect. To extend it, configure in `Program.cs`:

```csharp
builder.Services.AddServerSideBlazor(options =>
{
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(10);
});
```

The current app uses the default; adjust if user-testing shows too many unexpected disconnects.

---

## Post-deploy checklist

After every `workflow_dispatch` deploy, verify:

- [x] `DevAuth__Enabled` is `false` in App Service Configuration ✓ confirmed 2026-06-11
- [ ] `ASPNETCORE_ENVIRONMENT` is `Production`
- [x] `Auth__RegistrationMode` is `InviteOnly` ✓ confirmed 2026-06-11
- [ ] App loads at production URL and login works
- [ ] Google OAuth redirect completes (if configured)
- [ ] EF migrations applied: check Azure SQL `__EFMigrationsHistory` table or watch startup logs in Log Stream

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

## Custom domain (arborkin.com) — LIVE ✅

> **Status: live** — deployed 2026-07-29. App accessible at `https://arborkin.com`.

The app runs on Azure App Service, custom domain bound to `arborkin.com`. Both the custom domain and the existing `azurewebsites.net` URL remain accessible; family members should use `arborkin.com`.

**What was done:**

1. **Upgraded App Service tier** — Free (F1) → Basic B1 ($13/mo) to enable custom domain support and managed SSL certificates

2. **Added custom domain in Azure**
   - Azure Portal → App Service → Custom domains → Add custom domain
   - Selected `arborkin.com`
   - Azure generated CNAME record

3. **Updated DNS at GoDaddy**
   - Added CNAME record: `arborkin` → `arborkin-erbufqfkhzcka4cb.centralus-01.azurewebsites.net`
   - DNS propagated within 30 minutes

4. **Configured AllowedHosts in appsettings.json**
   - Updated `"AllowedHosts"` to semicolon-separated list: `arborkin-erbufqfkhzcka4cb.centralus-01.azurewebsites.net;arborkin.com;www.arborkin.com`
   - Deployed to Azure via `deploy-web.yml` workflow

5. **Verified SSL & HTTPS**
   - Azure auto-provisioned Let's Encrypt certificate via App Service Managed Certificates
   - HTTPS Only already enabled
   - Custom domain status shows "Secured" in Azure Portal

**Security verified:**
- ✓ `Auth__RegistrationMode = InviteOnly` — production setting, confirmed 2026-07-29
- ✓ `DevAuth__Enabled = false` — production setting, confirmed 2026-07-29
- ✓ CSP header configured via middleware in `Program.cs`
- ✓ Response headers (X-Frame-Options, X-Content-Type-Options, etc.) set in middleware
- ✓ Both custom domain and `azurewebsites.net` URL accessible (backward compatibility for monitoring/alerts)

**What's next:**
- [ ] Update Azure Monitor alert URLs to ping `arborkin.com` instead of `azurewebsites.net`
- [ ] Consider redirecting `www.arborkin.com` → `arborkin.com` (currently both work, no preference yet)
- [ ] Document for future maintainers: custom domain setup checklist in this guide

---

## Cost reduction options

- **Azure SQL Serverless** — auto-pauses after 1 hour of inactivity; cheaper at very low usage but adds a 30–60s cold-start delay after pause. Basic DTU is more predictable.
- **No deployment slots needed** — slots require Standard tier ($56/mo). Stick with direct deploy to the single production slot.
- **Disable Application Insights** — if auto-attached, it adds $2–5/mo at low volume. Check App Service → Application Insights in portal.
- **Connection string pool tuning** — add `Min Pool Size=0;` to reduce idle DTU consumption on the Basic tier.
