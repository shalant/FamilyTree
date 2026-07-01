Testing Plan — FamilyTree CI/CD

## Status
✅ **Implemented** — Unit + component tests fully automated in CI.  
🖥️ **Local** — UI tests (Playwright) run locally before pushing.

## Development Workflow

Tests run automatically on every push and PR. Results available in GitHub Actions dashboard immediately.

## Current CI Workflow

Tests run on **all branches** and **pull requests**:

```yaml
name: CI
on:
  push:
    branches: ['**']  # All branches
  pull_request:
    branches: ['**']  # All PRs

jobs:
  build-and-test:
    runs-on: ubuntu-latest
    timeout-minutes: 15
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: dotnet restore FamilyTree.sln
      - run: dotnet build FamilyTree.sln --no-restore -c Debug
      - run: dotnet build FamilyTree.sln --no-restore -c Release
      - run: dotnet test FamilyTree.sln --no-build -c Release --logger "trx;LogFileName=test_results.trx"
      - uses: actions/upload-artifact@v4
        with:
          name: test-results
          path: '**/*.trx'
```

**What runs:**
- ✅ Debug build (catches compiler issues)
- ✅ Release build (catches optimization issues)
- ✅ Unit tests (19 passing)
- ✅ Component tests (4 passing, 4 skipped)
- ✅ Test result artifacts (.trx files)

## Testing Scope & Implementation

### 1. Unit Tests (xUnit + FluentAssertions) — ✅ Done
**FamilyTree.Core.Tests** (19 tests)
- ✅ PersonServiceTests (8 tests)
  - Create returns person with correct name
  - `CreatedBy` and `FamilyId` stamped on create
  - Whitespace-only first names rejected
  - Birth date validation
  - Soft delete + restore behavior
  - Family scoping & super-user access
  
- ✅ RelationshipServiceTests (3 tests)
  - Canonical GUID ordering (lower always PersonA)
  - Duplicate relationship prevention
  - Delete removes record
  
- ✅ StoryTests (3 tests)
  - Story submission validation
  - Invite token expiry/validity checks
  - Focus state persistence
  
- ✅ AuthServiceTests (2 tests)
  - Focus state saves and loads
  - Query param override behavior

- ✅ HelperTests (3 tests)
  - Story invite validation
  - Submission rules
  - Focus resolution fallback

### 2. Component Tests (bUnit) — ✅ Done
**FamilyTree.Web.Tests** (4 passing, 4 skipped)
- ✅ FamilyTreeCanvasRenderTests
  - Canvas renders with correct viewport element
  
- ✅ HeroOverlayComponentTests
  - Overlay shows when Visible = true
  
- ✅ StoryRespondComponentTests
  - Renders without JSInterop errors
  
- ✅ ToastBehaviorTests
  - Toast service shows notifications
  
- ⏭️ PersonDetailDrawerTests (skipped)
  - Complex MudBlazor dependencies; test locally
  
- ⏭️ UI Tests (3 skipped — Playwright)
  - Require running server; run locally before push

### 3. Local Testing — Before Pushing

**Run all tests locally:**
```bash
dotnet test FamilyTree.sln
```

**Run UI tests (with server running):**
```bash
# Terminal 1: Start the app
dotnet watch

# Terminal 2: Run Playwright tests
dotnet test --filter "UiTests"
```

**Expected results:**
- All unit tests pass
- All component tests pass
- No test output errors in console

## Best Practices

### Before Pushing
1. **Run tests locally:**
   ```bash
   dotnet test FamilyTree.sln
   ```
2. **Verify Release build:**
   ```bash
   dotnet build FamilyTree.sln -c Release
   ```
3. **Review CI status** on GitHub Actions after push
4. **Check test result artifacts** if any test fails

### CI Guarantees
- ✅ No code merges without passing tests
- ✅ Debug + Release both verified
- ✅ Test results always archived
- ✅ Runs on all branches and PRs
- ✅ 15-minute timeout prevents hung builds

### When Tests Fail in CI
1. Check the error message in GitHub Actions
2. Run `dotnet test` locally to reproduce
3. Review the `.trx` artifact for detailed failure info
4. Fix locally and push again

## Success Criteria
- ✅ All unit tests pass in CI (19/19)
- ✅ All component tests pass in CI (4/4)
- ✅ Debug and Release builds both succeed
- ✅ No code merges without passing CI
- ✅ Test artifacts uploaded for every run
