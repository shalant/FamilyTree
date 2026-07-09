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
When a descendant of one root group marries a descendant of another (e.g. siblings-in-law) — at **any** nesting depth, not just a direct child of the root — `FamilyTreeLayoutEngine` detects the cross-root couple, removes it from the recursive placement loop, places each partner as a leaf under their own parent group (at the inner edges), then positions their children at the couple's midpoint in a post-placement pass. Prevents connector tangles.

Detection walks a `descendantOfRoot` map built by following the same `primaryGroup`-driven recursion `PlaceGroup` itself performs — not a single-hop "is this person literally in a root's own `ChildIds`" check. Found 2026-07-07: Marc (Bud+Florence's son) married Ellen (Ray+Rose's daughter) — a cross-root marriage the engine already detected correctly while Bud+Florence's group was itself a root. Once Dora (Bud's mother) was restored, Bud became Dora's recorded child, nesting Bud+Florence's group one level down — Marc was now a *grandchild* of a root, not a direct child, so the old single-hop check silently stopped firing. Both Dora's subtree and Ray+Rose's subtree then independently tried to place Marc and Ellen via ordinary recursion, and whichever ran first hijacked the other's position, sprawling Marc's connector across the canvas toward Ray+Rose's branch. See `FamilyTreeLayoutEngineTests.CrossRootCouple_StillDetectedWhenOneSideIsNestedUnderAGrandparent`.

Note: the "sort children to the inner edge" cosmetic step (part 3 of the fix above) only reorders a root's own direct `ChildIds` — for a deep couple like Marc it's a no-op (he isn't literally in Dora's `ChildIds`), not actively wrong. Actual position-correctness comes from removing the couple from `primaryGroup` plus the existing midpoint-placement pass, not from that cosmetic sort — so a deep cross-root couple lands correctly but without the same "touching inner edge" polish a depth-1 cross-root couple gets. Acceptable known limitation, not yet generalized.

