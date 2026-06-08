# FamilyTree — Build Plan & Feature Checklist

Structured roadmap organized by phase. Items marked ✅ are complete; 🔲 are pending.

---

## Phase 1 — Core CRUD (Complete)
- ✅ Person entity: name, birth/death dates & places, gender, maiden name, notes, photo URL
- ✅ Relationship entity: Parent, Spouse, Sibling, Adopted with canonical ordering
- ✅ Medium entity: photos and documents per person (Azure Blob Storage)
- ✅ EF Core + SQL Server, migrations, dev seeding
- ✅ PersonForm: all identity fields, relationships (parents, spouses, children), notes
- ✅ PersonAdd / PersonEdit pages
- ✅ People list page with search, sort, status filter (living/deceased), birthplace column
- ✅ PersonDetailDrawer: vitals, birthplace, deathplace, gender, relationships, media strip
- ✅ PersonMedia page: photo upload/gallery, document records with delete wired to API
- ✅ Family tree canvas: JS-free C# layout engine, SVG connectors, zoom/pan
- ✅ Dark mode (ThemeService + localStorage)
- ✅ MudBlazor + design token system (--ft-* CSS variables)
- ✅ Comprehensive input validation (70+ rules): dates, age gaps, spouse conflicts, DTO annotations

---

## Phase 2 — Auth & Identity

### 2a — Pre-auth iteration ✅ (complete)
- ✅ `AppUser` entity: Id, Email, DisplayName, PersonId (nullable FK→Person), IsSuperUser, FeatureFlags (JSON), CreatedAt, LastLoginAt
- ✅ `UserFamily` join table: UserId, FamilyId, Role (Admin/Member/Viewer), JoinedAt
- ✅ `UserInvite` table: Id, Email, FamilyId, RoleToGrant, Token (unique), ExpiresAt, AcceptedAt, CancelledAt, CreatedBy, CreatedAt
- ✅ `AuditLog` table: Id, UserId?, Action, EntityType, EntityId?, Timestamp (indexed), IpAddress?, OldValue (JSON), NewValue (JSON)
- ✅ `UserActivity` table: Id, UserId?, Date, ActionCount — unique on (UserId, Date)
- ✅ Soft delete on Person, Relationship, Medium — `DeletedAt`/`DeletedBy` + EF Global Query Filters
- ✅ `IAuditLogService` / `AuditLogService` — fire-and-forget; swallows exceptions so audit never breaks main ops
- ✅ `PersonService.DeleteAsync` converted to soft delete
- ✅ `PersonService.RestoreAsync` + `GetDeletedAsync` added
- ✅ EF migration `PreAuthIteration` applied to DB
- ✅ `/admin` page — Dashboard (stat cards + recent activity), Deleted people + Restore, Audit log with filters, Users, Activity
- ✅ `AdminEnabled` config flag — replaced by real `AuthorizeView` roles once Identity is wired

### 2b — Identity schema ✅ (complete, pending migration)
- ✅ `Microsoft.AspNetCore.Identity.EntityFrameworkCore` added to Core
- ✅ `AppUser` extends `IdentityUser<Guid>` — Identity owns Email, UserName, PasswordHash, etc.
- ✅ `AppDbContext` extends `IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>`
- ✅ `DevAuthHandler` — dev-only auto-auth scheme; `DevAuth:Enabled` in appsettings.Development.json
- ✅ `AdminEnabled` flag replaced with `<AuthorizeView Roles="Admin,SuperUser">` + `@attribute [Authorize]`
- ✅ `AddCascadingAuthenticationState()` + `AuthorizeRouteView` wired in Routes.razor
- ✅ `Family.IsPublic` (bool) — opt-in public discoverability
- ✅ `Family.RequireApproval` (bool, default true) — admin must approve new member claims
- ✅ `AppUser.PersonClaimStatus` enum (None/Pending/Approved) — tracks node claim state
- ✅ `Person.IsMinorOverride` (bool?) — null=derive from BirthDate, true=force minor, false=force adult
- ✅ Unique filtered index on `AppUser.PersonId` — one person node per user account
- 🔲 **Run migration: `dotnet ef migrations add AddIdentity` → `dotnet ef database update`**

### 2c — Authentication flow ← **Next**
- 🔲 Google OAuth + email/password (`AddAuthentication().AddGoogle().AddCookie()`)
- 🔲 Open registration page — anyone can create an account
- 🔲 Login / Logout pages
- 🔲 Email verification on registration
- 🔲 Forgot password / reset password (+ SendGrid email)
- 🔲 Seed super-user on startup (Development only)
- 🔲 Replace `DevAuthHandler` scheme with real auth in `Program.cs`
- 🔲 Replace `CustomAppBar` stubs with real claims (name, email, initials, avatar)
- 🔲 Wire `LoginOverlay` component (currently non-functional)
- 🔲 MFA required for Super-user and Admin roles (ASP.NET Identity TOTP)

