# Deployment Guide — Azure App Service + Azure SQL (Free Tiers)

## Overview

| Resource | Azure Service | Cost |
|---|---|---|
| API | App Service F1 (Free) | $0/mo |
| Web | App Service F1 (Free) | $0/mo |
| Database | Azure SQL Database (Free offer) | $0/mo |

**Total: $0/mo** — forever, as long as you stay within free tier limits.

Free tier limits that matter for a family app:
- App Service F1: 60 CPU minutes/day (shared) — ample for low traffic
- Azure SQL: 100,000 vCore-seconds/month, 32 GB storage — more than enough

---

## One-time Azure setup

### 1. Create the Azure SQL Database (free tier)

1. Azure Portal → Create a resource → Azure SQL Database
2. On the provisioning page, look for the **"Apply offer: Free Azure SQL Database"** banner and click it
3. Settings:
   - Database name: `FamilyTreeDb`
   - Server: create new → pick a name, region, SQL auth username + password
   - Compute + storage: **Free offer** should be pre-selected (General Purpose, Serverless)
   - Backup redundancy: Locally redundant (cheapest)
4. **Behavior when free limit reached: Auto-pause until next month** — ensures $0 bill
5. Create

Note the server name: `yourserver.database.windows.net`

### 2. Configure the SQL Server firewall

In the SQL Server resource (not the database):
- Networking → Add your current client IP
- Toggle "Allow Azure services and resources to access this server" → ON (needed for App Service)

### 3. Create the API App Service

1. Create a resource → Web App
2. Settings:
   - Name: `familytree-api` (becomes `familytree-api.azurewebsites.net`)
   - Runtime: .NET 10
   - OS: Linux (cheaper than Windows on free tier)
   - Plan: **Free F1**
3. Create

After creation, go to **Configuration → Application settings** and add:

| Name | Value |
|---|---|
| `ConnectionStrings__DefaultConnection` | `Server=yourserver.database.windows.net;Database=FamilyTreeDb;User Id=youruser;Password=yourpassword;TrustServerCertificate=False;Encrypt=True` |
| `AllowedOrigins__Web` | `https://familytree-web.azurewebsites.net` |
| `ASPNETCORE_ENVIRONMENT` | `Production` |

### 4. Create the Web App Service

Same as above but:
- Name: `familytree-web`
- After creation, add application setting:

| Name | Value |
|---|---|
| `ApiSettings__BaseUrl` | `https://familytree-api.azurewebsites.net` |
| `ASPNETCORE_ENVIRONMENT` | `Production` |

---

## GitHub Actions setup

The deploy workflows use publish profiles for authentication — simpler than service principals for personal projects.

### Get publish profiles

For each App Service (API and Web):
1. Azure Portal → App Service → Overview → Download publish profile
2. Open the downloaded `.PublishSettings` file and copy the entire XML content

### Add GitHub secrets

In your GitHub repo → Settings → Secrets and variables → Actions:

| Secret name | Value |
|---|---|
| `AZURE_API_APP_NAME` | `familytree-api` |
| `AZURE_API_PUBLISH_PROFILE` | (paste entire XML from API publish profile) |
| `AZURE_WEB_APP_NAME` | `familytree-web` |
| `AZURE_WEB_PUBLISH_PROFILE` | (paste entire XML from Web publish profile) |

### Trigger first deploy

Push to `main` — GitHub Actions will build, test, and deploy both apps.
EF Core migrations run automatically on API startup, creating all tables in Azure SQL.

---

## Local dev → Azure SQL sync

EF Core migrations are the single source of truth for schema.

```
Local workflow:
1. Change a model in FamilyTree.Api/Models/
2. dotnet ef migrations add <MigrationName>   ← generates migration file
3. dotnet ef database update                  ← applies to local SQL Server
4. git commit + push to main
5. GitHub Actions deploys API
6. API starts → db.Database.Migrate() runs → Azure SQL updated automatically
```

You never need to manually run SQL against Azure SQL. Migrations handle it.

---

## Connecting to Azure SQL from SSMS

You can inspect your production data directly in SSMS:

```
Server:   yourserver.database.windows.net
Auth:     SQL Server Authentication
Username: your admin user
Password: your password
```

Make sure your current IP is allowed in the SQL Server firewall first.
