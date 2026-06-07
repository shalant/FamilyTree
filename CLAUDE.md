# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run Commands

```bash
# Build
dotnet build FamilyTree.sln

# Run (with hot-reload)
cd src/FamilyTree.Core && dotnet watch    # Core services → https://localhost:7001
cd src/FamilyTree.Web && dotnet watch     # Web UI       → https://localhost:7000

# Tests
dotnet test FamilyTree.sln
dotnet test tests/FamilyTree.Core.Tests/FamilyTree.Core.Tests.csproj

# Database migrations (run from src/FamilyTree.Core)
dotnet ef migrations add <MigrationName>
dotnet ef database update
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

**CI/CD:** GitHub Actions builds, tests, and deploys to Azure App Service on push to main. Migrations run automatically on Core startup in production.

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

## Configuration

- **Local DB**: `Server=localhost\sqlexpress;Database=FamilyTreeDb` or Docker (`Dev!Password123`)
- **Dev seeding**: `DataSeeder.Seed()` runs on startup in development — creates a "My Family" row and a sample five-generation tree assigned to it
- **Blob storage**: `BlobStorageService` / `IBlobStorageService` abstracted; configured via `appsettings.json`

## Theme System

`ThemeService` (singleton) manages dark/light mode and persists the preference to `localStorage` via `IJSRuntime`. MudBlazor theme provider + custom CSS variables (`--ft-green-600`, `--ft-surface`, etc.). All pages use `@rendermode InteractiveServer`.
