# FamilyTree — project context

> Paste this file at the start of any AI session or hand it to a new collaborator.
> Keep it current as decisions are made. Last updated: 2026-06.

---

## What this is

A private family tree web application. The primary goal is a beautiful, organic
tree visualisation where each family member can browse their branch. Design input
is expected from artist collaborators in the family — the UI is intentionally kept
simple and easy to overhaul.

**Repo:** https://github.com/shalant/FamilyTree  
**Hosting:** Azure App Service (free tier F1) + Azure SQL Database (free tier)  
**Stack:** .NET 10, C# 13, Blazor Server, EF Core, MudBlazor, SQL Server

---

## Architecture

There is **no HTTP boundary** between the UI and the service layer:

```
FamilyTree.Web      Blazor Server UI
      │ direct service injection
      ▼
FamilyTree.Core     Service layer + EF Core (no REST controllers)
      │ EF Core
      ▼
SQL Server / Azure SQL Database
```

Local development uses SQL Server Express (`localhost\sqlexpress`) or a Docker
container. Both projects run with `dotnet watch`.

---

## Solution structure

```
FamilyTree/
├── src/
│   ├── FamilyTree.Shared/
│   │   ├── DTOs/Person/       PersonDto, PersonUpsertDto
│   │   ├── DTOs/              CoupleDto
│   │   └── Enums/             RelationshipType, MediaType, Gender
│   ├── FamilyTree.Core/
│   │   ├── Data/              AppDbContext, DataSeeder, EF Core migrations
│   │   ├── Mappers/           PersonMapper, RelationshipMapper
│   │   ├── Models/            Person, Relationship, Medium, Family
│   │   └── Services/          IPersonService, PersonService, BlobStorageService, …
│   └── FamilyTree.Web/
│       ├── Modules/
│       │   ├── Components/    Reusable UI components (see below)
│       │   ├── Dialogs/       ConfirmDialog, SiblingInferenceDialog
│       │   └── Pages/         Home, PersonAdd, PersonEdit
│       ├── Services/          FamilyTreeLayoutEngine, CoupleHelper, ToastService, …
│       └── wwwroot/           css/, js/ (canvas-interaction.js, ftDrag.js)
├── tests/
│   └── FamilyTree.Core.Tests/
├── docs/
│   ├── FamilyTree.md          ← this file
│   ├── todolist.md
│   ├── deployment.md
│   └── architecture-decisions/  ADRs
└── .github/workflows/         CI/CD → Azure App Service
```

---

## Components

| File | Purpose |
|------|---------|
| `FamilyTreeCanvas.razor` | Calls `FamilyTreeLayoutEngine`, renders SVG T-bar connectors + person nodes. Emits `OnPersonSelected`. No navigation knowledge. |
| `PersonNode.razor` | Single circle node — initials, name, years. Purely presentational. Receives pre-computed X/Y/Size. |
| `PersonDetailDrawer.razor` | Read-only side drawer. Vital dates, relationships, biography, photos. Emits `OnEdit`, `OnDelete`, `OnFocusPerson`. |
| `PersonForm.razor` | All form fields for add and edit. Shared by `PersonAdd` and `PersonEdit`. Handles autocomplete for parents, spouses, former spouses, siblings, children. |
| `HeroOverlayComponent.razor` | Floating card showing focus person stats and tree counts. Draggable. |
| `CustomToolbar.razor` | Floating zoom / center / reset toolbar. Draggable. |
| `LoginOverlay.razor` | Intro splash with "Continue as guest" — placeholder for future auth. |
| `ConfirmDialog.razor` | Generic MudBlazor dialog for destructive actions. Takes `Message` and `ConfirmLabel`. |
| `SiblingInferenceDialog.razor` | Appears after adding a sibling — offers to link that sibling's existing siblings too. |

---

## Pages

| Route | File | Purpose |
|-------|------|---------|
| `/` | `Home.razor` | Orchestrator. Owns all state: people list, couples, focus person, detail person, drag positions. Renders canvas + overlays. |
| `/people/add` | `PersonAdd.razor` | Loads all people for pickers, renders `PersonForm`, calls `CreateAsync`. |
| `/people/{id}/edit` | `PersonEdit.razor` | Loads person + all people, maps to `PersonUpsertDto`, renders `PersonForm`, calls `UpdateAsync`. Runs sibling-inference dialog post-save. |

---

## Data model

### Entities (`FamilyTree.Core/Models/`)
```
Family          Id, Name, CreatedAt
Person          Id, FamilyId (FK→Family, nullable), FirstName, MiddleName, LastName,
                MaidenName, BirthDate, BirthPlace, DeathDate, DeathPlace,
                BiographyNotes, ProfilePhotoUrl, Gender, audit fields, RowVersion
Relationship    Id, PersonAId, PersonBId, Type, StartDate, EndDate, Notes,
                audit fields, RowVersion
                — unique constraint on (PersonAId, PersonBId, Type)
                — PersonAId < PersonBId always enforced (canonical pair)
                — EndDate set = former/divorced; EndDate null = active
Medium          Id, PersonId (FK→Person, cascade delete), Url, FileName, Caption,
                Type, MimeType, audit fields, RowVersion
```

