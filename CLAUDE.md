# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run Commands

```bash
# Build
dotnet build FamilyTree.sln
dotnet build FamilyTree.sln -c Release   # verify production build

# Run (Web only — no separate Core API)
cd src/FamilyTree.Web && dotnet watch    # Web UI → https://localhost:44381

# Tests
dotnet test FamilyTree.sln
dotnet test tests/FamilyTree.Core.Tests/FamilyTree.Core.Tests.csproj

# Database migrations (run from src/FamilyTree.Core)
dotnet ef migrations add <MigrationName> --startup-project ../FamilyTree.Web
dotnet ef database update               --startup-project ../FamilyTree.Web

# User secrets (keep credentials out of appsettings.json)
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<connstr>" --project src/FamilyTree.Web
dotnet user-secrets set "SuperUser:Email"      "<email>"  --project src/FamilyTree.Web
dotnet user-secrets set "Google:ClientId"      "<id>"     --project src/FamilyTree.Web
dotnet user-secrets set "Google:ClientSecret"  "<secret>" --project src/FamilyTree.Web

# Email (optional — omit SmtpHost to use console-log fallback in dev)
dotnet user-secrets set "Email:SmtpHost"    "<host>"    --project src/FamilyTree.Web
dotnet user-secrets set "Email:SmtpPort"    "587"       --project src/FamilyTree.Web
dotnet user-secrets set "Email:EnableSsl"   "true"      --project src/FamilyTree.Web
dotnet user-secrets set "Email:Username"    "<user>"    --project src/FamilyTree.Web
dotnet user-secrets set "Email:Password"    "<pass>"    --project src/FamilyTree.Web
dotnet user-secrets set "Email:FromAddress" "<from>"    --project src/FamilyTree.Web
dotnet user-secrets set "Email:FromName"    "ArborKin"  --project src/FamilyTree.Web
```

Azure App Service config keys (double-underscore for nested):
`Email__SmtpHost`, `Email__SmtpPort`, `Email__EnableSsl`, `Email__Username`, `Email__Password`, `Email__FromAddress`, `Email__FromName`

## Architecture Overview

Three-tier .NET 10 / C# 13 application — **no HTTP boundary between Web and Core**:

```
FamilyTree.Web  (Blazor Server + MudBlazor)
    ↓ direct service injection (no REST)
FamilyTree.Core  (Service layer + EF Core)
    ↓
SQL Server (local) / Azure SQL Database (prod)
```

**Projects:**
- `FamilyTree.Shared` — DTOs and enums only; no business logic; shared by Core and Web
- `FamilyTree.Core` — Service layer, EF Core data access, blob storage abstraction; referenced directly by Web
- `FamilyTree.Web` — Blazor Server UI; injects `IPersonService` etc. directly; no HTTP client

**CI/CD:** Two separate workflows. `ci.yml` runs build + test automatically on every push/PR to `master`/`main`. `deploy-web.yml` is **manual only** (`workflow_dispatch`) — merging to `master` never deploys by itself; deploying to Azure App Service is a deliberate, separate trigger. The deploy workflow requires `AZURE_WEB_APP_NAME` and `AZURE_WEB_PUBLISH_PROFILE`/`AZURE_CREDENTIALS` secrets in the GitHub repo, and pushes straight to the live App Service (no deployment slot/swap, no health gate). EF migrations run automatically on every Web startup (dev and prod alike) via `ctx.Database.MigrateAsync()` in `Program.cs`, wrapped in try/catch: a failed migration logs `LogCritical`, best-effort emails `Ops:AlertEmail` (falls back to `SuperUser:Email`) with the exception via `IEmailSender`, then rethrows — the app deliberately fails to start rather than serve requests against a schema the code doesn't match.

## Key Architectural Decisions

### JS-Free Tree Layout (ADR 001)
All tree node positions are computed in C# (`FamilyTreeLayoutEngine`) before rendering — no post-render DOM measurement. Nodes use `position:absolute` at pre-calculated (X, Y) coordinates; SVG connectors use the same coordinates.

Current layout constants in `FamilyTreeLayoutEngine.cs`:
- `NodeSpacingX = 120` — horizontal gap between node centers
- `SpouseSpacingX = 200` — horizontal distance between couple nodes
- `FocusSize = 80` / `Gen1Size = 70` / `DefaultSize = 60` — node diameters
- `PxPerYear = 6.5` — vertical pixels per birth year (Y axis is a real timeline)

### Blazor Owns Position, JS Owns Gesture
Canvas pan/zoom state lives in JS (`canvas-interaction.js`). Drag positions for floating widgets (toolbar, hero overlay) live in Blazor C# state. JS calls `[JSInvokable] OnDragEnd(key, left, top)` on mouseup; Blazor re-renders with new position. No JS/Blazor conflicts.

