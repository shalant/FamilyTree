# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run Commands

```bash
# Build
dotnet build FamilyTree.sln

# Run (with hot-reload)
cd src/FamilyTree.Api && dotnet watch    # API → https://localhost:7001
cd src/FamilyTree.Web && dotnet watch    # Web → https://localhost:7000

# Tests
dotnet test FamilyTree.sln
dotnet test tests/FamilyTree.Api.Tests/FamilyTree.Api.Tests.csproj

# Database migrations (run from src/FamilyTree.Api)
dotnet ef migrations add <MigrationName>
dotnet ef database update
```

## Architecture Overview

Three-tier .NET 10 / C# 13 application:

```
FamilyTree.Web  (Blazor Server + MudBlazor)
    ↓ direct service injection
FamilyTree.Api  (Service layer + EF Core; no REST controllers)
    ↓
SQL Server (local) / Azure SQL Database (prod)
```

**Projects:**
- `FamilyTree.Shared` — DTOs and enums only; no business logic; shared by Api and Web
- `FamilyTree.Api` — Service layer, EF Core data access, blob storage abstraction
- `FamilyTree.Web` — Blazor Server UI; injects services from Api directly; no HTTP boundary

**CI/CD:** GitHub Actions builds, tests, and deploys both services to Azure App Service on push to main. Migrations run automatically on API startup in production.

## Key Architectural Decisions

### JS-Free Tree Layout (ADR 001)
All tree node positions are computed in C# before rendering — no post-render DOM measurement. Nodes use `position:absolute` at pre-calculated (X, Y) coordinates; SVG connectors use the same coordinates. Fixed layout constants: `NodeSpacingX=100px`, `GenerationH=110px`, `FocusSize=64px`, `DefaultSize=44px`. Consequence: no dynamic text sizing — fixed label widths only.

### Canonical Relationship Ordering
For bidirectional relationship types (Spouse, Sibling), `PersonAId < PersonBId` is always enforced. A unique DB constraint on `(PersonAId, PersonBId, Type)` prevents duplicates. Use `CanonicalPair(x, y)` helper when creating/querying these relationships.

### Blazor Component Responsibilities
| Component | Role |
|-----------|------|
| `FamilyTreeCanvas` | Layout logic + SVG rendering; emits `OnPersonSelected` |
| `PersonNode` | Presentational circle node only |
| `PersonDetailDrawer` | Read-only details; emits `OnEdit` / `OnDelete` / `OnFocus` |
| `PersonForm` | Shared add/edit form fields |
| `People.razor` | Orchestrator — owns state, navigation, and dialog invocation |

## Domain Model

**Core entities:** `Person`, `Relationship`, `Medium`

- `Person`: name fields, dates/places, `Gender` enum, `BiographyNotes` (5000 chars), `ProfilePhotoUrl` (500 chars), audit fields, SQL `RowVersion` for concurrency
- `Relationship`: directional link with `Type` enum (Parent, Spouse, Sibling, Adopted), optional date range, unique constraint on `(PersonAId, PersonBId, Type)`
- `Medium`: photo/media file linked to a Person (cascade delete)

**DTOs (in `FamilyTree.Shared`):**
- `PersonDto` — read model; includes computed `FullName`, `Age`, `IsDeceased`, and derived ID lists (`ParentIds`, `ChildIds`, `SpouseIds`, `SiblingIds`) populated by `PersonMapper`
- `PersonUpsertDto` — write model for create/update
- `CoupleDto` — derived at render time from `PersonDto.ParentIds` by `CoupleHelper`; used for SVG connector rendering

## Service & Data Access Patterns

- All service methods return `ServiceResponse<T>` — check `.Success` before using `.Data`; use static factories `ServiceResponse.Ok(data)` / `ServiceResponse.Fail(message)`
- `DbContextFactory<AppDbContext>` is used for scoped, thread-safe contexts (not a singleton DbContext)
- `PersonMapper` enriches `PersonDto` with derived relationship ID lists from in-memory relationship data — this derivation is not persisted

## Configuration

- **API** `appsettings.json`: connection string to `FamilyTreeDb`, `AllowedOrigins` for CORS (Web URL)
- **Web** `appsettings.json`: `ApiSettings.BaseUrl` pointing to API URL
- **Local DB**: `Server=localhost\sqlexpress;Database=FamilyTreeDb` or Docker (`Dev!Password123`)
- **Dev seeding**: `DataSeeder.Seed()` runs on startup in development — creates a sample family tree

## Theme System

`ThemeService` (singleton) manages dark/light mode and persists the preference to `localStorage` via `IJSRuntime`. MudBlazor theme provider + custom CSS variables (`--ft-green-600`, `--ft-surface`, etc.). All pages use `@rendermode InteractiveServer`.
