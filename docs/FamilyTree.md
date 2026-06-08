# FamilyTree — project context

> Paste this file at the start of any AI session or hand it to a new collaborator.
> Keep it current as decisions are made. Last updated: 2026-06-07.

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
│   │   ├── Models/            Person, Relationship, Medium, Family,
│   │   │                      AppUser, UserFamily, UserInvite, AuditLog, UserActivity
│   │   └── Services/          IPersonService, PersonService, IAuditLogService,
│   │                          AuditLogService, BlobStorageService, …
│   └── FamilyTree.Web/
│       ├── Modules/
│       │   ├── Components/    Reusable UI components (see below)
│       │   ├── Dialogs/       ConfirmDialog, ExportDialog, SiblingInferenceDialog
│       │   └── Pages/         Home, PersonAdd, PersonEdit, Admin, Dashboard, …
│       ├── Services/          FamilyTreeLayoutEngine, CoupleHelper, ToastService, …
│       └── wwwroot/           css/, js/ (canvas-interaction.js, ftDrag.js, ftUtils.js)
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
| `ExportDialog.razor` | Export scope (all / immediate / ancestors / descendants) + format (JSON / CSV). Downloads via `ftDownloadFile` JS. |

---

## Pages

| Route | File | Purpose |
|-------|------|---------|
| `/` | `Home.razor` | Orchestrator. Owns all state: people list, couples, focus person, detail person, drag positions. Renders canvas + overlays. |
| `/people/add` | `PersonAdd.razor` | Loads all people for pickers, renders `PersonForm`, calls `CreateAsync`. |
| `/people/{id}/edit` | `PersonEdit.razor` | Loads person + all people, maps to `PersonUpsertDto`, renders `PersonForm`, calls `UpdateAsync`. Runs sibling-inference dialog post-save. |
| `/dashboard` | `Dashboard.razor` | Family stats (total people, living, generations, photos), quick actions, recent additions. |
| `/admin` | `Admin.razor` | Admin dashboard — stats, soft-deleted people + restore, audit log with filters, user list, daily activity. Gated by `AdminEnabled` config flag (see below). |

---

## Data model

### Entities (`FamilyTree.Core/Models/`)

**Core tree entities** — all three carry soft-delete fields (`DeletedAt DateTime?`, `DeletedBy Guid?`) and are covered by EF Core Global Query Filters that exclude deleted rows from all normal queries. Use `.IgnoreQueryFilters()` to access deleted records (e.g. admin restore).

```
Family          Id, Name, CreatedAt

Person          Id, FamilyId (FK→Family, nullable),
                FirstName, MiddleName, LastName, MaidenName,
                BirthDate, BirthPlace, DeathDate, DeathPlace,
                BiographyNotes, ProfilePhotoUrl, Gender,
                CreatedAt, CreatedBy, UpdatedAt, UpdatedBy,
                DeletedAt, DeletedBy,    ← soft delete
                RowVersion

Relationship    Id, PersonAId, PersonBId, Type, StartDate, EndDate, Notes,
                CreatedAt, CreatedBy, UpdatedAt, UpdatedBy,
                DeletedAt, DeletedBy,    ← soft delete
                RowVersion
                — unique constraint on (PersonAId, PersonBId, Type)
                — PersonAId < PersonBId always enforced (canonical pair)
                — EndDate set = former/divorced; EndDate null = active
                — NOT cascade-soft-deleted when a Person is deleted;
                  hidden automatically because the Person query filter excludes
                  deleted people, and GetAllAsync filters rels in-memory

Medium          Id, PersonId (FK→Person, cascade delete), Url, FileName, Caption,
                Type, MimeType,
                CreatedAt, CreatedBy, UpdatedAt, UpdatedBy,
                DeletedAt, DeletedBy,    ← soft delete
                RowVersion
```

**Pre-auth user/invite entities** — schema is live; data fills in when ASP.NET Core Identity is wired.