### `PersonDto` (read)
```csharp
Guid        Id
Guid?       FamilyId
string      FirstName, MiddleName?, LastName, MaidenName?
string      FullName            // computed
DateOnly?   BirthDate, DeathDate
string?     BirthPlace, DeathPlace
int?        Age                 // computed
bool        IsDeceased          // computed
string?     BiographyNotes, ProfilePhotoUrl
Gender?     Gender
List<Guid>  ParentIds, ChildIds, SpouseIds, FormerSpouseIds, SiblingIds
```

### `PersonUpsertDto` (write)
```csharp
string      FirstName, MiddleName?, LastName, MaidenName?
DateOnly?   BirthDate, DeathDate
string?     BirthPlace, DeathPlace, BiographyNotes, ProfilePhotoUrl
Gender?     Gender
List<Guid>  ParentIds, SpouseIds, FormerSpouseIds, SiblingIds, ChildIds
```

### `CoupleDto` (render-time only, never persisted)
Derived by `CoupleHelper.Derive(people)` from shared children + explicit spouse/formerSpouse IDs.
Carries `PersonAId`, `PersonBId`, `ChildIds`, `IsFormer`.

---

## Tree layout

All node positions computed in C# by `FamilyTreeLayoutEngine` before render. Key facts:

- **Y axis** is a real birth-year timeline (`PxPerYear = 6.5`). No fixed row heights.
- **X axis** uses bottom-up subtree width measurement (`MeasureGroup`) + top-down placement (`PlaceGroup`). Couples sit `SpouseSpacingX = 200px` apart. Children are spread evenly below the couple midpoint.
- **Cross-root couples** (a child of family A marries a child of family B): detected before placement, each partner placed as a leaf under their own family, children placed at the couple midpoint in a post-pass. Prevents connector tangles.
- **Connectors** are classic pedigree T-bars: straight horizontal couple lines, vertical stems, horizontal T-bars, vertical drops. Former couples use dashed grey 💔; active couples use solid green ❤.
- **Canvas pan/zoom** lives in JS (`canvas-interaction.js`). Blazor never touches `ft-transform` style directly.
- **Widget drag** (toolbar, hero card): Blazor owns the stored position; JS calls `[JSInvokable] OnDragEnd(key, left, top)` on mouseup.

Layout constants in `FamilyTreeLayoutEngine.cs`:
- `NodeSpacingX = 120` — horizontal gap between node centers
- `SpouseSpacingX = 200` — gap between couple nodes
- `FocusSize = 80` / `Gen1Size = 70` / `DefaultSize = 60` — node diameters
- `PaddingX = 90` / `PaddingY = 10` — canvas edge padding

---

## Service patterns

- All service methods return `ServiceResponse<T>` — always check `.Success` before `.Data`
- Static factories: `ServiceResponse.Ok(data)` / `ServiceResponse.Fail(message)`
- `IDbContextFactory<AppDbContext>` — scoped contexts, never singleton DbContext
- `PersonMapper.MapToDto(person, rels, allPeople)` — enriches read model with derived ID lists
- `PersonService.SyncRelationshipsDiffAsync` — diffs existing rels against the new DTO and creates/updates/deletes accordingly; handles spouse ↔ former-spouse transitions via `EndDate`

---

## Multi-tenant preparation

A `Family` table exists as a tenant container. `Person.FamilyId` (nullable) links each person to a family. The seeder creates one "My Family" row and assigns all seeded people to it. Service-layer filtering by `FamilyId` will be added when ASP.NET Core Identity is wired in.

---

## Navigation decisions

| Action | Pattern | Reason |
|--------|---------|--------|
| View person details | Side drawer | Keeps tree visible in context |
| Add person | Full page `/people/add` | Needs focus, relationship pickers |
| Edit person | Full page `/people/{id}/edit` | Shares `PersonForm` with add |
| Delete confirm | `ConfirmDialog` | Destructive — explicit confirmation required |
| Focus tree on person | In-place state `_focusPerson` | No navigation, re-renders canvas |
| Share view | Copy URL with `?focus=<id>` to clipboard | Stateless shareable link |

---

## Local dev commands

```bash
# Run Core (services)
cd src/FamilyTree.Core && dotnet watch

# Run Web UI
cd src/FamilyTree.Web && dotnet watch

# New migration (run from src/FamilyTree.Core)
dotnet ef migrations add <Name>
dotnet ef database update
```

---

## How to use this document

At the start of a new AI session, paste this file and say what you want to work on.
Update the file as decisions are made — especially the data model and layout sections.