### Canonical Relationship Ordering
For bidirectional relationship types (Spouse, Sibling), `PersonAId < PersonBId` is always enforced. A unique DB constraint on `(PersonAId, PersonBId, Type)` prevents duplicates.

### Former/Active Spouse Distinction
`Relationship.EndDate` (nullable `DateOnly`) distinguishes active spouses (`EndDate = null`) from former spouses (`EndDate` set). `PersonDto` exposes both `SpouseIds` and `FormerSpouseIds`. The canvas renders former couples with a dashed grey 💔 connector vs. solid green ❤.

### Cross-Root Couple Layout
When a child of one root group marries a child of another (e.g. siblings-in-law), `FamilyTreeLayoutEngine` detects the cross-root couple, removes it from the recursive placement loop, places each partner as a leaf under their own parent group (at the inner edges), then positions their children at the couple's midpoint in a post-placement pass. Prevents connector tangles.

### SSR Flash Prevention
`Home.razor` uses a `_ready` flag to suppress tree rendering until after `OnAfterRenderAsync` reads `localStorage`. During SSR (no JS), the flag stays false and a spinner shows. After SignalR connects, `OnAfterRenderAsync` reads `ft-focus` from localStorage, sets the correct focus person, flips `_ready = true`, and calls `StateHasChanged()`. This prevents the brief flash of an alphabetically-first person before the user's saved focus loads.

### Mobile/Tablet Responsive Pattern (≤768px)
Most responsive behavior (AppBar swapping search for a hamburger + centered user identity, drawers going full-width, etc.) is pure CSS via a single `@media (max-width: 768px)` block in `app.css` — no Blazor/JS involvement. Two pieces need more than CSS:

- **Toolbar default state**: `CustomToolbar` doesn't know the viewport size itself. `ftDrag.js`'s `watchViewport()` reports `window.innerWidth` to `Home.razor` via `[JSInvokable] OnViewportWidthChanged`, which sets `_isNarrowViewport` and passes it down as `CustomToolbar.ForceCollapsed`. `ForceCollapsed` only seeds the *default* `_collapsed` state when it changes (crossing the breakpoint) — it doesn't permanently lock out the user's own expand/collapse toggle. `watchViewport()` always re-targets the latest `dotNetRef` and forces one immediate report per call rather than gating on a "already watching" flag — a stale reference there previously meant later page instances (after a reconnect/navigation) silently never got notified.
- **Expanded toolbar mobile styling**: `.ft-toolbar-full` (not `.ft-toolbar-mini`) sets its own `position: fixed` inside the mobile media query to dock as a full-width footer, independent of whatever position the parent `#ft-toolbar` div (used for the collapsed pill) has.

### Document Import Pipeline
Every import format normalizes to plain text and funnels through one Claude extraction prompt (`ClaudeImportService.ExtractFromTextAsync`), so all formats get identical preview/approve/commit/rollback behavior for free:
- **Paste text** tab calls `ExtractFromTextAsync` directly.
- **PDF / Document** tab calls `ExtractFromDocumentAsync(bytes, fileName)`, which extracts text first (iText7 for `.pdf`, raw UTF-8 decode for `.txt`) then hands off to the same `ExtractFromTextAsync` path. `.doc`/`.docx` aren't supported yet — throws `NotSupportedException` telling the user to paste text instead.
- **GEDCOM** and **Excel/CSV** tabs are still UI stubs (`ProceedAsync` just shows a toast) — no extraction wired up.

Approval and rollback: `CommitAsync` only persists people the user left checked in the preview, and tags every created `Person` with an `ImportBatchId`. `RollbackBatchAsync` soft-deletes (`DeletedAt`) every person in that batch plus any relationship where *both* sides were in the batch — nothing is hard-deleted, so an import is always reversible from the batch list.

**Security note:** `UglyToad.PdfPig` was the initial choice for PDF text extraction but was rejected — its NuGet version history jumps straight from `0.1.9-alpha001-patch1` to a non-standard `1.7.0-custom-5` prerelease, a pattern consistent with a hijacked maintainer account pushing a malicious release. Check any package's version history before adding it if something about the version number looks off.

**Known gotcha — MudBlazor drawer width overrides:** `MudDrawer`'s closed state is `right: calc(-1 * var(--mud-drawer-width))`, not `width: 0`. Overriding the rendered `width` alone (e.g. `.ft-person-drawer { width: 100% !important; }`) leaves the *closed-state offset* still using the original `Width="…"` Razor parameter, so the drawer only shifts off-screen by its old width while actually rendering at the new one — leaving the difference visible as a blank box. Always override the CSS variable MudBlazor itself reads (`--mud-drawer-width`), not the raw `width` property.

