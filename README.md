# FamilyTree

[![CI](https://github.com/shalant/FamilyTree/actions/workflows/ci.yml/badge.svg)](https://github.com/shalant/FamilyTree/actions/workflows/ci.yml)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dot-net)](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

### Test Status
| Metric | Status |
|--------|--------|
| **Core Tests** | 75/75 ✅ |
| **Web Tests** | 41/45 ✅ (4 skipped) |
| **Total** | 116 passing |
| **Build (Debug + Release)** | ✅ |
| **CI Pipeline** | ✅ [View Latest Run](https://github.com/shalant/FamilyTree/actions/workflows/ci.yml) |

A full-stack family tree application built with **Blazor Server (.NET 10)**, **EF Core**, **SQL Server**, and **MudBlazor** — featuring a custom C# layout engine, smooth pan/zoom canvas, multi-tenant family isolation, invite-only auth, and an admin panel with audit logging.

---

## Screenshots

### 1. Full Family Tree Canvas

![FamilyTree Hero](Screenshots/Home.png)

Interactive canvas with SVG connectors, focus node highlight, and draggable hero overlay — light mode.

---

### 2. Dark Mode Tree

![FamilyTree Dark Mode](Screenshots/Home_Dark.png)

Dark mode variant showing the token-driven theme system and glass surfaces.

---

### 3. Person Detail Drawer

![Person Detail Drawer](Screenshots/PersonEditDrawer.png)

Side drawer with edit / delete / focus-person actions while the live tree remains visible behind it.

---

### 4. Add Person Form

![Add Person Form](Screenshots/PersonAdd.png)

Shared form component used for both add and edit; includes date validation, photo upload, and relationship selectors.

---

### 5. Import Family Data

![Import Family Data](Screenshots/Import_Dark.png)

GEDCOM / PDF / CSV / paste-text import wizard in dark mode; previews all relationships before committing.

---

### 6. Dashboard

![Dashboard](Screenshots/Dashboard.png)

"Your Family Tree" hub — stats, quick actions, recently added members, export options, and community links.

---

### 7. Admin Panel

![Admin Panel](Screenshots/Admin.png)

Admin panel: invite management, user roles, linked-person assignment, and audit tabs.

---

## Development Time

Built from initial concept to a fully interactive, themed, multi-tenant application in a compressed timeline. Features delivered include a custom layout engine, auth stack, admin panel, media uploads, CI/CD pipeline, and a test suite — demonstrating end-to-end full-stack delivery speed.

---

## Features

**Canvas & Tree**
- Pan + zoom with physics-based clamping; position state lives in JS, layout computed in C#
- Custom `FamilyTreeLayoutEngine` — all node (X, Y) positions calculated server-side before render; no DOM measurement
- Y-axis is a real birth-year timeline; SVG connectors for parents, spouses, children
- Former spouses rendered with dashed connectors; cross-root couple layout without connector tangles
- Focus mode persisted to `localStorage`; SSR flash prevention via `_ready` flag

**Auth & Security**
- ASP.NET Core Identity with cookie auth and optional Google OAuth
- Registration modes: `Open`, `InviteOnly` (production default), `Closed`
- Token-based invite flow with configurable TTL; admin-generated invite URLs
- Login rate limiting (5 req / 15 min per IP) + Identity account lockout
- `ICurrentUserService` interface in Core, implemented in Web — no `HttpContext` in the service layer
- Super-user bootstrap on startup via config (idempotent)

**Multi-tenant Family Isolation**
- `FamilyId` claim baked into auth cookie at sign-in
- All person queries scoped to the caller's family; super-users bypass scoping to see all data
- Audit fields (`CreatedBy`, `UpdatedBy`, `DeletedBy`, `FamilyId`) stamped on every write

**Admin Panel**
- Dashboard stats, daily activity chart, deleted-persons restore, user management
- Audit log with entity type / action / timestamp; activity counts tracked per user per day

**Data**
- Soft delete on `Person`, `Relationship`, `Medium` — global EF query filters
- `Relationship` canonical ordering: lower `Guid` is always `PersonAId`; unique DB constraint prevents duplicates
- `Relationship.EndDate` distinguishes active from former spouses
- Filtered performance indexes on `People`, `Relationships`, `AuditLogs`
- EF migrations run automatically on startup via `MigrateAsync()`

**Media & Export**
- Azure Blob Storage for photo uploads; falls back to Azurite for local dev
- SVG tree export (full canvas), CSV and JSON person export

**CI/CD**
- GitHub Actions CI: build + test on every push to `master`
- Manual deploy to Azure App Service (B1 Linux) via `workflow_dispatch`

---

## Architecture

Single-service design — no HTTP boundary between UI and data layer:

```
FamilyTree.Web   (Blazor Server + MudBlazor — Azure App Service B1)
     ↓  direct service injection
FamilyTree.Core  (Service layer + EF Core)
     ↓
SQL Server (local SQLEXPRESS) / Azure SQL Database (prod)
```

**Projects:**
- `FamilyTree.Shared` — DTOs and enums; no logic; shared by Core and Web
- `FamilyTree.Core` — Services, EF Core, blob storage, migrations
- `FamilyTree.Web` — Blazor Server UI; injects `IPersonService` etc. directly

Key design decisions are documented in `docs/` (ADR 001: JS-free layout engine, relationship canonical ordering, cross-root couple layout, SSR flash prevention).

---

## Quality Strategy

Confidence in code comes from rigorous constraints, not subjective review. This project follows **Uncle Bob Martin's approach**: surround agents (and developers) with a gauntlet of automated checks that catch bugs early.

**Constraints in place:**
- **Comprehensive test suites** — 75 Core + 41 Web tests; new features come with regression test pairs (before/after)
- **Strict type safety** — C# 13 nullable reference types; compiler catches entire categories of null-reference bugs
- **CI enforcement** — `dotnet build` + `dotnet test` must pass on every push (no manual override path)
- **Database integrity** — unique constraints, referential integrity, soft-delete cascade tracking; data shape enforced by the schema
- **Build verification** — Release builds verified locally before pushing; Warnings treated as errors

**Result:** The test suite is comprehensive enough that human code review is optional — if code passes the gauntlet, it works.

---

## Testing

| Test Type | Framework | Runs | Command |
|-----------|-----------|-------|---------|
| **Unit Tests** (Core services) | xUnit + FluentAssertions | ✅ CI on every PR | `dotnet test` |
| **Component Tests** (Blazor) | bUnit | ✅ CI on every PR | `dotnet test` |
| **UI Tests** (E2E) | Playwright | 🖥️ Local only | `dotnet run` + `dotnet test` |

**Unit & Component Tests** — Automated in CI
- Run on every PR and push to any branch
- In-memory EF Core provider; each test gets isolated database
- ~2–3 min total

**UI Tests** — Manual local testing
- Require running app server + browser
- Run locally before pushing: `dotnet watch` + `dotnet test --filter "UiTests"`

### Core Test Coverage

**`PersonServiceTests` (8 tests)**
- Create returns person with correct name
- `CreatedBy` and `FamilyId` are stamped on every create
- Whitespace-only first name is rejected
- Birth date after death date returns a failure message
- Soft delete sets `DeletedAt` + `DeletedBy`; record invisible to normal queries
- Restore clears those fields; record becomes visible again
- Family scoping: user with `FamilyId = X` only sees family X
- Super-user with no `FamilyId` sees all persons across families

**`RelationshipServiceTests` (3 tests)**
- Canonical ordering: lower GUID always stored as `PersonA`
- Duplicate relationship creation returns failure (not DB exception)
- Delete hard-removes relationship record

**`ComponentTests` (4 tests, 4 skipped)**
- FamilyTreeCanvas renders without errors
- HeroOverlay displays when visible
- ToastService shows notifications
- PersonDetailDrawer (skipped — complex MudBlazor dependencies)

**`StoryTests` (3 tests)**
- Story submission validation
- Invite token expiry checks
- Auth focus state persistence

### Run Tests Locally

```bash
# Unit + component tests (same as CI)
dotnet test FamilyTree.sln

# UI tests (requires running server)
dotnet watch  # Terminal 1
dotnet test --filter "UiTests"  # Terminal 2
```

---

## Project Structure

```
FamilyTree/
├── src/
│   ├── FamilyTree.Shared/        # DTOs, enums (no logic)
│   ├── FamilyTree.Core/          # Services, EF Core, migrations, blob storage
│   └── FamilyTree.Web/           # Blazor Server UI, auth endpoints
│       ├── Components/           # Canvas, PersonNode, drawers, dialogs, admin
│       ├── Services/             # CurrentUserService, ThemeService, etc.
│       └── wwwroot/              # CSS, JS (canvas-interaction.js only)
├── tests/
│   ├── FamilyTree.Core.Tests/    # Service-layer tests (xUnit + InMemory EF)
│   └── FamilyTree.Web.Tests/     # Component tests (bUnit)
├── docs/                         # ADRs, build plan, deployment, next steps
└── .github/workflows/            # ci.yml (test on push), deploy-web.yml (manual)
```