### 2d — Post-registration flows
- 🔲 Onboarding page (post-registration): "Create a family" or "I have an invite code"
- 🔲 Public family search / "find my node" wizard:
  - Unauthenticated search by name on public families (`Family.IsPublic = true`)
  - Living adults: name only shown publicly; minors: hidden entirely
  - "This is me" → register/login → `PersonClaimStatus = Pending`
  - Admin approves/rejects in `/admin → Users` tab
- 🔲 Invite flow: admin sends invite → email with token → `/join?token=...` → creates UserFamily row
- 🔲 Super-admin user deletion — `UserManager.DeleteAsync` in `/admin → Users` tab

### 2e — Post-auth wiring
- 🔲 `ICurrentUserService` — returns current AppUser from ClaimsPrincipal
- 🔲 Populate `CreatedBy` / `UpdatedBy` / `DeletedBy` / `AuditLog.UserId` from current user
- 🔲 Populate `UserActivity` daily action counts on every write
- 🔲 Scoped data access — filter all service queries by `FamilyId` from claims
- 🔲 Data visibility tiers enforced in service layer:
  - Public: deceased full data; living adults name-only; minors hidden
  - Member: full data for non-minors; minors first name + position only
  - Admin/SuperUser: full data including minors

---

## Phase 3 — Dashboard & Navigation (UI Complete, backends pending)

- ✅ `/admin` — Dashboard tab (stats + recent audit), Deleted + Restore, Audit log, Users, Activity; `AdminEnabled` config gate
- ✅ `/dashboard` — stat cards, quick actions, recent additions, export stubs, donate, request feature
- ✅ `/import` — tabbed UI for GEDCOM, PDF/Document, Excel/CSV, paste text
- ✅ `/about` — project description, tech stack, privacy commitment, roadmap, Venmo donate
- ✅ `/faq` — searchable accordion: getting started / privacy & security / data / technical
- ✅ `RequestFeatureDialog` — title, category, description, priority, optional email
- ✅ `CustomAppBar` — Dashboard, Import, About, FAQ nav links + user menu entries
- 🔲 Dashboard "Generations" stat: wire to real tree depth calculation (currently placeholder)
- 🔲 Dashboard "Recently added": sort by `CreatedAt` once audit fields are populated
- 🔲 Feature request backend: store submissions in DB table or forward via email (SendGrid)
- ✅ Venmo handle: `@doug-Rosenberg-2`
- 🔲 About page roadmap: mark items complete as they ship

---

## Phase 4 — Import

### 4a — GEDCOM
- 🔲 Add `GedcomParserService` to API (GedcomSharp NuGet or hand-rolled parser)
- 🔲 Parse GEDCOM 5.5.1: people, dates, places, relationships, notes, multimedia
- 🔲 `ImportPreviewDto`: person + relationship candidates with source line reference
- 🔲 Preview table UI (wire up existing `/import` preview section):
  - Checkbox per row, inline editable fields, "conflict with existing" warning badge
- 🔲 Commit endpoint: batch-create via existing `PersonService.CreateAsync`
- 🔲 Post-import summary: "X added, Y skipped, Z conflicts"

### 4b — AI Document Import (PDF, Word, paste text)
- 🔲 Add `PdfTextExtractorService` using PdfPig NuGet (text extraction from PDF)
- 🔲 Add `AiImportService`: send extracted text to Claude API (claude-sonnet-4-6)
  - Structured JSON output: people array + relationship array
  - Confidence score per extracted field
  - Source snippet for each extraction
  - Handle long documents via chunking (>32k chars)
- 🔲 Wire preview UI to `AiImportService` results
- 🔲 Word/DOCX support: DocumentFormat.OpenXml for text extraction
- 🔲 Paste-text path: direct send to Claude without extraction step
- 🔲 "AI-powered" badge and explanation in UI (already present in Import.razor)

### 4c — Excel / CSV
- 🔲 `SpreadsheetImportService` using ClosedXML (.xlsx) + built-in CSV parsing
- 🔲 Column mapping UI: user maps their columns to known fields (first-pass auto-detect)
- 🔲 Template download endpoint (generate sample .xlsx with column headers + example row)
- 🔲 Preview + commit (same pattern as GEDCOM)

