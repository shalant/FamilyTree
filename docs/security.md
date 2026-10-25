# Security — Arborkin

> Practical security guidance for a public family tree application.
> The goal is protecting real people's personal data — especially living people and
> minors — while keeping the app discoverable and useful. Updated: 2026-06-08.
>
> **Status note (2026-10-10):** much of the design below (visibility tiers, minor
> protection, `PersonClaimStatus` approval, email-confirmation gating, MFA, per-family
> `UserFamily.Role` admins, SAS-token blob URLs, HaveIBeenPwned) is **not implemented**;
> treat those sections as the target design, not current behaviour. What is actually
> enforced today is recorded in "Scoping and auth adversarial review (2026-10-10)" below.

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

### A01 · Broken Access Control — OPEN (Critical) → see the 2026-10-10 review below
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
- **Vulnerable packages (checked 2026-10-23, `dotnet list package --vulnerable --include-transitive`):**
  - Fixed: `System.Security.Cryptography.Pkcs` 9.0.4 (high, GHSA-846f-f764-76mx; moderate, GHSA-2fmv-rv2c-59x7), pulled in by `Microsoft.Data.SqlClient` 6.1.1. Pinned to 10.0.13 in `FamilyTree.Core.csproj`.
  - **Open:** `SixLabors.ImageSharp` 3.1.12 (bumped from 3.1.11) still has five advisories (three high); only 4.1.2+ fixes them. ImageSharp 4 fails the build without a Six Labors license key (`SixLaborsLicenseKey` or a `sixlabors.lic` file), even under the free Apache tier, so upgrading needs Doug to get a key from sixlabors.com. Reachable: `MediumService.ResizeIfOversizedAsync` decodes uploaded images (signed-in family members only). Until 2026-10-24 it decoded whatever format ImageSharp detected, TIFF included, because the MIME allow-list trusts the browser's label. **Narrowed 2026-10-24:** `CreateAsync` now checks the file's actual bytes and rejects any recognised format other than JPEG, PNG, WebP or GIF, and the resize step decodes only those four (`MediumServiceTests.CreateAsync_DisallowedFormatLabelledAsJpeg_IsRejectedAndNotUploaded`). That closes the TIFF encoder/decoder bugs. Still reachable: the ICC-profile allocation bug, since JPEG and PNG can carry ICC profiles; only the 4.x upgrade fixes it. The HistogramEqualization one doesn't apply (never called).
  - Open, tests only: `AngleSharp` 1.1.2 (moderate, via bUnit) in `FamilyTree.Web.Tests`; never deployed.

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

## Scoping and auth adversarial review (2026-10-10)

Leg 3 of `docs/Testing/fuzz-and-adversarial-review-plan.md`, branch
`security/scoping-adversarial-review`. A `/code-review` at xhigh effort plus a manual pass
over `PersonService`, `RelationshipService`, `StoryService`, `MediumService`,
`GedcomExportService`, `StoryInviteService`, `AuthService`, `AppUserClaimsPrincipalFactory`,
`CurrentUserService`, `AdminUserService`, `AdminAuditService`, and the pages that call them.
S3/S4/S5 only leak data once a second `Family` row exists; whether production has more
than one was not checked (no production access in this review). S1 and S2 were
exploitable with a single family.

**Enforcement now in place.** Every family-scoped query goes through one predicate per
service (`ScopedPeople`, `ScopedStories`, `VisiblePersonIds`): a super-user sees all
families; anyone else sees only their own `FamilyId` claim; a caller with no claim sees
nothing. A family-B ID behaves exactly like an ID that doesn't exist (same message).
Locked in by `tests/FamilyTree.Core.Tests/CrossTenantMatrixTests.cs`: two seeded
families, every public method of the services above called as a family-A Member and
Admin against family-B IDs, plus positive controls as family B and as the super-user.
**When adding a public method to one of these services, add a row there.** Pages are
covered by `tests/FamilyTree.Web.Tests/ComponentTests/PageAuthorizationTests.cs`.

