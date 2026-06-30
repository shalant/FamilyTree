Testing Plan — FamilyTree CI/CD Expansion
Purpose
This document defines the plan for adding automated testing to the FamilyTree project.
The goal is to validate new features (story invites, onboarding toasts, focus resolution, navigation) and ensure stability before merging into production.

All testing work will occur on a dedicated branch:

Code
feature/testing-pipeline
Current CI Workflow
Your existing workflow (ci.yml) builds and runs tests on pushes and pull requests to main and master:

yaml
name: CI

on:
  push:
    branches: [master, main]
  pull_request:
    branches: [master, main]


jobs:
  build-and-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET 10
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore
        run: dotnet restore FamilyTree.sln

      - name: Build
        run: dotnet build FamilyTree.sln --no-restore -c Release

      - name: Test
        run: dotnet test FamilyTree.sln --no-build -c Release --logger "console;verbosity=normal"
This pipeline ensures build integrity and basic test execution.
The next step is to expand test coverage and add richer reporting.

Testing Scope
1. Unit Tests (xUnit + FluentAssertions)
Located in:

Code
FamilyTree.Core.Tests
FamilyTree.Web.Tests
Focus Resolution
DB focus → selected

LocalStorage focus → selected

Query param focus → overrides

No focus anywhere → first person fallback

Story Invite Flow
Valid token → loads form

Expired token → shows expired screen

Used token → shows “Already shared”

Invalid token → shows “Link not found”

Story Submission
Empty body → error

Valid body → success

Duplicate submission → “Already shared”

Navigation Logic
Redirects

Invite → register → home

“See the family tree” button behavior

2. Component Tests (bUnit)
Located in:

Code
FamilyTree.Web.Tests
Components to test
StoryRespond.razor

HeroOverlayComponent.razor

FamilyTreeCanvas.razor

PersonDetailDrawer.razor

Assertions
Correct rendering of states (loading, invalid, submitted, already shared)

Toasts appear only once per condition

Buttons trigger correct navigation events

Persistent info toast remains visible

3. Integration Tests
Using WebApplicationFactory:

StoryInviteService API calls

PersonService CRUD

AuthService focus persistence

Token validation end‑to‑end

4. UI Tests (Playwright)
Simulate real user flows:

Fill out “Share this memory” form

Submit → see conversion screen

Click “See the family tree →”

Verify tree loads and focus ring appears

Verify onboarding toast logic

Pipeline Enhancements
Add richer test reporting
yaml
- name: Test with report
  run: dotnet test FamilyTree.sln --no-build -c Release --logger "trx;LogFileName=test_results.trx"

- name: Upload test results
  uses: actions/upload-artifact@v4
  with:
    name: test-results
    path: '**/*.trx'
Add Playwright UI tests
yaml
- name: Install Playwright
  run: pwsh ./tests/FamilyTree.Web.Tests/bin/Release/net10.0/playwright.ps1 install

- name: Run Playwright tests
  run: dotnet test tests/FamilyTree.Web.Tests --configuration Release
Branch Workflow
Create branch:

bash
git checkout -b feature/testing-pipeline
Add new test files under FamilyTree.Web.Tests and FamilyTree.Core.Tests.

Commit and push changes.

Verify CI runs automatically on push.

Review test results in GitHub Actions.

Merge into main after successful runs and manual QA.

Success Criteria
✅ All tests pass in CI
✅ No duplicate toasts or onboarding regressions
✅ Story invite flow validated end‑to‑end
✅ Focus resolution logic confirmed
✅ Playwright UI tests run successfully
✅ Artifacts uploaded for review





StoryInviteTests.cs

StoryRespondComponentTests.cs (bUnit)

PlaywrightStoryFlowTests.cs


✔ Core logic tests
Focus resolution
Story invite validation
Story submission rules

✔ Component tests
StoryRespond.razor
HeroOverlayComponent
PersonDetailDrawer

✔ UI tests
Playwright flows for story submission and navigation