### Root-Group Placement Order
Root nuclear groups (couples/individuals with no parent of their own) render left-to-right in a **stable** order derived from each group's own identity: `BuildNuclearGroups` sorts couples by the minimum position of the two partners in the (alphabetically-ordered) people list — never by whichever descendant happens to trigger the couple's discovery. This matters because a person transitions from "root individual" (positioned by simple append order) to "root couple" (positioned by this sort) the instant they gain a spouse; before this was fixed (2026-07-06), that transition's position was decided by whichever of the couple's *children* had the alphabetically-earliest last name (an artifact of `CoupleHelper.Derive`'s dictionary insertion order), not by the couple's own identity — so marrying an existing orphan-sibling root could send their entire nuclear group to the far side of the tree. See `FamilyTreeLayoutEngineTests.MarryingAnOrphanSiblingRoot_DoesNotJumpTheirGroupPastUnrelatedRootsByAlphabeticalAccident`.

Separately, root groups are **deliberately never reordered by sibling relationships** once placed — an earlier version tried clustering siblings together, which caused worse instability (moving already-placed people whenever a new sibling joined, breaking down entirely once one sibling married). See the in-code comment above the "place roots left to right" loop in `ComputeLayout` for the full reasoning.

The cross-root-couple reorder (see below) had the *same* class of bug: it picked which of the two bridged root groups was "rgA" (the anchor the other group gets inserted next to) by comparing the married couple's own canonical `PersonAId`/`PersonBId` GUIDs — arbitrary and unrelated to genealogy or rendering order. With a third, unrelated group also in the mix, this could occasionally shove that third group to the very front of the tree depending purely on whether one spouse's random GUID happened to be lower than the other's (caught as a **flaky** regression test, 2026-07-06 — passed most runs, failed others with the same inputs). Fixed by anchoring `rgA`/`rgB` on each root group's *current position* in the already-stably-sorted `rootGroups` list instead.

**A person with two recorded spouses** (real, messy genealogy: a widow/widower's second marriage — e.g. Florence, married to Bud with their child Marc, *and* to a second husband Harvey Fleishman with no children together and no `EndDate` recorded to mark the first marriage as former) exposed a *third* instance, found 2026-07-07. `primaryGroup` correctly prefers whichever of a person's groups has children (fixed the same day — a childless second marriage must never win the slot that anchors real children just by sorting alphabetically first). But `rootGroups` still treated the *other*, non-primary group (Harvey+Florence) as its own independent root-placement candidate, so it could `SetNode` Florence's position first — via the wrong anchor — before her real family (with Marc) was ever processed, sending Marc's position far from where he belongs. Fixed by requiring a group be the primary group of **every** parent it has (not just at least one — Harvey has no competing group, so his trivially-primary childless group would otherwise still pass an "at least one" check). The person left over (Harvey) is then anchored next to his already-placed spouse in the safety-net placement pass, the same pattern as the sibling-anchor fix, rather than being dumped at the far edge of the canvas. See `FamilyTreeLayoutEngineTests.PersonWithTwoSpouses_ChildAnchorsUnderTheMarriageThatActuallyHasChildren`.

### Where Layout Bugs Hide: Classification Transitions
`FamilyTreeLayoutEngine.ComputeLayout` is a pure function — it fully recomputes every position from scratch on every call, off whatever `people`/`couples` data it's given. There's no incremental/partial-update state to go stale, so every layout bug found this session came from the same underlying shape: an edit that changes **which structural bucket a person falls into**, exposing that the deterministic sort/placement rules weren't actually invariant across that specific transition. Known transitions and their fixes:
- Person gains their first Sibling link with no shared parent on the tree, where the sibling is deep inside an existing nuclear family rather than a root themselves (root-individual sibling-anchor fix, 2026-07-06)
- Root individual gains a spouse — "individual" → "couple" (root-group-ordering fix, 2026-07-06)
- Person gains a *second* spouse/marriage — "one group" → "two groups, one primary" (primary-group children-preference + root-groups-must-be-primary-for-every-parent fix, 2026-07-07)
- A cross-root couple's own branch stops being a root itself — "direct child of a root" → "descendant of a root at any depth" (cross-root detection generalized from a single-hop check to a full descendant walk, 2026-07-07)

**Rule of thumb for future work**: any new feature that lets a person cross one of these classification lines needs its own `FamilyTreeLayoutEngineTests` regression test built as a before/after pair — same people, only the one edit differing — not just a static one-shot scenario. That's the fastest way to catch the next transition-shaped bug before it reaches production, rather than after a user stumbles into it live.

### Post-Signup Tree Linking (MVP) — "Willa scenario"
When a new user signs up after being invited to write a story about someone who isn't on the tree yet (e.g. Willa writing about her father Bill, where neither has a `Person` row), `LinkToTreeModal` walks her through a **subject-first** linking flow instead of asking about herself directly:

1. `Register.razor` (`OnInitializedAsync`) looks up the pending unlinked story for the invited email via `StoryService.GetByAuthorEmailAsync` (matches by `Story.Author.Email` for self-authored stories, or `Story.Invite.InvitedEmail` for anonymous invite-response stories) and takes the most recent story where `PersonId == null`. Its `UnlinkedPersonName` ("Bill Small") and `Id` are passed into `LinkToTreeModal` as `StorySubjectName`/`StoryId`. `Home.razor`'s recovery path (`ShowLinkingModalAsync`, triggered when a signed-in user still has `PersonId == null`) does the same lookup — this is the "catch state" for a user who closed the modal early.
2. When `StorySubjectName` is present, the modal asks about **Bill** first: "Is Bill Small already on this family tree?" → either pick him from existing persons, or (if not found) "Is Bill Small related to anyone on this tree?" → pick an anchor person and describe Bill's relationship to them. `AuthService.CreateUnlinkedPersonAsync` creates Bill as a standalone `Person` (not tied to any user) linked to that anchor via `Relationship`.
3. Only once Bill exists (found or newly created) does the modal ask "How are you related to Bill Small?" — `AuthService.LinkUserToTreeAsync` creates Willa's own `Person`, links her to Bill, and sets `user.PersonId`. The modal then calls `StoryService.LinkToPersonAsync` to link the original pending story to Bill's person.
4. If no pending story exists (plain signup, no invite), the modal falls back to the generic flow: "Are you already on this family tree?" → either pick yourself, or describe your relationship to an existing anchor person, skipping the subject-first steps entirely.

**Relationship direction** is never left ambiguous: since `Relationship` rows encode `PersonAId = parent`, `PersonBId = child` for `Type.Parent` (see `PersonMapper`), every dropdown offers explicit directional tokens (`"ParentOfConnected"` / `"ChildOfConnected"` / `"Spouse"` / `"Sibling"`) resolved by `AuthService.ResolveRelationshipDirection` — there is no single ambiguous "Parent" option.

**Auto sign-in**: `AuthService.RegisterAsync` only creates the account — Blazor Server can't set an auth cookie from within a SignalR circuit. After the modal completes, `Register.razor` calls the existing `window.ftSubmitLogin(email, password)` JS helper (a real hidden-form POST to `/auth/do-login`, the same endpoint `LoginOverlay` uses) so the browser actually becomes authenticated as the new user — otherwise whichever account was previously logged in in that browser stays active and the hero overlay/tree keep showing the old user.

### Pre-Auth Pages Use `AuthLayout`, Not `MainLayout`
`Register.razor`, `ForgotPassword.razor`, `ResetPassword.razor`, and `StoryRespond.razor` all use `@layout AuthLayout` instead of the default `MainLayout`. `MainLayout` always renders `CustomAppBar` (search, add-person, people list, stories, dashboard icons) — fine for `Home.razor`, where an unauthenticated user sees `LoginOverlay` as a full-viewport cover that hides the AppBar entirely, but these four pages just render a centered auth card with no full-screen cover, so the AppBar would show through around it — giving someone mid-signup a false "I'm already in the app" signal. `AuthLayout` (in `Layout/AuthLayout.razor`) carries the same Mud providers/theme setup as `MainLayout`/`TreeLayout` but omits `CustomAppBar` entirely. `About.razor`/`Faq.razor` deliberately keep `MainLayout` — they're also linked from within the authenticated app via `CustomAppBar`, so losing navigation there would be a regression for logged-in users reading them.

### SSR Flash Prevention
`Home.razor` uses a `_ready` flag to suppress tree rendering until after `OnAfterRenderAsync` reads `localStorage`. During SSR (no JS), the flag stays false and a spinner shows. After SignalR connects, `OnAfterRenderAsync` reads `ft-focus` from localStorage, sets the correct focus person, flips `_ready = true`, and calls `StateHasChanged()`. This prevents the brief flash of an alphabetically-first person before the user's saved focus loads.

### Mobile/Tablet Responsive Pattern (≤768px)
Most responsive behavior (AppBar swapping search for a hamburger + centered user identity, drawers going full-width, etc.) is pure CSS via a single `@media (max-width: 768px)` block in `app.css` — no Blazor/JS involvement. Two pieces need more than CSS:

- **Toolbar default state**: `CustomToolbar` doesn't know the viewport size itself. `ftDrag.js`'s `watchViewport()` reports `window.innerWidth` to `Home.razor` via `[JSInvokable] OnViewportWidthChanged`, which sets `_isNarrowViewport` and passes it down as `CustomToolbar.ForceCollapsed`. `ForceCollapsed` only seeds the *default* `_collapsed` state when it changes (crossing the breakpoint) — it doesn't permanently lock out the user's own expand/collapse toggle. `watchViewport()` always re-targets the latest `dotNetRef` and forces one immediate report per call rather than gating on a "already watching" flag — a stale reference there previously meant later page instances (after a reconnect/navigation) silently never got notified.
- **Expanded toolbar mobile styling**: `.ft-toolbar-full` (not `.ft-toolbar-mini`) sets its own `position: fixed` inside the mobile media query to dock as a full-width footer, independent of whatever position the parent `#ft-toolbar` div (used for the collapsed pill) has.

**Known gotcha — MudBlazor drawer width overrides:** `MudDrawer`'s closed state is `right: calc(-1 * var(--mud-drawer-width))`, not `width: 0`. Overriding the rendered `width` alone (e.g. `.ft-person-drawer { width: 100% !important; }`) leaves the *closed-state offset* still using the original `Width="…"` Razor parameter, so the drawer only shifts off-screen by its old width while actually rendering at the new one — leaving the difference visible as a blank box. Always override the CSS variable MudBlazor itself reads (`--mud-drawer-width`), not the raw `width` property.

### Bulk Import — Deactivated (2026-07-04)
`/import` (GEDCOM/PDF/CSV/paste-text) is disabled — the route renders a "temporarily
unavailable" placeholder instead of `ImportFormPanel`, and every UI entry point
(AppBar icon + mobile drawer item, Dashboard quick action, Admin's "Imports" tab) has
been removed. **Not deleted** — `ImportFormPanel`, `ClaudeImportService`, `ImportsTab`,
and the `ImportBatch` model are all left in place, just unreferenced, so the feature
can come back later without a rebuild from scratch.

**Why:** hand-entering a few edge-case people (an orphan sibling, a married orphan
sibling, a 3-way sibling cluster) surfaced several rounds of layout-engine bugs this
session (see `Commentary/UnlinkedInviteProblem.md` and the `FamilyTreeLayoutEngineTests`
regression suite) — each fixable one at a time because a human could see and report
each broken rendering as it happened. A bulk GEDCOM import can contain dozens of those
same edge cases (plus others: multiple marriages, adopted children, unknown parents,
disconnected branches) arriving all at once, with no per-item feedback loop. See
`docs/FutureFeatures/bulk-import-deactivated.md` for the planned path back — most
likely dropping the birth-year-based Y-axis timeline in favor of pure generational
depth, which would remove the fragile birth-year inference machinery entirely.

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

## Domain Model

**Core entities:** `Person`, `Relationship`, `Medium`, `Family`, `Story`, `StoryInvite`

- `Family`: tenant/group container (`Id`, `Name`); all persons belong to one family via nullable `FamilyId` FK
- `Person`: name fields, dates/places, `Gender` enum, `BiographyNotes` (5000 chars), `ProfilePhotoUrl` (500 chars), `FamilyId` (nullable), audit fields, SQL `RowVersion`
- `Relationship`: bidirectional link with `Type` enum (Parent, Spouse, Sibling, Adopted), optional `StartDate`/`EndDate`, unique constraint on `(PersonAId, PersonBId, Type)`
- `Medium`: photo/media file linked to a Person (cascade delete on person)
- `Story`: prose narrative about a `Person` (nullable `PersonId` — see below) or a free-text `UnlinkedPersonName`. `AuthorId` (nullable, set null on user delete) for self-authored stories; `AuthorName` free-text fallback for anonymous invite-flow submissions. `IsApproved` gates the public `/stories` feed (always `true` for self-authored via `StoryService.CreateAsync`, always `false` for invite responses pending moderation). `IsHidden` lets an admin take an already-approved story down without losing its approval state. `SortOrder` (admin up/down reorder, swaps with the adjacent row in current display order) plus `CreatedAt` as the tiebreak define `/stories` ordering.
- `StoryInvite`: token-based invite (URL-safe 64-byte random token, 30-day default TTL via `Stories:InviteTtlDays`) emailing someone a no-login link to write a `Story` about a `Person` or free-text name. `IsUsed` makes `SubmitResponseAsync` idempotent on the token.
- **Unlinked stories**: both `Story` and `StoryInvite` allow `PersonId == null` with `UnlinkedPersonName` set instead — the subject doesn't have to exist in the tree yet. Admin's Stories tab has a dedicated linking queue with a "+ Add someone new…" option in the person-search dropdown that opens `/people/add?name=<typed name>` in a new tab, pre-filling the name.

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
- **Frontend components (Blazor pages, admin tabs) must NOT directly use `DbContext` or `IDbContextFactory`. All database queries must go through the service layer.** Create new service interfaces and implementations as needed to expose domain operations.
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
