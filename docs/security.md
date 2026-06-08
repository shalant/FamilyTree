# Security — Arborkin

> Practical security guidance for a private family tree application.
> The goal is protecting real people's personal data and preventing accidental
> exposure — not enterprise-grade compliance. Updated: 2026-06-07.

---

## Threat model (be honest about what this is)

**What Arborkin is:** a private, invite-only web app shared among roughly a family's worth of people (10–50 users). The data is personal but not financial or medical.

**What we're protecting against:**
- A non-invited person viewing or editing family data
- A logged-in user accidentally (or deliberately) deleting records
- A former member retaining access after they should have lost it
- A curious family member stumbling into the admin area
- Standard web vulnerabilities (XSS, injection, CSRF) that could affect any user

**What we are NOT protecting against (and that's fine):**
- Nation-state actors
- Sophisticated targeted attacks on our infrastructure
- Compliance regimes (HIPAA, SOC 2, PCI) — none apply here
- Zero-day exploits in the .NET or Azure runtime

Calibrate effort accordingly. A brute-force lockout policy matters. A formal pen-test probably doesn't.

---

## Data sensitivity

| Data | Sensitivity | Notes |
|------|-------------|-------|
| Living people: name, birth year, photo | **Medium** | Personally identifiable; treat carefully |
| Deceased people: full dates, places | **Low** | Historical; public genealogy norm |
| Biography notes | **Medium–High** | May contain health, immigration, relationship details |
| User email addresses | **Medium** | Standard PII |
| Audit log | **Low–Medium** | Internal operational data |
| Profile photos of living people | **Medium** | Stored in Azure Blob Storage |

**Key rule:** living people's data is more sensitive than historical records. When in doubt, treat a person as living unless `DeathDate` is set.

---

## Authentication (when wired)

### Approach
- **Cookie auth, not JWT** — Blazor Server runs on a persistent SignalR connection; stateless JWT is wrong here
- **Google OAuth primary** — least friction for family members, no password to forget
- **Email/password fallback** — for non-Google users; use ASP.NET Core Identity's built-in hashing (bcrypt/PBKDF2 — do not roll your own)
- **No open registration** — invite-only; `UserInvite` table + token flow

### Session hygiene
- Sessions expire on browser close by default; offer "remember me" as opt-in only
- Logout invalidates the server-side session, not just the cookie
- Super-user and Admin roles require MFA (ASP.NET Core Identity supports TOTP authenticator apps out of the box)

### Invite tokens
- Generated with `RandomNumberGenerator.GetBytes(32)` → Base64 URL-safe encoding
- Stored as the raw token (or a hash — prefer hashing if the DB is ever compromised)
- Expire after 7 days; one-time use (`AcceptedAt` set on acceptance, checked before honoring)
- Cancelled immediately if the admin revokes the invite

### Password rules (if email/password is enabled)
- Minimum 10 characters — length beats complexity
- Check against `HaveIBeenPwned` API on registration (ASP.NET Identity has a NuGet for this)
- No forced periodic rotation — it just trains users to pick `Password1!`, `Password2!`

---

## Authorization

### Role model
```
Super-user   IsSuperUser = true on AppUser; global; all permissions
Admin        UserFamily.Role = "Admin"; scoped per family; manage members, restore records, audit log
Member       UserFamily.Role = "Member"; full CRUD on people and relationships
Viewer       UserFamily.Role = "Viewer"; read-only (deferred)
```

### Enforcement layers
1. **Nav link hidden** — `<AuthorizeView Roles="Admin,SuperUser">` prevents casual discovery
2. **Page-level attribute** — `@attribute [Authorize(Roles="Admin,SuperUser")]` redirects to login on direct URL access
3. **Service-level check** (add for destructive ops) — confirm the caller's role before executing; don't rely on UI alone
4. **Family scoping** — all queries filtered by `FamilyId` derived from the authenticated user's `UserFamily` rows; a user cannot access another family's data even with a valid session

### Principle of least privilege
- Members cannot see audit logs or deleted records — those are Admin-only
- Admins cannot create other Admins — only the Super-user can promote
- No self-promotion; no auto-elevation on invite acceptance (role is set by the inviter)

---

## Application security

### Blazor Server specifics
- Blazor Server runs entirely server-side — no sensitive data in the browser bundle
- All user interaction goes through SignalR; there is no REST API surface to enumerate
- `[JSInvokable]` methods are the only JS→C# bridge; keep them minimal and validate inputs
- Antiforgery is enabled (`app.UseAntiforgery()`) — do not disable it

### Input validation
- All service methods validate DTOs before touching the database (`ValidationHelper.ValidateDto`)
- `PersonService` has 70+ business rules (date bounds, age gaps, spouse conflicts)
- Never trust client-supplied IDs for authorization — always re-check ownership server-side

### SQL injection
- EF Core parameterizes all queries — do not use raw SQL strings with user input
- If raw SQL is ever needed, use `FromSqlRaw` with `@p0`-style parameters, never string interpolation

### XSS
- Blazor renders all string values HTML-encoded by default — `@variable` is safe
- Only `@((MarkupString)html)` bypasses encoding; never use it with user-supplied content
- MudBlazor components follow the same encoding rules

### CSRF
- Blazor's antiforgery token covers form submissions; SignalR channel is inherently session-bound
- No additional CSRF mitigation needed for normal Blazor Server patterns

### Secrets
- Connection strings and API keys live in **User Secrets** in development (`dotnet user-secrets`)
- In production, use **Azure Key Vault** or App Service environment variables — never commit secrets to git
- The `.gitignore` excludes `appsettings.*.json` user secrets files; keep it that way

---

## Infrastructure (Azure)

### App Service
- Run on at least the **Basic B1** tier in production (Free/Shared tiers don't support custom domains or TLS)
- Force HTTPS: `app.UseHttpsRedirection()` is already wired; also set "HTTPS Only" in App Service settings
- Set `ASPNETCORE_ENVIRONMENT=Production` — this suppresses detailed error pages and Swagger UI

### Azure SQL
- Use the **connection string in App Service Configuration** (not hardcoded)
- Enable **Azure Defender for SQL** — detects anomalous queries for free on Basic tier
- Enable **automatic backups** (on by default for Azure SQL); set 7-day retention
- The app user's DB login should have only `db_datareader` + `db_datawriter` + `EXECUTE` — not `db_owner`

### Blob Storage (photos)
- Container access level: **Private** — all blob URLs must be served through the app, not public CDN URLs
- If you generate direct blob URLs for photos, use **SAS tokens** with short expiry (1 hour) rather than permanent public URLs
- Enable **soft delete on blobs** (Azure portal: Data protection → Blob soft delete, 7 days)

### TLS / domain
- Azure App Service provides a free managed TLS certificate for custom domains
- Use TLS 1.2 minimum (Azure default); disable TLS 1.0/1.1 in App Service TLS/SSL settings

---

## Soft delete & data retention

- All Person, Relationship, and Medium records are **soft-deleted** — `DeletedAt` is set, the row remains
- The admin can **restore** within the retention window
- Hard delete is not currently exposed in the UI — deliberate
- Consider a **90-day hard-purge policy** for soft-deleted records: a background job or manual admin action to permanently remove rows older than 90 days. Not yet implemented.
- Audit log rows are **never deleted** — they are the paper trail

---

## What we're explicitly not doing (and why)

| Not doing | Why it's OK |
|-----------|-------------|
| Formal pen-test | 10–50 family users, not a public SaaS |
| WAF / DDoS protection | Azure App Service has basic rate limiting; family app isn't a target |
| Encrypted columns in SQL | Data-at-rest encryption is on by default in Azure SQL (TDE) |
| End-to-end encryption of biography text | Overkill for a family app; TDE covers the at-rest case |
| IP allowlisting | Family members travel; fixed IPs don't work |
| Per-request audit logging (reads) | Log writes and deletes only; logging every GET is noise |
| GDPR Data Protection Officer | Not required below 250 employees and without systematic processing |

---

## Incident checklist (if something goes wrong)

1. **Unauthorized access detected** → revoke the user's `UserFamily` row immediately; rotate any shared secrets; check audit log for what they accessed
2. **Data accidentally deleted** → restore from soft-delete via `/admin → Deleted tab`; if hard-deleted (unlikely), restore from Azure SQL backup
3. **Credential compromise** → force logout all sessions (invalidate the security stamp in Identity); require password reset; check `AuditLog` for actions taken
4. **Blob storage exposed** → regenerate storage account SAS key; audit blob access logs in Azure portal
5. **Secrets committed to git** → rotate immediately (connection string, storage key, OAuth client secret); use `git filter-repo` to remove from history; assume the secret is compromised from the moment of the commit