### Blazor Component Responsibilities
| Component | Role |
|-----------|------|
| `FamilyTreeCanvas` | Calls `LayoutEngine.ComputeLayout()`, renders SVG connectors + nodes; emits `OnPersonSelected` |
| `PersonNode` | Presentational circle node — initials, name, years. Purely presentational. |
| `PersonDetailDrawer` | Read-only side drawer; emits `OnEdit`, `OnDelete`, `OnFocusPerson` |
| `PersonForm` | All form fields for add and edit; shared by `PersonAdd` and `PersonEdit` |
| `Home.razor` | Orchestrator — owns all state, dialog invocation, focus, drag positions |
| `HeroOverlayComponent` | Floating info card (focus person stats); draggable |
| `CustomToolbar` | Floating toolbar (zoom, center, reset); draggable |
| `ConfirmDialog` | Generic destructive-action confirmation dialog |
| `SiblingInferenceDialog` | Offers to link additional siblings when a new sibling is added |
| `LoginOverlay` | Full-page auth card; handles email/password POST and Google OAuth redirect |
| `Register.razor` | Invite-aware registration page; reads `?invite=<token>` query param |
| `Admin.razor` | Admin panel: dashboard stats, deleted persons, user management, audit log, activity, stories management |
| `StatCard` | Dashboard stat tile; accepts `Icon`, `Accent`, `Href`, `Subtitle` parameters |
| `CustomAppBar` | Top nav; desktop icon row vs. mobile hamburger + centered user identity, both pure CSS breakpoint-driven |
| `Stories.razor` | `/stories` — family-wide feed of approved, non-hidden stories; "Add story" opens `StoryFormDialog` |
| `StoryFormDialog` | Compose a story directly; optional `PersonId` (locked) or person-picker + "not in the tree yet" checkbox for a free-text name |
| `StoryInviteDialog` | Email someone a token link to write a story about a person; same locked-vs-picker pattern as `StoryFormDialog`, plus a "Not them? Invite about someone else" override even when a person is preset |
| `StoryRespond.razor` | Unauthenticated `/story/respond/{token}` page where invite recipients submit their memory; ends in an account-creation CTA |
| `AdminStoryEditDialog` | Admin-only title/body edit for an existing story |
| `Import.razor` | `/import` — thin page wrapper around `ImportFormPanel` |
| `ImportFormPanel` | Tabbed import UI (GEDCOM/PDF/Excel/paste-text) shared by `/import` and the Admin Imports tab; owns the extract→preview→approve→commit state machine and the import-batch history table |

## Domain Model

**Core entities:** `Person`, `Relationship`, `Medium`, `Family`, `Story`, `StoryInvite`, `ImportBatch`