---

## Phase 5 — Export

- 🔲 GEDCOM export: serialize People + Relationships to GEDCOM 5.5.1 format
- 🔲 Excel export: ClosedXML, one sheet per entity type (People, Relationships)
- 🔲 CSV export: serialize PersonDto list, pipe through file download
- 🔲 PDF report: QuestPDF — printable summary with tree stats and person list
- 🔲 Wire export buttons in Dashboard (currently "Coming soon" disabled)

---

## Phase 6 — Tree Visualization Improvements

- 🔲 Performance: virtual/windowed rendering for trees > 200 nodes
- 🔲 Mobile layout: touch-pan, pinch-zoom on canvas, condensed node display
- 🔲 Ancestor-only view: direct ancestors of focus person only
- 🔲 Descendant-only view: descendants only
- 🔲 Print / screenshot tree (html2canvas or server-side rendering)
- 🔲 Generation band labels visible on canvas
- 🔲 Search highlight: pulse ring on matched node after search

---

## Phase 7 — Person Data Model Expansion

Consider whether fields belong on `Person` (scalar, one per person) or a child table (multiple per person over time):

**On Person table (scalar):**
- 🔲 Occupation (free text, 200 chars)
- 🔲 Religion (free text or enum, 100 chars)
- 🔲 Ethnicity / heritage (free text, 200 chars)

**Child table `PersonAlternateName`:**
- 🔲 Alternate names, nicknames, name changes (one row per alias)

**Child table `PersonResidence`:**
- 🔲 Address / city / country + date range (one row per residence period)

**Relationship fields (already in DB, need UI):**
- 🔲 Expose `StartDate` / `EndDate` on spouse relationships (marriage date range)
- 🔲 Add divorce / separation event type to RelationshipType enum

**Generic event log `PersonEvent`:**
- 🔲 Event type enum (Birth, Death, Marriage, Divorce, Immigration, Graduation, Military, Other)
- 🔲 Event date, place, description, source citation
- 🔲 Replaces ad-hoc fields like military service, immigration, naturalization

---

## Phase 8 — Sharing & Multi-User

- ✅ Audit log table schema (`AuditLog`) — wired to Person CRUD; awaiting UserId from auth
- ✅ User activity table schema (`UserActivity`) — awaiting UserId from auth
- ✅ Invite table schema (`UserInvite`) — awaiting email send and accept flow
- ✅ `UserFamily` role model schema — Admin/Member/Viewer scoped per family
- ✅ `Family.IsPublic` — opt-in public discoverability
- ✅ `Family.RequireApproval` — admin approval gate for new member claims
- 🔲 Invite flow: send email (SendGrid), accept token endpoint, create `UserFamily` row
- 🔲 Permission enforcement: gate write operations behind Member+ role check
- 🔲 Public family search and "find my node" wizard (see Phase 2d)
- 🔲 Activity feed: recent edits visible to collaborators
- 🔲 Optimistic concurrency conflict resolution (RowVersion already on entities)

---

## Phase 9 — Settings & Quality of Life

- 🔲 Settings: wire all tree display toggles (show photos, compact mode, show years)
- 🔲 Profile page: update name, email, avatar
- 🔲 Password reset flow
- 🔲 Email notifications: invites, feature request receipts
- 🔲 Keyboard shortcuts (j/k navigation, e = edit, / = search, etc.)
- 🔲 Undo last destructive action (soft-delete + restore within session)
- 🔲 Bulk operations on People list (bulk tag, bulk delete)

---

## Phase 10 — Infrastructure & Production

- 🔲 Azure App Service deployment (GitHub Actions CI/CD already scaffolded)
- 🔲 Custom domain + SSL certificate
- 🔲 Azure Key Vault for production secrets / connection strings
- 🔲 Azure SQL automatic backups, 7-day retention policy
- 🔲 Rate limiting on sensitive API endpoints (auth, import)
- 🔲 Application Insights or Sentry for error monitoring
- 🔲 GDPR: cookie notice, data export, right-to-erasure workflow

---

## Ongoing

- 🔲 Unit tests: validation rules in PersonService, RelationshipService
- 🔲 Integration tests: PersonService CRUD against real DB
- 🔲 Blazor component tests (bUnit): PersonForm, FamilyTreeCanvas
- 🔲 Accessibility audit: ARIA labels, keyboard navigation, screen reader support
- 🔲 Performance baseline: < 300ms TTFB on dashboard, < 1s tree render for 100-person tree
- 🔲 SEO: meta tags, Open Graph image for shared tree links

---

*Last updated: 2026-06-07*
