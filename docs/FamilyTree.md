# FamilyTree — project context

> Paste this file at the start of any AI session or hand it to a new collaborator.
> Keep it current as decisions are made. Last updated: 2026-05.

---

## What this is

A private family tree web application. The primary goal is a beautiful, organic
tree visualisation where each family member's account "focuses" on their branch.
Design input is expected from artist collaborators in the family — the UI is
intentionally kept simple and easy to overhaul.

**Repo:** https://github.com/shalant/FamilyTree  
**Hosting:** Azure App Service (free tier F1) + Azure SQL Database (free tier)  
**Stack:** .NET 10, Blazor Server, ASP.NET Core Web API, EF Core, MudBlazor, SQL Server

---

## Architecture

```
FamilyTree.Web      Blazor Server UI
      │ HTTP
      ▼
FamilyTree.Api      ASP.NET Core REST API
      │ EF Core
      ▼
Azure SQL Database
```

Local development uses a SQL Server Docker container (`localhost,1433`).
Both projects run with `dotnet watch`. No JavaScript interop — layout is
computed in C#.

---

## Solution structure

```
FamilyTree/
├── src/
│   ├── FamilyTree.Shared/
│   │   ├── DTOs/Person/            PersonDto, PersonUpsertDto
│   │   └── Enums/                  RelationshipType, MediaType
│   ├── FamilyTree.Api/
│   │   ├── Controllers/            PeopleController, RelationshipsController
│   │   ├── Data/                   AppDbContext, EF Core migrations
│   │   ├── DTOs/                   API-only models
│   │   └── Services/               Business logic
│   └── FamilyTree.Web/
│       ├── Modules/
│       │   ├── Components/         Reusable UI components (see below)
│       │   └── Pages/              Routable pages (see below)
│       ├── Services/               IPersonService — typed HTTP client
│       └── wwwroot/                Static assets
├── tests/
│   ├── FamilyTree.Api.Tests/
│   └── FamilyTree.Web.Tests/
├── database/                       Seed scripts, useful queries
├── docs/
│   ├── deployment.md
│   ├── ui-components.md
│   └── architecture-decisions/     ADRs
└── .github/workflows/              CI/CD → Azure App Service
```

---

## Components

| File | Purpose |
|------|---------|
| `FamilyTreeCanvas.razor` | SVG tree. Computes layout in C#, no JS. Emits `OnPersonSelected`. Knows nothing about navigation or drawers. |
| `PersonNode.razor` | Single circle node — initials, name, year. Purely presentational. Receives pre-computed `X`/`Y`/`Size`. |
| `PersonDetailDrawer.razor` | Read-only side drawer. Shows vital dates, relationships, notes. Emits `OnEdit`, `OnDelete`, `OnFocusPerson`. |
| `PersonForm.razor` | All form fields for add and edit. Shared by both pages. Single responsibility: field rendering + validation. |
| `ConfirmDialog.razor` | Reusable MudBlazor dialog for any destructive action. Takes `Message` and `ConfirmLabel` parameters. |

---

## Pages

| Route | File | Purpose |
|-------|------|---------|
| `/people` | `People.razor` | Orchestrator. Owns view toggle (tree/list), search, dialog invocation, and navigation decisions. Delegates all display. |
| `/people/add` | `PersonAdd.razor` | Loads all people for relationship pickers, renders `PersonForm`, calls `CreateAsync`. |
| `/people/{id}/edit` | `PersonEdit.razor` | Loads person by id + all people, maps to `PersonUpsertDto`, renders `PersonForm`, calls `UpdateAsync`. |

---

## Data model

### `PersonDto` (read)
```csharp
int       Id
string    FirstName
string    LastName
string    FullName          // computed
DateTime? BirthDate
DateTime? DeathDate
int?      Age
string?   Notes
List<int>? ParentIds        // populated by API — drives tree layout
List<int>? ChildIds         // populated by API
List<int>? SpouseIds        // populated by API
int       GenerationDepth   // set at render time, never persisted
```

### `PersonUpsertDto` (write)
```csharp
string    FirstName
string    LastName
DateTime? BirthDate
DateTime? DeathDate
string?   Notes
List<int> ParentIds
List<int> SpouseIds
```

