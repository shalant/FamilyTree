markdown# FamilyTree

A private family tree application built with Blazor Server (.NET 10), ASP.NET Core Web API,
SQL Server, and MudBlazor. Hosted on Azure App Service (free tier) with Azure SQL Database.

## Architecture
FamilyTree.Web  (Blazor Server — Azure App Service F1, free)
│  HTTP client calls
▼
FamilyTree.Core  (ASP.NET Core Web API — Azure App Service F1, free)
│  EF Core
▼
Azure SQL Database  (free tier — 100k vCore-seconds/mo, 32 GB)

**Local development:** SQL Server runs in a single Docker container.
Connect with SSMS or Azure Data Studio via `localhost,1433`.
Both .NET projects run natively with `dotnet watch` for hot-reload and debugger support.

## Projects

| Project             | Purpose                                                        |
|---------------------|----------------------------------------------------------------|
| `FamilyTree.Shared` | DTOs and enums shared between Web and Api                      |
| `FamilyTree.Core`    | REST API — EF Core, business logic, database access            |
| `FamilyTree.Web`    | Blazor Server UI — MudBlazor components, HTTP client services  |

## Quick start

### 1. Start local SQL Server

```bash
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=Dev!Password123" \
  -p 1433:1433 --name familytree-db --restart unless-stopped -d \
  mcr.microsoft.com/mssql/server:2022-latest
```

Connect in SSMS: `localhost,1433` / `sa` / `Dev!Password123`

### 2. Apply migrations

```bash
cd src/FamilyTree.Core
dotnet ef database update
```

### 3. Run the API

```bash
cd src/FamilyTree.Core
dotnet watch
# Swagger at https://localhost:7001/swagger
```

### 4. Run the web app

```bash
cd src/FamilyTree.Web
dotnet watch
# App at https://localhost:7000
```

## EF Core migrations

```bash
cd src/FamilyTree.Core

# After changing a model
dotnet ef migrations add <MigrationName>

# Apply locally
dotnet ef database update

# Migrations run automatically on Azure on deployment startup
```

## Deployment

See [docs/deployment.md](docs/deployment.md) for full Azure setup.

Push to `master` → GitHub Actions builds, tests, and deploys both App Services automatically.

## Project structure
FamilyTree/
├── src/
│   ├── FamilyTree.Shared/
│   │   ├── DTOs/
│   │   │   └── Person/             # PersonDto, PersonUpsertDto
│   │   └── Enums/                  # RelationshipType, MediaType
│   ├── FamilyTree.Core/
│   │   ├── Controllers/            # PeopleController, RelationshipsController
│   │   ├── Data/                   # AppDbContext, EF Core migrations
│   │   ├── DTOs/                   # API-only request/response models
│   │   └── Services/               # Business logic
│   └── FamilyTree.Web/
│       ├── Modules/
│       │   ├── Components/         # Reusable UI components
│       │   │   ├── FamilyTreeCanvas.razor    # SVG tree, JS-free layout
│       │   │   ├── PersonNode.razor          # Single node on the tree
│       │   │   ├── PersonDetailDrawer.razor  # Read-only side drawer
│       │   │   ├── PersonForm.razor          # Shared add/edit form
│       │   │   └── ConfirmDialog.razor       # Reusable destructive-action dialog
│       │   └── Pages/
│       │       ├── People.razor              # Tree + list view, orchestration
│       │       ├── PersonAdd.razor           # /people/add
│       │       └── PersonEdit.razor          # /people/{id}/edit
│       ├── Services/
│       │   └── IPersonService.cs             # Typed HTTP client interface
│       └── wwwroot/
├── tests/
│   ├── FamilyTree.Core.Tests/
│   └── FamilyTree.Web.Tests/
├── database/                       # Seed scripts, useful queries
├── docs/
│   ├── deployment.md
│   ├── architecture-decisions/     # ADRs (see below)
│   └── ui-components.md            # Component map and conventions
└── .github/workflows/              # CI/CD

## UI conventions

The web app uses a two-view pattern on the People page:

- **Tree view** — SVG-based family tree with a "focus" node (the logged-in user or
  any selected person). Layout is computed entirely in C# — no JavaScript required.
- **List view** — sortable, searchable MudBlazor table, same data.

**Component responsibilities:**

| Component               | Responsibility                                      |
|-------------------------|-----------------------------------------------------|
| `FamilyTreeCanvas`      | Layout computation + SVG connectors. Emits `OnPersonSelected`. Knows nothing about navigation. |
| `PersonNode`            | Single circle node. Purely presentational.          |
| `PersonDetailDrawer`    | Read-only detail. Emits `OnEdit`, `OnDelete`, `OnFocusPerson`. |
| `PersonForm`            | All form fields. Shared by add and edit pages.      |
| `ConfirmDialog`         | Reusable destructive-action confirmation.           |
| `People.razor`          | Orchestrator. Owns navigation decisions and dialog invocation. |

**Navigation pattern:**

| Action          | Pattern      | Reason                              |
|-----------------|--------------|-------------------------------------|
| View details    | Drawer       | Keeps tree visible behind it        |
| Add person      | Full page    | Needs full focus                    |
| Edit person     | Full page    | Shares `PersonForm` with add        |
| Delete confirm  | Dialog       | Destructive, needs explicit confirm |
| Focus tree      | State change | No navigation, just updates FocusId |