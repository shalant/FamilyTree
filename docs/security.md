# Security — Arborkin

> Practical security guidance for a public family tree application.
> The goal is protecting real people's personal data — especially living people and
> minors — while keeping the app discoverable and useful. Updated: 2026-06-08.

---

## Threat model

**What Arborkin is:** a publicly accessible family tree app. Anyone can register
and search public trees. Family data is gated behind membership; public visitors
get a limited read-only view.

**What we're protecting against:**
- Unauthenticated access to private family data (membership required for full access)
- Living minors' data appearing in public search results or being indexed
- A registered user accessing another family's data
- A former member retaining access after removal
- Unauthorized users claiming a person node without approval
- Account takeover (stolen credentials, session hijack)
- Standard web vulnerabilities: XSS, injection, CSRF

**What we are NOT protecting against:**
- Nation-state actors
- Compliance regimes (HIPAA, SOC 2, PCI) — none apply here
- Zero-day exploits in the .NET or Azure runtime

---

## Data sensitivity & visibility tiers

### Sensitivity
| Data | Sensitivity | Notes |
|------|-------------|-------|
| Living minors: any data | **High** | Hidden from all public views; limited even to Members |
| Living adults: name, birth year, photo | **Medium** | PII; public view shows name only |
| Deceased people: full dates, places | **Low** | Historical; public genealogy norm |
| Biography notes | **Medium–High** | May contain health, immigration, relationship details |
| User email addresses | **Medium** | Standard PII; never exposed in public views |
| Audit log | **Low–Medium** | Admin-only |

### Visibility tiers (enforced in service/mapper layer)

| Tier | Who | Deceased | Living adult | Minor |
|------|-----|----------|-------------|-------|
| Public | Unauthenticated / Viewer | Full record | Name only | Hidden ("Private") |
| Member | Authenticated family member | Full record | Full record | First name + position only |
| Admin / SuperUser | Admin or SuperUser | Full record | Full record | Full record |

**Minor detection:** `Person.DeathDate == null && BirthDate >= today − 18 years`.
`Person.IsMinorOverride` can force or override this when BirthDate is unknown.

---

## Authentication

### Approach
- **Cookie auth, not JWT** — Blazor Server runs on a persistent SignalR connection
- **Google OAuth primary** — least friction; no password to forget
- **Email/password fallback** — ASP.NET Core Identity's built-in bcrypt/PBKDF2 hashing
- **Open registration** — anyone can create an account; family membership is separate

### Account states
- **Registered, no family** — can log in, sees onboarding page only
- **Pending claim** — has a person node claimed (`PersonClaimStatus = Pending`); awaiting admin approval
- **Member** — claim approved (`PersonClaimStatus = Approved`); `UserFamily` row exists; full family access
- **Admin** — `UserFamily.Role = "Admin"`; can manage members, restore records, view audit log
- **SuperUser** — `AppUser.IsSuperUser = true`; global access across all families

### Session hygiene
- Sessions expire on browser close by default; "remember me" is opt-in
- Logout invalidates the server-side session, not just the cookie
- Super-user and Admin roles require MFA (ASP.NET Identity TOTP)

### Password rules (email/password path)
- Minimum 10 characters — length over complexity
- Check against HaveIBeenPwned API on registration
- No forced periodic rotation
- Password reset: `UserManager.GeneratePasswordResetTokenAsync` → email link → `ResetPasswordAsync`
- Google-only users have no password; show "sign in with Google" on the reset page

### Email verification
- New accounts: `EmailConfirmed = false`; confirmed via link in registration email
- Unconfirmed accounts can log in but cannot access family data

---

## Authorization

### Role model
```
SuperUser   IsSuperUser = true on AppUser; global; all permissions; can delete any user
Admin       UserFamily.Role = "Admin"; scoped per family; manages members, approvals, restore
Member      UserFamily.Role = "Member"; full CRUD on people and relationships in their family
Viewer      Unauthenticated public access to public families; read-only, visibility-tiered
```