### `IPersonService`
```csharp
Task<List<PersonDto>>  GetAllAsync()
Task<PersonDto?>       GetByIdAsync(int id)
Task<PersonDto>        CreateAsync(PersonUpsertDto dto)
Task<PersonDto>        UpdateAsync(int id, PersonUpsertDto dto)
Task                   DeleteAsync(int id)
```

---

## Tree layout

The tree is rendered without JavaScript. Key facts for anyone modifying it:

- `ComputeDepths()` runs BFS from the focus node up through `ParentIds`, then
  a second pass downward to assign negative depths to children.
- Nodes are grouped by depth, sorted oldest-first (highest depth at top).
- Each generation is centered horizontally. Node `X`/`Y` are plain integers.
- SVG bezier curves are built from the same coordinates — no DOM measurement.
- Layout constants in `FamilyTreeCanvas.razor` (all easy to tune):
  - `NodeSpacingX` = 100px — horizontal gap between node centers
  - `GenerationH` = 110px — vertical gap between rows
  - `PaddingX` / `PaddingY` = 60px — canvas edge padding
  - `FocusSize` = 64px — focused node diameter
  - `DefaultSize` = 44px — all other nodes

---

## Navigation decisions

| Action | Pattern | Reason |
|--------|---------|--------|
| View person details | Drawer (end anchor) | Keeps tree visible in context |
| Add person | Full page `/people/add` | Needs focus, no context needed |
| Edit person | Full page `/people/{id}/edit` | Shares `PersonForm` with add |
| Delete confirm | `ConfirmDialog` | Destructive — requires explicit confirmation |
| Focus tree on person | In-place state (`_focusId`) | No navigation, just re-renders canvas |

---

## Key decisions made

**No JavaScript in the tree component (ADR 001)**  
Layout is computed in C# before render. Nodes use `position:absolute` with
pre-calculated coordinates. Eliminates `IJSRuntime` dependency and
`OnAfterRenderAsync` timing issues. Tradeoff: text overflow must be handled
manually with fixed label widths.

**`PersonForm` shared between add and edit**  
Both pages pass an `Initial` model and a submit callback. The form owns only
field rendering and validation — it has no knowledge of create vs. update.
Adding a new field means touching one file.

**`FamilyTreeCanvas` knows nothing about navigation**  
It emits `OnPersonSelected(PersonDto)` and stops. The parent page decides
whether to open a drawer, navigate, or do something else. Makes the canvas
reusable (e.g., embeddable in a dashboard widget).

**Drawer for detail, pages for edit/add**  
Detail view and edit form have different jobs and will diverge over time.
Collapsing them would couple read and write concerns prematurely.

**`ConfirmDialog` is generic**  
Takes `Message` and `ConfirmLabel` as parameters. One component covers all
destructive actions across the app — not just person deletion.

---

## What's working

- [x] Blazor Server + API + SQL Server running locally
- [x] `PeopleController` — GET all, GET by id, POST, PUT, DELETE
- [x] People list view with search, sort, pagination
- [x] Tree view — generation layout, SVG bezier connectors, focus node
- [x] View toggle (tree ↔ list)
- [x] `PersonDetailDrawer` — vital dates, relationships, notes
- [x] `PersonForm` shared by add and edit
- [x] `PersonAdd` page
- [x] `PersonEdit` page
- [x] `ConfirmDialog` for delete
- [x] CI/CD → Azure via GitHub Actions

## What's next / not yet done

- [ ] `ParentIds` / `ChildIds` / `SpouseIds` populated by API (tree connectors depend on this)
- [ ] Authentication — who is the "focus" user
- [ ] Photo / avatar upload
- [ ] Timeline view
- [ ] Search across tree (not just list)
- [ ] Mobile layout for tree canvas
- [ ] Relationship management UI (add/remove parents, spouses)

---

## Local dev commands

```bash
# Start SQL Server
docker start familytree-db

# Run API  (https://localhost:7001, Swagger at /swagger)
cd src/FamilyTree.Api && dotnet watch

# Run Web  (https://localhost:7000)
cd src/FamilyTree.Web && dotnet watch

# New migration
cd src/FamilyTree.Api
dotnet ef migrations add <Name>
dotnet ef database update
```

---

## How to use this document

At the start of a new AI session, paste this file and say what you want to work on.
The assistant will have full context without needing to re-derive the architecture
from scratch. Update the "What's working" checklist and "Key decisions made" section
as the project evolves.