| # | Severity | Finding | Status |
|---|---|---|---|
| S1 | Critical | Only `/admin` had `[Authorize]`; `AuthorizeRouteView` only blocks pages that do. Logged out, `/people/add`, `/stories`, `/dashboard` etc. loaded (confirmed in a local run), and `PersonService`/`StoryService`/`MediumService.CreateAsync` accepted anonymous callers, putting their rows in the oldest family. | **Fixed.** `[Authorize]` in `_Imports.razor`; nine public pages opt out with `[AllowAnonymous]`. Creates require a signed-in family member (super-user excepted). |
| S2 | Critical | Any Admin could take over the super-user: request a reset at `/forgot-password`, then Admin › Users › "Copy reset link" (the reset request `Id` is the only credential). The Admin role is global, and the Users, Audit log and Activity tabs showed every family's accounts, invites and audit rows; Admins could promote others to Admin. | **Fixed.** `AdminUserService`, `AdminAuditService`, `AdminActivityService` and `AuthService`'s invite/reset/link-person methods are SuperUser-only; those tabs are hidden from Admins. Decision 2026-10-10: family Admins keep person/story moderation, scoped to their family. |
| S3 | High | Cross-family reads: `StoryService.GetAllApprovedAsync` (the `/stories` feed), `GetAllAsync`, `GetUnlinkedAsync`, `GetByPersonAsync`; `MediumService.GetAllAsync`/`GetByPersonIdAsync`; `StoryInviteService.GetAllAsync`; relationship lists exposing another family's person IDs. `GetByPersonAsync` also showed pending and hidden stories in the person drawer. | **Fixed.** All scoped; the drawer shows approved, non-hidden stories only. |
| S4 | High | Cross-family writes: Person Update/Delete (any Admin), Restore (anyone); relative IDs from another family on Create/Update; Story SetHidden/Approve/Move/LinkToPerson, and edit/delete of any unlinked story by anyone; Medium uploads onto any person; `LinkUserToTreeAsync`/`CreateUnlinkedPersonAsync` planting people in another family; story invites about another family's person. | **Fixed.** All scoped. Story edit/delete: author or Admin (decision 2026-10-10). Restore requires Admin. |
| S5 | High | New users always joined the oldest family: admin invites, story-invite responses, Open-mode registration with an invite (the invite was ignored). | **Fixed** for story responses (the subject's family, else the sender's) and Open-mode invites (the invite's family). **By design, documented:** the super-user's own invites, Google sign-up in Open mode and plain Open-mode signup still use the oldest family, because the super-user has no family claim and those paths carry no family. Fix before a second family uses Open mode. |
| S6 | Medium | Name/existence oracles: validators quoted another family's person (name, death date, marital status); different messages for "not found" vs "no permission"/"exists elsewhere". | **Fixed** on every path above. |
| S7 | Medium | Role and `FamilyId` claims sit in a 14-day sliding cookie with no security-stamp validation, so demoting an Admin or resetting a password doesn't end existing sessions. `AppUser.IsAdmin` is a second, UI-invisible source of the Admin role. | **Fixed 2026-10-25** (A4). The cookie checks the security stamp (`SecurityStampValidator`) and `SecurityStampRevalidatingAuthStateProvider` re-checks open Blazor circuits, both every 5 minutes (`Auth:SessionCheckInterval`); a stale stamp signs the session out, a current one refreshes the claims. A password reset or an Admin change (`AdminUserService.SetAdminAsync`) rotates the stamp. `IsAdmin` is now the only Admin source: the Users tab read nothing and wrote Identity roles that were never created; migration `FoldAdminRoleIntoIsAdmin` moves any such membership onto `IsAdmin`. Limit: services read claims from the circuit's `HttpContext`, so a revoked circuit stops rendering protected pages but isn't torn down. |
| S8 | Medium | Media are stored as raw blob URIs with no SAS token, in one shared container; anyone holding a URL can fetch the photo, including after the person is deleted. This contradicts "Blob Storage" under Infrastructure below. The live container's access level was **not** checked (no live Azure access in this review); it must allow anonymous blob reads, since the browser loads these unsigned URLs directly and photos display. | **Open**, TodoList (A4, its own PR). |
| S9 | Low | `Auth:RegistrationMode` fails open (missing or mis-cased → Open); story-invite `IsUsed` check-then-set race; CR/LF in names/places injects GEDCOM records; a null MIME type skips the upload allow-list; `/auth/do-login` ignores `ReturnUrl`. | **Partly fixed 2026-10-25** (A4): the mode fails closed (unrecognised → InviteOnly, case-insensitive) and sign-in follows `ReturnUrl` for local paths only (`LoginRedirect`). The rest **Open**, TodoList. |

**Accepted by design (raise before changing):** any Member can send a story invite to any
email, and a response offers that email a registration invite into the story's family.
That's the "Willa scenario" onboarding path (CLAUDE.md), and it bypasses
`InviteOnly`'s admin gate by intent.

---

## Incident checklist

1. **Unauthorized access** → revoke `UserFamily` row; check audit log; rotate secrets if needed
2. **Data accidentally deleted** → restore from soft-delete in `/admin → Deleted`; if hard-deleted, restore from Azure SQL backup
3. **Credential compromise** → invalidate security stamp in Identity (forces re-login for all sessions); check `AuditLog`
4. **Minor data exposed publicly** → check `Person.IsMinorOverride` and `BirthDate`; audit `PersonService` visibility tier logic
5. **Blob storage exposed** → regenerate storage SAS key; audit Azure blob access logs
6. **Secrets committed to git** → rotate immediately; use `git filter-repo` to remove from history; assume compromised
