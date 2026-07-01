# Contributing to FamilyTree

## Development Workflow

### 1. Local Development
Start the app in watch mode:
```bash
cd src/FamilyTree.Web
dotnet watch
```
App runs at `https://localhost:44381`

### 2. Run Tests Before Pushing
```bash
# Unit + Component tests (in-memory, ~30s)
dotnet test FamilyTree.sln

# UI tests (requires running app server)
# Terminal 1: dotnet watch
# Terminal 2: dotnet test --filter "UiTests"
```

### 3. Create a Pull Request
After pushing, verify CI passes:
- GitHub Actions runs automatically on all branches and PRs
- Check [Actions tab](https://github.com/shalant/FamilyTree/actions) for results
- All tests must pass before merging


## Testing Strategy

| Type | Framework | Runs | Command |
|------|-----------|-------|---------|
| **Unit** | xUnit | ✅ CI | `dotnet test` |
| **Component** | bUnit | ✅ CI | `dotnet test` |
| **UI** | Playwright | 🖥️ Local | `dotnet test --filter "UiTests"` |

### Unit Tests (CI-automated)
- Service layer: PersonService, RelationshipService, StoryService, etc.
- In-memory EF Core database
- Run: `dotnet test tests/FamilyTree.Core.Tests`

### Component Tests (CI-automated)
- Blazor component rendering: Canvas, Hero Overlay, Toast notifications
- bUnit test harness
- Run: `dotnet test tests/FamilyTree.Web.Tests`

### UI Tests (Local only)
- Real browser automation (Playwright)
- Full app flow testing
- Requires running server
- Run locally before pushing; too slow for every CI run
- Run: Start app + `dotnet test --filter "UiTests"`

## Code Guidelines

### Naming
- `PascalCase` for public classes, methods, properties
- `camelCase` for local variables and parameters
- `_camelCase` for private fields

### Comments
- Add comments only for **non-obvious WHY**, not WHAT
- Avoid comments that duplicate code
- Use self-documenting names instead

### Error Handling
- Validate inputs at system boundaries (user input, external APIs)
- Trust internal code and framework guarantees
- Use `ServiceResponse<T>` for domain operations (check `.Success` before `.Data`)

### Database Changes
Run migrations:
```bash
dotnet ef migrations add MigrationName --startup-project src/FamilyTree.Web
dotnet ef database update --startup-project src/FamilyTree.Web
```

Migrations run automatically on app startup in all environments (dev + prod).

## Architecture

### Three-Tier Stack
```
FamilyTree.Web (Blazor Server + MudBlazor)
    ↓ direct service injection (no REST)
FamilyTree.Core (Services + EF Core)
    ↓
SQL Server / Azure SQL Database
```

### Key Components
- **FamilyTreeLayoutEngine** — C# positions all tree nodes before render
- **FamilyTreeCanvas** — SVG rendering of layout
- **PersonDetailDrawer** — Side panel for person info + actions
- **CustomToolbar** — Floating zoom/pan controls
- **Admin Panel** — User & audit management

See `CLAUDE.md` for architectural decisions and component responsibilities.

## Build & Test

```bash
# Build
dotnet build FamilyTree.sln                    # Debug
dotnet build FamilyTree.sln -c Release         # Production

# Test
dotnet test FamilyTree.sln                     # All tests
dotnet test FamilyTree.sln --filter "Core"     # Core tests only
dotnet test FamilyTree.sln --filter "Web"      # Web tests only
dotnet test FamilyTree.sln --filter "UiTests"  # UI tests only

# Run
cd src/FamilyTree.Web && dotnet watch
```

## Pull Requests

1. Create a feature branch: `git checkout -b feature/description`
2. Make changes and test locally
3. Push your changes: `git push origin feature/description`
4. GitHub Actions CI will run automatically
5. Once all tests pass, create a PR
6. Code review + merge

## Debugging

### Test Failures
```bash
# See detailed error
dotnet test FamilyTree.sln --logger "console;verbosity=normal"

# Run specific test
dotnet test --filter "TestName"

# Download CI artifacts
# GitHub Actions → Latest Run → Artifacts → test-results.trx
```

### Application Issues
```bash
# Enable detailed logging
# Add to appsettings.Development.json:
"Logging": {
  "LogLevel": {
    "Default": "Debug",
    "Microsoft": "Information"
  }
}
```

## Useful Links

- **GitHub Actions**: https://github.com/shalant/FamilyTree/actions
- **Testing Plan**: `docs/testing-plan.md`
- **Architecture**: `CLAUDE.md`
- **Build/Run**: `CLAUDE.md` (Build & Run Commands)