```
AppUser         Id, Email (unique), DisplayName, PersonId (FK→Person, nullable),
                IsSuperUser, FeatureFlags (nvarchar(max) JSON),
                CreatedAt, LastLoginAt
                — FeatureFlags JSON shape: { canEdit, isLocked, dailyCrudCap, isDonor }

UserFamily      UserId (FK→AppUser), FamilyId (FK→Family)   ← composite PK
                Role (Admin / Member / Viewer), JoinedAt

UserInvite      Id, Email, FamilyId (FK→Family), RoleToGrant,
                Token (unique), ExpiresAt, AcceptedAt, CancelledAt,
                CreatedBy (FK→AppUser, nullable), CreatedAt

AuditLog        Id, UserId (FK→AppUser, nullable),
                Action (Create/Update/Delete/Restore/Login/RoleChange),
                EntityType (Person/Relationship/Medium/User),
                EntityId?, Timestamp (indexed), IpAddress?,
                OldValue (JSON?), NewValue (JSON?)

UserActivity    Id, UserId (FK→AppUser, nullable),
                Date, ActionCount
                — unique constraint on (UserId, Date)
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
DateTime?   DeletedAt           // null for live records; set by admin restore UI
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
- `PersonMapper.MapToDto(person, rels)` — enriches read model with derived ID lists
- `PersonService.SyncRelationshipsDiffAsync` — diffs existing rels against the new DTO and creates/updates/deletes accordingly; handles spouse ↔ former-spouse transitions via `EndDate`
- `PersonService.DeleteAsync` — **soft delete**: sets `DeletedAt = UtcNow`, does not remove the row or its relationships
- `PersonService.RestoreAsync` — clears `DeletedAt` / `DeletedBy` using `IgnoreQueryFilters()` to find the deleted row
- `PersonService.GetDeletedAsync` — returns all soft-deleted people for the admin UI (also uses `IgnoreQueryFilters()`)
- `IAuditLogService.LogAsync` — fire-and-forget audit entry writer; exceptions are swallowed so audit failures never break the main operation; called from PersonService on Create / Update / Delete / Restore

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

## Admin UI & access gating

The `/admin` route provides:
- **Dashboard tab** — stat cards (people, deleted count, audit entries today, users, families), recent audit activity feed, quick-nav to other tabs
- **Deleted tab** — soft-deleted people with Restore button; restored rows re-appear in the tree immediately
- **Users tab** — `AppUser` table (empty until auth is wired)
- **Audit log tab** — filterable by action type and entity type; capped at 500 rows; timestamps shown in local time
- **Activity tab** — `UserActivity` daily action counts (empty until auth is wired)

### `AdminEnabled` config flag

Controls both the nav link and the page itself:

```
appsettings.json                → "AdminEnabled": false   (production default — page redirects to /)
appsettings.Development.json    → "AdminEnabled": true    (local dev — fully accessible)
```

`NavMenu.razor` reads this flag via `IConfiguration` and omits the Admin link when false.
`Admin.razor` redirects to `/` immediately if the flag is false, even if someone knows the URL.

**When auth lands:** replace the flag check with `<AuthorizeView Roles="Admin,SuperUser">` in NavMenu and `@attribute [Authorize(Roles = "Admin,SuperUser")]` on the page — no other structural changes needed.

### Role-aware navigation (post-auth pattern)

```razor
@* NavMenu.razor — after auth is wired *@
<AuthorizeView Roles="Admin,SuperUser">
    <MudNavLink Href="/admin" Icon="@Icons.Material.Outlined.AdminPanelSettings">
        Admin
    </MudNavLink>
</AuthorizeView>
```

Role hierarchy:
```
Super-user   cross-family; all permissions; seeds admins; global flag on AppUser.IsSuperUser
Admin        per-family (UserFamily.Role = "Admin"); restore deleted, view audit log, manage users
Member       full CRUD on people and relationships
Viewer       read-only (deferred — may just be unauthenticated access)
```

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
