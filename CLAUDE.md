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
```

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

**CI/CD:** GitHub Actions (`.github/workflows/deploy-web.yml`) builds and deploys to Azure App Service on push to `master` or `main`. Requires `AZURE_WEB_APP_NAME` and `AZURE_WEB_PUBLISH_PROFILE` secrets in the GitHub repo. EF migrations run automatically on Web startup via `ctx.Database.MigrateAsync()` in production.

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
| `Admin.razor` | Admin panel: dashboard stats, deleted persons, user management, audit log, activity |
| `StatCard` | Dashboard stat tile; accepts `Icon`, `Accent`, `Href`, `Subtitle` parameters |

## Domain Model

**Core entities:** `Person`, `Relationship`, `Medium`, `Family`

- `Family`: tenant/group container (`Id`, `Name`); all persons belong to one family via nullable `FamilyId` FK
- `Person`: name fields, dates/places, `Gender` enum, `BiographyNotes` (5000 chars), `ProfilePhotoUrl` (500 chars), `FamilyId` (nullable), audit fields, SQL `RowVersion`
- `Relationship`: bidirectional link with `Type` enum (Parent, Spouse, Sibling, Adopted), optional `StartDate`/`EndDate`, unique constraint on `(PersonAId, PersonBId, Type)`
- `Medium`: photo/media file linked to a Person (cascade delete on person)

**DTOs (in `FamilyTree.Shared`):**
- `PersonDto` — read model; `FullName`, `Age`, `IsDeceased` computed; derived ID lists `ParentIds`, `ChildIds`, `SpouseIds`, `FormerSpouseIds`, `SiblingIds` populated by `PersonMapper`
- `PersonUpsertDto` — write model for create/update; includes `FormerSpouseIds`
- `CoupleDto` — derived at render time by `CoupleHelper.Derive()`; carries `IsFormer` flag for connector styling

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

## Theme System

`ThemeService` (singleton) manages dark/light mode and persists the preference to `localStorage` via `IJSRuntime`. MudBlazor theme provider + custom CSS variables (`--ft-green-600`, `--ft-surface`, etc.). All pages use `@rendermode InteractiveServer`.
