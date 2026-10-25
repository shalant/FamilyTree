# ArborKin — Family Tree Application

[![CI](https://github.com/shalant/FamilyTree/actions/workflows/ci.yml/badge.svg)](https://github.com/shalant/FamilyTree/actions/workflows/ci.yml)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dot-net)](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

Built by [Douglas Rosenberg](https://dougrosenbergdev.com) — full-stack .NET software engineer. See the [portfolio write-up and technical blog post](https://dougrosenbergdev.com/blog/building-family-tree-blazor) on ArborKin's tree layout engine, drag-state handling, and photo upload architecture.

## About ArborKin

**ArborKin** is a full-stack family tree application designed to help extended families reconnect and preserve their stories. Built with **Blazor Server**, **C# 13**, **EF Core**, and **SQL Server**, it provides an interactive canvas for visualizing genealogy, sharing family memories, and maintaining a permanent record of relationships across generations.

The application prioritizes **privacy first** — families control their own data, invited members can contribute stories and photos, and an audit log ensures complete accountability. The architecture emphasizes **reliability through constraints**: comprehensive tests, strict type safety, and database integrity validation catch bugs before production deployment.

Key technical highlights:
- **Custom C# layout engine** computing all node positions server-side without DOM measurement
- **Birth-year timeline Y-axis** for genealogical accuracy and visual continuity
- **Multi-tenant isolation** — each family's data is completely separated; role-based authorization controls edit privileges
- **Soft-delete recovery** — accidentally deleted family members can be restored; cascading soft-deletes preserve referential integrity
- **Invite-first auth** — registration requires a valid family invite token; families control membership
- **Azure deployment-ready** — App Service + SQL Database + Blob Storage; manual deploy gate prevents accidents

Designed as both a genuine family tool (actively used by 5+ family members) and a portfolio piece demonstrating professional full-stack .NET development.

---

## Test Status

| Metric | Status |
|--------|--------|
| **Core Tests** | 287/287 ✅ |
| **Web Tests** (components, layout, services) | 324/329 ✅ (5 skipped) |
| **E2E Tests** (Playwright, real SQL Server) | 18/18 ✅ |
| **Total** | 629 passing (as of 2026-10-25) |
| **Build (Debug + Release)** | ✅ |
| **CI Pipeline** | ✅ [View Latest Run](https://github.com/shalant/FamilyTree/actions/workflows/ci.yml) |
| **Branch Protection** | ✅ Master requires PR + passing CI |

---

## Motivation

This project serves three intertwined goals: **practical**, **educational**, and **professional**.

**Practically**, it's a real tool for a real family. Building software for people you know (rather than hypothetical users) forces you to prioritize genuine usability and reliability. Every feature shipped must work correctly the first time — there's no room for tutorial-code shortcuts.

**Educationally**, it's a deliberate deep-dive into full-stack .NET development. Specifically: relational database design (schema evolution, migration sequencing, scoping strategies, cascade behavior), exploratory testing discipline (understanding when automated tests suffice and when manual testing catches things machines miss), and the architectural decisions that keep code maintainable as complexity grows.

**Professionally**, it's an end-to-end portfolio piece: not a tutorial clone or toy project, but a production-ready application with comprehensive tests, security constraints, role-based access control, and actual deployment infrastructure. It demonstrates the ability to ship a complete system, not just write code that passes a code review.

---

## Screenshots

<div style="display: grid; grid-template-columns: 1fr 1fr; gap: 2rem; margin: 2rem 0;">

<div>

### Tree Canvas (Light)

<img src="Screenshots/Home.png" alt="FamilyTree Hero" style="width: 100%; max-width: 400px;">

Interactive pan/zoom canvas with SVG connectors and draggable focus overlay.

</div>

<div>

### Tree Canvas (Dark)

<img src="Screenshots/Home_Dark.png" alt="FamilyTree Dark Mode" style="width: 100%; max-width: 400px;">

Dark mode with glass-morphism surfaces and token-driven theme.

</div>

<div>

### Person Detail Drawer

<img src="Screenshots/PersonEditDrawer.png" alt="Person Detail Drawer" style="width: 100%; max-width: 400px;">

Edit / delete actions while tree remains interactive behind the drawer.

</div>

<div>

### Add/Edit Form

<img src="Screenshots/PersonAdd.png" alt="Add Person Form" style="width: 100%; max-width: 400px;">

Shared form for create/update with date validation and photo uploads.

</div>

<div>

### Dashboard

<img src="Screenshots/Dashboard.png" alt="Dashboard" style="width: 100%; max-width: 400px;">

Stats, quick actions, activity feed, export options.

</div>

<div>

### Admin Panel

<img src="Screenshots/Admin.png" alt="Admin Panel" style="width: 100%; max-width: 400px;">

Invite management, user roles, linked-person assignment, audit log.

</div>

</div>

---

## What's Shipped

- **Custom layout engine** — All tree node positions computed in C# before rendering; Y-axis is a real genealogical timeline
- **Interactive canvas** — Pan, zoom, focus individual persons; draggable toolbar and info overlays persist to browser storage  
- **Role-based access control** — Invite-only registration, Admin vs. Member distinction, Google OAuth optional
- **Semantic duplicate detection** — Fuzzy name matching (Levenshtein distance, 80% threshold) catches typos and variations when linking new people
- **Family data isolation** — Each family's data completely separated; admins control who can edit/delete; multi-tenant scoping enforced at service layer
- **Story curation** — Family members can submit memories; admins moderate before public visibility
- **Media uploads** — Photos stored in Azure Blob Storage; web-optimized thumbnails
- **Audit trail** — Every create/update/delete logged with timestamp, user, and change summary
- **Admin dashboard** — Stats, activity feeds, user management, soft-deleted person recovery
- **CI/CD pipeline** — Automated tests on every push; branch protection requires PR + CI pass; manual deploy gate to production
- **Comprehensive tests** — 124 passing tests (75 Core + 49 Web) covering services, layout engine, auth, duplicate detection, and data integrity
- **Technical blog** — Deep-dive articles on RBAC, fuzzy matching, multi-tenant architecture, layout engine, and testing discipline in `docs/blogs/`

---

## Learn More

**Technical Blog** — Deep-dive articles on architectural decisions and lessons learned:
- [Role-Based Access Control Implementation](docs/blogs/01-rbac-implementation.md)
- [Fuzzy Name Matching with Levenshtein Distance](docs/blogs/02-fuzzy-name-matching.md)
- [Multi-Tenant Architecture for Family Trees](docs/blogs/03-multi-tenant-architecture.md)
- [Tree Layout Engine: Computing Positions in C#](docs/blogs/04-tree-layout-engine.md)
- [Testing Discipline: Constraints Over Code Review](docs/blogs/05-testing-discipline.md)

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
- Registration modes: `Open`, `InviteOnly` (production default), `Closed`; a missing or unrecognised setting fails closed to `InviteOnly`
- Token-based invite flow with configurable TTL; admin-generated invite URLs
- Login rate limiting (5 req / 15 min per IP) + Identity account lockout
- Sessions re-checked against the user's security stamp every 5 minutes, on requests and in open Blazor circuits: a password reset or a role change signs other sessions out
- Signed-out deep links return to the requested page after sign-in (local paths only, no open redirect)
- One Admin source (`AppUser.IsAdmin`), set by the site owner from the Users tab
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
- `Relationship.IsEnded` / `EndDate` distinguish active from former spouses; an ended marriage with no known date stays undated
- Filtered performance indexes on `People`, `Relationships`, `AuditLogs`
- EF migrations run automatically on startup via `MigrateAsync()`

**Media & Export**
- Azure Blob Storage for photo uploads (falls back to Azurite for local dev); photos are served by the app at `/media/...`, only to the person's own family, so the container can stay private
- SVG tree export (full canvas), CSV and JSON person export

**CI/CD**
- GitHub Actions CI on every push and PR: build + unit/component tests, plus a separate E2E job (Playwright against a disposable SQL Server container)
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
| **Unit / integration** (Core services) | xUnit + FluentAssertions, EF Core InMemory, real `UserManager` | ✅ CI on every PR | `dotnet test FamilyTree.sln` |
| **Component + layout** (Blazor, layout engines) | bUnit, CsCheck property tests | ✅ CI on every PR | `dotnet test FamilyTree.sln` |
| **E2E** (real browser, real app process) | Playwright + SQL Server | ✅ CI on every PR (own job) | `dotnet test tests/FamilyTree.E2E.Tests` |

**Unit, integration and component tests**: in-memory EF Core with each test on its own database. Highlights:
- `CrossTenantMatrixTests`: every public method of the family-scoped services, called from one family against another's IDs, must fail or return nothing
- `FamilyTreeLayoutFuzzTests` / `FamilyTreeLayoutTransitionTests`: property tests and before/after edit pairs for the layout engines
- `PlausibilityRulesTests`, `EntryGuardrailServiceTests`: date sanity rules shared by the server and the form
- `AdminUserServiceTests`, `SecurityStampRevalidationTests`, `LoginRedirectTests`: session revocation and sign-in redirects
- Tests for bugs found live are tagged `[Trait("Category", "Regression")]`

**E2E tests**: launch the built app against a disposable SQL Server database and drive it with headless Chromium: invite-to-tree signup, the story-invite journey under invite-only registration on a desktop and a phone, CSRF on login/logout, session revocation, sign-in deep links, mobile layout. The security-stamp check runs on every request in every E2E test. Locally they need SQL Server Express (`E2E_ConnectionString` overrides the default) and a trusted dev certificate (`dotnet dev-certs https --trust`).

### Run Tests Locally

```bash
# Everything, including E2E (needs local SQL Server)
dotnet build FamilyTree.sln
dotnet test FamilyTree.sln

# Only the tests for bugs found live
dotnet test FamilyTree.sln --filter "Category=Regression"
```

The login rate limit (5 per 15 minutes per IP) applies to E2E runs too: running the E2E suite several times in a row can fail with `?loginError=toomany`.

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
