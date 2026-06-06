# CLAUDE.md
   2
   3 This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.
   4
   5 ## Build & Run Commands
   6
   7 ```bash
   8 # Build
   9 dotnet build FamilyTree.sln
  10
  11 # Run (with hot-reload)
  12 cd src/FamilyTree.Core && dotnet watch    # API → https://localhost:7001
  13 cd src/FamilyTree.Web && dotnet watch    # Web → https://localhost:7000
  14
  15 # Tests
  16 dotnet test FamilyTree.sln
  17 dotnet test tests/FamilyTree.Core.Tests/FamilyTree.Core.Tests.csproj
  18
  19 # Database migrations (run from src/FamilyTree.Core)
  20 dotnet ef migrations add <MigrationName>
  21 dotnet ef database update
  22 ```
  23
  24 ## Architecture Overview
  25
  26 Three-tier .NET 10 / C# 13 application:
  27
  28 ```
  29 FamilyTree.Web  (Blazor Server + MudBlazor)
  30     ↓ HTTP / service injection
  31 FamilyTree.Core  (ASP.NET Core REST API + EF Core)
  32     ↓
  33 SQL Server (local) / Azure SQL Database (prod)
  34 ```
  35
  36 **Projects:**
  37 - `FamilyTree.Shared` — DTOs and enums only; no business logic; shared by Api and Web
  38 - `FamilyTree.Core` — REST API, service layer, EF Core data access, Swagger in dev
  39 - `FamilyTree.Web` — Blazor Server UI; calls the API; never references Api project directly
  40
  41 **CI/CD:** GitHub Actions builds, tests, and deploys both services to Azure App Service on push to main. Migrations run automatically on API startup in production.
  42
  43 ## Key Architectural Decisions
  44
  45 ### JS-Free Tree Layout (ADR 001)
  46 All tree node positions are computed in C# before rendering — no post-render DOM measurement. Nodes use `position:absolute` at pre-calculated (X, Y) coordinates; SVG connectors
      use the same coordinates. Fixed layout constants: `NodeSpacingX=100px`, `GenerationH=110px`, `FocusSize=64px`, `DefaultSize=44px`. Consequence: no dynamic text sizing — fixed
     label widths only.
  47
  48 ### Canonical Relationship Ordering
  49 For bidirectional relationship types (Spouse, Sibling), `PersonAId < PersonBId` is always enforced. A unique DB constraint on `(PersonAId, PersonBId, Type)` prevents duplicates
     . Use `CanonicalPair(x, y)` helper when creating/querying these relationships.
  50
  51 ### Blazor Component Responsibilities
  52 | Component | Role |
  53 |-----------|------|
  54 | `FamilyTreeCanvas` | Layout logic + SVG rendering; emits `OnPersonSelected` |
  55 | `PersonNode` | Presentational circle node only |
  56 | `PersonDetailDrawer` | Read-only details; emits `OnEdit` / `OnDelete` / `OnFocus` |
  57 | `PersonForm` | Shared add/edit form fields |
  58 | `People.razor` | Orchestrator — owns state, navigation, and dialog invocation |
  57 | `PersonForm` | Shared add/edit form fields |
  58 | `People.razor` | Orchestrator — owns state, navigation, and dialog invocation |
  59
  60 ## Domain Model
  61
  62 **Core entities:** `Person`, `Relationship`, `Medium`
  63
  64 - `Person`: name fields, dates/places, `Gender` enum, `BiographyNotes` (5000 chars), `ProfilePhotoUrl` (500 chars), audit fields, SQL `RowVersion` for concurrency
  65 - `Relationship`: directional link with `Type` enum (Parent, Spouse, Sibling, Adopted), optional date range, unique constraint on `(PersonAId, PersonBId, Type)`
  66 - `Medium`: photo/media file linked to a Person (cascade delete)
  67
  68 **DTOs (in `FamilyTree.Shared`):**
  69 - `PersonDto` — read model; includes computed `FullName`, `Age`, `IsDeceased`, and derived ID lists (`ParentIds`, `ChildIds`, `SpouseIds`, `SiblingIds`) populated by `PersonMap
     per`
  70 - `PersonUpsertDto` — write model for create/update
  71 - `CoupleDto` — derived at render time from `PersonDto.ParentIds` by `CoupleHelper`; used for SVG connector rendering
  72
  73 ## Service & Data Access Patterns
  74
  75 - All service methods return `ServiceResponse<T>` — check `.Success` before using `.Data`; use static factories `ServiceResponse.Ok(data)` / `ServiceResponse.Fail(message)`
  76 - `DbContextFactory<AppDbContext>` is used for scoped, thread-safe contexts (not a singleton DbContext)
  77 - `PersonMapper` enriches `PersonDto` with derived relationship ID lists from in-memory relationship data — this derivation is not persisted
  78
  79 ## Configuration
  80
  81 - **API** `appsettings.json`: connection string to `FamilyTreeDb`, `AllowedOrigins` for CORS (Web URL)
  82 - **Web** `appsettings.json`: `ApiSettings.BaseUrl` pointing to API URL
  83 - **Local DB**: `Server=localhost\sqlexpress;Database=FamilyTreeDb` or Docker (`Dev!Password123`)
  84 - **Dev seeding**: `DataSeeder.Seed()` runs on startup in development — creates a sample family tree
  85
  86 ## Theme System
  87
  88 `ThemeService` (singleton) manages dark/light mode and persists the preference to `localStorage` via `IJSRuntime`. MudBlazor theme provider + custom CSS variables (`--ft-green-
     600`, `--ft-surface`, etc.). All pages use `@rendermode InteractiveServer`.