### Enforcement layers
1. **Nav link hidden** — `<AuthorizeView Roles="Admin,SuperUser">` prevents casual discovery
2. **Page-level attribute** — `@attribute [Authorize(Roles="Admin,SuperUser")]` redirects on direct URL access
3. **Service-level visibility tier** — enforced in mapper/service, not just UI; public vs member vs admin data shape
4. **Family scoping** — all queries filtered by `FamilyId` from current user's `UserFamily` rows
5. **Claim approval gate** — `PersonClaimStatus = Pending` blocks family access until admin approves

### Principle of least privilege
- Members cannot see audit logs, deleted records, or pending claims — Admin-only
- Admins cannot promote other users to Admin — only SuperUser can
- `Family.IsPublic` can only be set by a family Admin or SuperUser
- `Family.RequireApproval` defaults to `true`; Admin can disable it per family

---

## Minor protection

- Minors never appear in public search results or unauthenticated views
- The "find my node" wizard hides minors at the query level — not just the UI
- Even Members see only first name and family position for minors; no dates or places
- Admins and SuperUsers see full data (needed for record management)
- `Person.IsMinorOverride` allows manual override when BirthDate is unknown:
  - `null` = derive from BirthDate (default)
  - `true` = force treat as minor regardless of dates
  - `false` = force treat as adult (e.g., adopted adult with unknown birthdate)

---

## Application security

### Blazor Server specifics
- Blazor Server runs entirely server-side — no sensitive data in the browser bundle
- All user interaction goes through SignalR; there is no REST API surface to enumerate
- `[JSInvokable]` methods are the only JS→C# bridge; keep them minimal and validate inputs
- Antiforgery is enabled (`app.UseAntiforgery()`) — do not disable it

### Input validation
- All service methods validate DTOs before touching the database
- `PersonService` has 70+ business rules (date bounds, age gaps, spouse conflicts)
- Never trust client-supplied IDs for authorization — always re-check ownership server-side

### SQL injection
- EF Core parameterizes all queries — no raw SQL strings with user input
- If raw SQL is ever needed, use `FromSqlRaw` with `@p0`-style parameters only

### XSS
- Blazor renders all string values HTML-encoded by default — `@variable` is safe
- `@((MarkupString)html)` bypasses encoding — never use it with user-supplied content

### Secrets
- Connection strings and API keys: **User Secrets** in development
- Production: **Azure Key Vault** or App Service environment variables — never commit secrets

---

## Infrastructure (Azure)

- **App Service**: force HTTPS; set `ASPNETCORE_ENVIRONMENT=Production`
- **Azure SQL**: connection string in App Service Configuration; Azure Defender enabled; 7-day backup retention; app user has `db_datareader + db_datawriter + EXECUTE` only
- **Blob Storage**: private container; SAS tokens with short expiry for photo URLs; blob soft delete enabled (7 days)
- **TLS**: managed certificate; TLS 1.2 minimum

---

## Soft delete & data retention

- Person, Relationship, Medium: soft-deleted (`DeletedAt` set); Admin can restore within retention window
- Hard delete not exposed in UI — deliberate
- **90-day hard-purge policy** (not yet implemented): background job removes soft-deleted rows older than 90 days
- Audit log rows are never deleted

---

## What we're explicitly not doing

| Not doing | Why it's OK |
|-----------|-------------|
| Formal pen-test | Small user base, not a financial/medical SaaS |
| WAF / DDoS protection | Azure App Service basic rate limiting is sufficient |
| Encrypted columns | Azure SQL TDE covers at-rest encryption |
| IP allowlisting | Users travel; fixed IPs don't work |
| Per-request read audit logging | Log writes and deletes only; read logging is noise |
| GDPR Data Protection Officer | Not required for this scale |

---

## OWASP Top 10:2025 — Audit Findings (2026-06-12)

> Status key: **OPEN** = unmitigated gap, **PARTIAL** = some protection exists, **OK** = well covered.

### A01 · Broken Access Control — OPEN (Critical)
- `PersonService.GetByIdAsync()` has **no `FamilyId` check** — any authenticated user who knows a GUID can fetch any person across family boundaries. Same gap exists in `RelationshipService` and `MediumService`.
- `DevAuthHandler.cs` bypasses all authentication when `DevAuth:Enabled = true`. If this reaches a deployed environment the app is fully open.
- *Fix:* add family-scope guard to every single-entity lookup; add a startup assertion that `DevAuth:Enabled` is false in non-Development environments.