- `Family`: tenant/group container (`Id`, `Name`); all persons belong to one family via nullable `FamilyId` FK
- `Person`: name fields, dates/places, `Gender` enum, `BiographyNotes` (5000 chars), `ProfilePhotoUrl` (500 chars), `FamilyId` (nullable), audit fields, SQL `RowVersion`
- `Relationship`: bidirectional link with `Type` enum (Parent, Spouse, Sibling, Adopted), optional `StartDate`/`EndDate`, unique constraint on `(PersonAId, PersonBId, Type)`
- `Medium`: photo/media file linked to a Person (cascade delete on person)
- `Story`: prose narrative about a `Person` (nullable `PersonId` — see below) or a free-text `UnlinkedPersonName`. `AuthorId` (nullable, set null on user delete) for self-authored stories; `AuthorName` free-text fallback for anonymous invite-flow submissions. `IsApproved` gates the public `/stories` feed (always `true` for self-authored via `StoryService.CreateAsync`, always `false` for invite responses pending moderation). `IsHidden` lets an admin take an already-approved story down without losing its approval state. `SortOrder` (admin up/down reorder, swaps with the adjacent row in current display order) plus `CreatedAt` as the tiebreak define `/stories` ordering.
- `StoryInvite`: token-based invite (URL-safe 64-byte random token, 30-day default TTL via `Stories:InviteTtlDays`) emailing someone a no-login link to write a `Story` about a `Person` or free-text name. `IsUsed` makes `SubmitResponseAsync` idempotent on the token.
- **Unlinked stories**: both `Story` and `StoryInvite` allow `PersonId == null` with `UnlinkedPersonName` set instead — the subject doesn't have to exist in the tree yet. Admin's Stories tab has a dedicated linking queue with a "+ Add someone new…" option in the person-search dropdown that opens `/people/add?name=<typed name>` in a new tab, pre-filling the name.
- `ImportBatch`: groups every `Person` created by one AI-assisted import run (`PersonCount`/`RelationshipCount` totals, `RolledBackAt` when undone). See [Document Import Pipeline](#document-import-pipeline) above.

**DTOs (in `FamilyTree.Shared`):**
- `PersonDto` — read model; `FullName`, `Age`, `IsDeceased` computed; derived ID lists `ParentIds`, `ChildIds`, `SpouseIds`, `FormerSpouseIds`, `SiblingIds` populated by `PersonMapper`
- `PersonUpsertDto` — write model for create/update; includes `FormerSpouseIds`
- `CoupleDto` — derived at render time by `CoupleHelper.Derive()`; carries `IsFormer` flag for connector styling
- `StoryDto` / `StoryUpsertDto` — read/write models for `Story`; `AuthorDisplayName` (from the `Author` nav property) falls back to `AuthorName` (free-text), then `"Unknown"` in the UI
- `StoryInviteCreateDto` / `StoryInviteValidationDto` / `StoryInviteResponseDto` — invite creation, token validation, and response submission

## Service & Data Access Patterns

- All service methods return `ServiceResponse<T>` — check `.Success` before using `.Data`
- Use `ServiceResponse.Ok(data)` / `ServiceResponse.Fail(message)` static factories
- `IDbContextFactory<AppDbContext>` is used for scoped, thread-safe contexts (not a singleton DbContext)
- `PersonMapper` enriches `PersonDto` with derived relationship ID lists — not persisted
- `PersonService.SyncRelationshipsDiffAsync` handles create/update/delete of all relationship types including spouse ↔ former-spouse transitions

## Auth & Security

### Identity Stack
ASP.NET Core Identity (`AppUser : IdentityUser<Guid>`) with cookie auth (`IdentityConstants.ApplicationScheme`). `AppUserClaimsPrincipalFactory` injects `DisplayName`, `PersonId`, and role claims into the cookie.

### Registration Modes
Controlled by `Auth:RegistrationMode` in config:
- `Open` — anyone can register (used in `appsettings.Development.json`)
- `InviteOnly` — requires a valid `UserInvite` token in the registration URL (production default)
- `Closed` — registration disabled entirely

### Invite Flow
`IAuthService.CreateInviteAsync(email)` generates a URL-safe base64 token, stores it in `UserInvites`, and returns it. Admin constructs `/register?invite=<token>`. Token TTL is `Auth:InviteTtlDays` (default 7). `CreateInviteAsync` auto-creates a `Family` row if none exists.

### Rate Limiting & Lockout
- `/auth/do-login` is protected by a fixed-window rate limiter: 5 requests per 15 min per IP (`RequireRateLimiting("login")`)
- Identity lockout: 5 failed attempts → 15-minute account lock (`lockoutOnFailure: true`)
- Error codes passed via `?loginError=` query param: `invalid`, `missing`, `locked`, `toomany`, `noinvite`, `closed`, `google_error`, `google_unavailable`

### Google OAuth
Registered conditionally — only when both `Google:ClientId` and `Google:ClientSecret` are non-empty (prevents startup errors in environments without credentials). Flow: `/auth/google` → Google → `/auth/google-callback`. Callback handles three cases: existing external login, existing email account needing link, new account (mode-checked). Add credentials via user secrets locally; via App Service config in Azure.

### Super-user Bootstrap
On startup, if `SuperUser:Email` config is set, that `AppUser` is promoted to `IsSuperUser = true` idempotently. Super-users cannot have their role changed via the Admin UI.

### Dev Auth Bypass
`DevAuth:Enabled = true` in `appsettings.Development.json` activates a fake auth handler that signs in a synthetic admin user without a password. Disable before deploying.

## Configuration

- **Local DB**: `Server=localhost\SQLEXPRESS;Database=FamilyTreeDb;Trusted_Connection=True;TrustServerCertificate=True` — stored in user secrets, not `appsettings.json`
- **User secrets**: All credentials (DB connection string, SuperUser email, Google OAuth, Azure Storage) are stored via `dotnet user-secrets` for local dev. See Build commands above.
- **Dev seeding**: `DataSeeder.Seed()` runs on startup in development — creates a "My Family" row and a sample five-generation tree
- **Blob storage**: `BlobStorageService` / `IBlobStorageService` abstracted; falls back to `UseDevelopmentStorage=true` (Azurite) if `AzureStorage:ConnectionString` is not set
- **Azure App Service config keys**: Use double-underscore for nested keys — e.g. `ConnectionStrings__DefaultConnection`, `Google__ClientId`
- **`Ops:AlertEmail`** (optional): recipient for the migration-failure notification email; falls back to `SuperUser:Email` if unset. A failed send never masks the original migration exception — see CI/CD above.

## Theme System

`ThemeService` (singleton) manages dark/light mode and persists the preference to `localStorage` via `IJSRuntime`. MudBlazor theme provider + custom CSS variables (`--ft-green-600`, `--ft-surface`, etc.). All pages use `@rendermode InteractiveServer`.