### A02 · Security Misconfiguration — OPEN (High)
- `"AllowedHosts": "*"` in `appsettings.json` allows any Host header — enables Host Header Injection. Lock to the actual domain in production.
- No **Content-Security-Policy**, **X-Frame-Options**, **X-Content-Type-Options**, or **Referrer-Policy** response headers configured anywhere.
- `appsettings.Development.json` contains a hardcoded super-user email and `DevAuth:Enabled = true` — must never reach a deployed environment.

### A03 · Software Supply Chain — PARTIAL
- `Azure.Storage.Blobs 12.29.0-beta.1` is a pre-release package used in production (`FamilyTree.Core.csproj`). Upgrade to a stable release.
- No committed NuGet lock file — enables dependency-confusion attacks. Add `RestorePackagesWithLockFile = true` to project files.

### A04 · Cryptographic Failures — PARTIAL
- Password policy is weakened: `RequireDigit = false`, `RequireNonAlphanumeric = false`, `RequireUppercase = false` (`Program.cs` ~L74). "aaaaaaaaaa" is a valid password.
- *Note:* `security.md` lines 74 and 78 claim MFA is required for Admin/SuperUser and that HaveIBeenPwned is checked on registration — neither was found in the codebase. These should be treated as aspirational until implemented.

### A05 · Injection — PARTIAL (mostly OK)
- No raw SQL anywhere; EF Core parameterizes all queries.
- `Faq.razor` uses `@((MarkupString)item.A)` to render FAQ answers as raw HTML. If FAQ content ever comes from the database or user input without sanitization, this is a stored XSS vector. (`security.md` line 146 correctly documents the rule — enforce it here.)

### A06 · Insecure Design — OPEN (Medium)
- **Password reset tokens are in the URL query string** (`AuthService.cs` ~L204) — tokens leak via `Referer` headers, server logs, and browser history. Same issue with registration invite tokens.
- No rate limit on invite generation — an admin could spam invitations with no throttle.
- No "a password reset was requested for your account" notification email to the account owner.

### A07 · Authentication Failures — PARTIAL
- No MFA/2FA implemented (`security.md` line 74 says it's required — it is not yet built).
- Password reset tokens are not session- or IP-bound; token theft before redemption is undetected.
- No login history visible to users; no new-device alerts.

### A08 · Software/Data Integrity — PARTIAL
- `AuditLogService` silently swallows its own exceptions (~L48) — audit failures are invisible to admins.
- Audit log rows are mutable/deletable by anyone with direct DB access; not tamper-proof.

### A09 · Security Logging & Alerting — PARTIAL
- No real-time alerts for suspicious events (mass deletes, repeated lockouts, role escalation, new registrations). Admins must manually poll the admin page.
- Rate-limit rejections are not logged (`Program.cs` ~L65).
- No integration with Application Insights, Sentry, or equivalent for centralized error visibility.

### A10 · Exception Handling — OK (minor notes)
- Generic error messages used throughout; `UseExceptionHandler` active in production.
- Some error messages include entity GUIDs (minor information leakage).
- If `db.Database.MigrateAsync()` throws on startup the app crashes with no graceful degradation.

---

## Incident checklist

1. **Unauthorized access** → revoke `UserFamily` row; check audit log; rotate secrets if needed
2. **Data accidentally deleted** → restore from soft-delete in `/admin → Deleted`; if hard-deleted, restore from Azure SQL backup
3. **Credential compromise** → invalidate security stamp in Identity (forces re-login for all sessions); check `AuditLog`
4. **Minor data exposed publicly** → check `Person.IsMinorOverride` and `BirthDate`; audit `PersonService` visibility tier logic
5. **Blob storage exposed** → regenerate storage SAS key; audit Azure blob access logs
6. **Secrets committed to git** → rotate immediately; use `git filter-repo` to remove from history; assume compromised
