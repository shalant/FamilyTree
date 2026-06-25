# Family App — Todo List

---

## In Progress / Up Next

- [x] **Deploy** — pushed to production; user testing in progress at arborkin-erbufqfkhzcka4cb.centralus-01.azurewebsites.net
- [x] **[SECURITY — Critical] Family-scope check in `GetByIdAsync`** — `PersonService`, `RelationshipService`, and `MediumService` all lack a `FamilyId` guard on single-entity lookups; any authenticated user who knows a GUID can read across family boundaries
- [x] **[SECURITY — Critical] `DevAuth` production guard** — add a startup assertion that `DevAuth:Enabled` is false outside of Development environment
- [x] **[SECURITY — High] Lock `AllowedHosts`** — set to `arborkin-erbufqfkhzcka4cb.centralus-01.azurewebsites.net` in `appsettings.json`; dev overrides to `*` in `appsettings.Development.json`.
- [ ] **[SECURITY — DO WHEN DOMAIN MOVES] Update `AllowedHosts` for `arborkin.com`** — when the custom domain is live, update `"AllowedHosts"` in `appsettings.json` to `arborkin.com;arborkin-erbufqfkhzcka4cb.centralus-01.azurewebsites.net` (keep both until the Azure URL is fully retired)
- [x] **[SECURITY — High] Add security response headers** — X-Frame-Options, X-Content-Type-Options, Referrer-Policy, Content-Security-Policy added via middleware in `Program.cs`
- [x] **[SECURITY — High] Strengthen password policy** — `RequireDigit = true`, `RequireUppercase = true` enabled in `Program.cs`; `RequireNonAlphanumeric` left false to avoid UX friction; minimum length remains 10
- [x] **[SECURITY — Medium] Move reset/invite tokens out of URL** — reset-password and invite links now carry only an opaque DB-row `Guid` (`?id=`/`?invite=`) instead of the real Identity/invite token; the secret token never leaves the server, and `ResetPassword.razor`/`Register.razor` strip even the opaque id from browser history via `history.replaceState` once validated
- [x] **[SECURITY — Medium] Upgrade `Azure.Storage.Blobs`** — upgraded from `12.29.0-beta.1` to `12.29.0` stable
- [x] **[SECURITY — Medium] Surface `AuditLogService` failures** — already logs at `LogError` level in catch block
- [ ] **[SECURITY — Medium] MFA/2FA** — `security.md` documents this as required for Admin/SuperUser but it is not yet implemented
- [x] **[SECURITY — DO BEFORE 2ND FAMILY] Family scoping gap** — fixed `AuthService.RegisterAsync` and Google OAuth callback to insert `UserFamily` row on registration; backfilled prod DB (`FamilyId` on all People, `UserFamily` rows for all 3 users, seed data deleted)
- [x] **[SECURITY — Low] NuGet lock file** — `Directory.Build.props` added with `RestorePackagesWithLockFile = true`; `packages.lock.json` generated for all 5 projects
- [ ] **GEDCOM export** — wire the "Coming soon" stub in Dashboard; `GedcomExportService` writing standard `.ged` format from the current family's people + relationships
- [x] **Mobile/tablet responsive layout (2026-06-17)** — `@media (max-width: 768px)` breakpoint across AppBar (hamburger nav + centered user identity), person drawer (full-width — with a MudBlazor `--mud-drawer-width` variable gotcha fixed along the way), toolbar (defaults collapsed via a JS viewport watcher, expanded state docks as a full-width footer)
- [x] **Stories feature (2026-06-17)** — see `docs/FutureFeatures/stories-table.md` and `story-invite-flow.md` for what shipped (superseded `story-invite-email.md`)
- [ ] **[DATA SAFETY — High] Enable long-term SQL backup retention (2026-06-18)** — checked `arborkin-sql`/`FamilyTreeDb` (Hyperscale) via `az sql db str-policy show` / `ltr-policy show`: short-term PITR retention is 7 days (automatic, can't disable), but long-term retention is fully off (`weeklyRetention`/`monthlyRetention`/`yearlyRetention` all `PT0S`), and backup storage redundancy is `Local` (not geo-redundant). A bad edit or bug not caught within a week is unrecoverable. Configure an LTR policy (e.g. weekly retention for a few months) before inviting family members who'll be entering irreplaceable data.
- [ ] **[DATA SAFETY] Automate an offline `.bacpac` export as a second backup layer (2026-06-18)** — considered automating a CSV export of SQL data as a cheap backup; rejected because per-table CSV loses FKs/identity values/schema, making restore a manual reconstruction instead of a clean restore. Better option: script `az sql db export` (full schema+data .bacpac) to a storage account or local file on a schedule — cheap, full-fidelity, restorable via `az sql db import`. Complements (doesn't replace) the LTR policy above.
- [ ] **Verify `Ops:AlertEmail`/`SuperUser:Email` App Service settings (2026-06-18)** — attempted via `az webapp config appsettings list --name arborkin --resource-group arborkin-rg`; failed with a persistent TLS connection reset specific to that call (SQL CLI calls against the same subscription worked fine), so this looks like a local network/proxy issue rather than an Azure-side one. Re-check via Portal or retry CLI from a different network to confirm the migration-failure alert email is actually configured.
- [ ] **Azure Monitor alert for App Service health/5xx** — Action Group + alert rule in the Portal so a failed deploy/startup crash pages even if the in-app migration-failure email itself never fires; complements the `Program.cs` try/catch+email already in place
- [ ] **Cache-busting for dynamically-imported JS** (`ftDrag.js`, `canvas-interaction.js`) — optional; only worth doing if stale-browser-module confusion during dev keeps costing real time. Not a production risk (fresh page load = fresh fetch for every user); purely a dev-loop nicety
- [ ] **Detailed import progress bar with live narration (2026-06-23)** — the extract step currently shows only an indeterminate spinner ("Analyzing document with Claude AI…") during a 2+ minute opaque API call, so the user has no idea whether it's working or how far along it is. Replace it with a narrating progress UI:
    - **Stream the Claude response** — switch `ClaudeImportService.ExtractFromTextAsync` to `stream: true`, read the SSE response (`HttpCompletionOption.ResponseHeadersRead`), accumulate `content_block_delta` text deltas into a `StringBuilder`, then parse the assembled JSON exactly as today. Streaming is also the more robust path for large docs — it sidesteps the non-streaming server-side time limit and pairs with bumping `max_tokens` to 64000.
    - **Live people counter** — as deltas arrive, count `"firstName"` (or `"id":`) occurrences and report a real, advancing signal: *"Reading family records… 147 people found so far"*. This is the honest progress indicator — a true percentage is impossible because the total isn't known until the response finishes.
    - **Narrate the real local stages we already know** — "Reading PDF — N pages / X characters" (iText gives this instantly), "Sending to Claude…", "Parsing N people…".
    - **Plumbing** — thread an `IProgress<ImportProgress>` (stage + count + message) through `ExtractFromDocumentAsync`/`ExtractFromTextAsync` (interface change), and in `ImportFormPanel` update a `MudProgressLinear` + status line via `InvokeAsync(StateHasChanged)` on each callback. Effort: ~half a day (Tier 2 of the two options scoped on 2026-06-23).
- [ ] **Large-tree rendering — generation-row layout (2026-06-23)** — the birth-year timeline layout produces long/ragged/canvas-spanning connectors on big imports (255-person Herskovitz tree). Switch large trees to a layered generation-row layout with orthogonal bus connectors (short verticals + one sibling bus per couple, no diagonals), crossing-reduced sibling ordering, and the focus+degrees view filter as the mandatory safety valve. Full spec in `docs/architecture-decisions/002-generation-row-layout.md`. This is the "second massive lift" (drawing) complementing the import-pipeline lift.
- [x] **[SECURITY — High] Branch protection on `master`** (2026-06-18) — added rule via `gh api`: requires a PR (no direct pushes), requires the `build-and-test` CI check to pass with branch up-to-date (`strict: true`), 0 required approvals (solo dev), `enforce_admins: false` so admin bypass remains available for emergencies, force-pushes and branch deletion blocked.

---

## Recently Completed

- [x] **Naming conventions reviewed (2026-06-17)** — audited table/column naming after adding `Stories`; kept PascalCase table names, plural table nouns (`People`, `Media`, `Stories`, etc.) with singular C# entity classes, and plain `Id` PKs. This already matches standard .NET/EF Core + SQL Server convention (lowercase-snake_case is Postgres/Rails advice, not applicable here; `TableNameId`-style PKs would conflict with ASP.NET Identity's own `Id` columns). No renames needed.
- [x] **Admin Imports tab** — replaced placeholder alert with full `ImportFormPanel` component; extracted import UI (GEDCOM/PDF/CSV/paste tabs + action bar) into shared `ImportFormPanel.razor`; used in both `/import` page and admin tab; import history table preserved below the form; Blazor stale-circuit reconnect modal also added
- [x] **Dashboard** — `/dashboard` hub with family stats (total/living/generations/photos), quick actions, recently added list, "Coming soon" export section, feature-request and contact-admin buttons
- [x] **Import family data** — `/import` page with GEDCOM / PDF / CSV / paste-text tabs; `IImportService` (`ClaudeImportService`) backend; `ImportBatch` model + `AddImportBatch` migration; preview-before-commit flow
- [x] **PDF/document import actually wired up (2026-06-23)** — the PDF/Document tab previously just showed a "coming soon" toast; now `ExtractFromDocumentAsync` extracts text from `.pdf` (via iText7) or `.txt` and feeds it through the same Claude extraction prompt the paste-text tab uses, inheriting per-record approval checkboxes and batch rollback for free. `.doc`/`.docx` still unsupported. Considered `UglyToad.PdfPig` first but rejected it — its NuGet history jumps straight from `0.1.9-alpha001-patch1` to a non-standard `1.7.0-custom-5` prerelease, a likely compromised-package signature; switched to `itext7` (normal incremental version history).
- [x] **Import reliability + page-range + entity-resolution (2026-06-23)** — a real 255-person PDF import surfaced several gaps, all addressed:
    - **Timeout/retry fixes** — the `anthropic` HttpClient used the default 100s timeout (large extractions take ~2 min and were cancelled mid-flight, but still billed); bumped to 10 min in `Program.cs`. The uploaded `IBrowserFile` was re-read lazily at extract time and went stale on retry (`_blazorFilesById` null error); now read once at upload into a cached `byte[]`.
    - **Drag-and-drop fix** — `MediaUploadZone`'s `<label>` had `@ondrop:preventDefault` which cancelled the native file-input's own drop handling, so dropped files never landed. Removed the preventDefault; the invisible overlay input now captures drops natively.
    - **PDF page-range selection** — `GetPdfPageCount` + clamped `startPage`/`endPage` through the extraction chain; UI shows the page count and optional "extract pages X–Y" inputs, so a huge doc can be imported a branch at a time (smaller, cheaper Claude calls).
    - **Match-to-existing-people** — new `ImportMatchService` (fuzzy first+last name, alias/maiden aware, birth-year proximity, hard year-conflict exclusion) suggests existing tree members per imported person; `AnnotateMatchesAsync` populates candidates; the preview offers suggestion-only "Link / Create new" (default Create new). On commit a linked person reuses the existing Guid (no duplicate) so relationships graft onto the real tree instead of forming an island. Relationships are now tagged with `ImportBatchId` (`AddRelationshipImportBatchId` migration) so rollback also removes bridging links without touching the pre-existing person.
    - **Admin Deleted tab paging** — `MudTablePager` (25/50/100) so the post-rollback 99+ list is paged.
- [x] **Feature request backend** — `UserMessage` DB model + `AddUserMessages` migration; `IUserMessageService.SubmitFeatureRequestAsync`; Admin panel "Messages" tab shows all submissions with type badge (Feature / Message)
- [x] **Password reset email** — `IEmailSender` abstraction; `SmtpEmailSender` (Gmail SMTP) + `LogEmailSender` (dev console fallback); `AuthService` sends reset link automatically; Gmail App Password configured in Azure
- [x] **Family scoping** — `ICurrentUserService` (interface in Core, impl in Web); `FamilyId` claim baked into auth cookie; all service queries scoped; `CreatedBy`/`UpdatedBy`/`DeletedBy` stamped on every write; super-users bypass scoping
- [x] **DB performance indexes** — filtered composite indexes on People, Relationships, AuditLogs via `AddPerformanceIndexes` migration
- [x] **Testing** — 11 xUnit tests with InMemory EF; covers create/audit stamping/family scoping/soft delete/restore/canonical ordering/duplicate prevention
- [x] **Auth** — ASP.NET Core Identity with cookie auth; Google OAuth + email/password login; invite-only registration (`Auth:RegistrationMode`); rate limiting on `/auth/do-login` (5 req/15 min/IP); account lockout after 5 failed attempts; super-user bootstrap on startup; `LoginOverlay`, `Register.razor` with `?invite=<id>` query param (opaque invite-row id, not the raw token); `DevAuth` bypass for dev; `Admin.razor` with dashboard, deleted people, audit log, users, and activity tabs
- [x] **SVG export — multi-style/theme** — `ExportDialog` updated with Style (Classic/Minimal/Dark), Color Theme (Forest/Ocean/Sepia/Mono), and Degrees of Separation slider; BFS filter to include only people within N hops of focus person; `SvgExportService` rewired with 12 hand-tuned palettes, radial gradients, drop shadows, decade bands (Classic), and flat rendering (Minimal/Dark)
- [x] **Focus persisted to DB** — `AppUser.FocusPersonId` column added (migration `20260609165520_AddUserFocusPerson`); `IAuthService.SaveFocusPersonAsync` / `GetFocusPersonIdAsync`; `ResolveFocusAsync` chains DB → localStorage → first person; login/Google OAuth redirects to `/?focus=<id>` using `FocusPersonId ?? PersonId`
- [x] **Search opens drawer without re-centering** — search now navigates to `/?view=<id>` instead of `/?focus=<id>`; `OnParametersSetAsync` handles `View` param (opens drawer only) separately from `Focus` param (re-centers tree + opens drawer)
- [x] **CI/CD split** — `deploy-web.yml` is now `workflow_dispatch` only (manual deploy); new `ci.yml` runs build + test on every push to `master`/`main`; `deploy-api.yml` deleted (leftover from old separate-API architecture — Core is now a class library, not a deployable)
- [x] **Pre-auth iteration** — soft delete on Person/Relationship/Medium (`DeletedAt`/`DeletedBy` + EF Global Query Filters); `AuditLog`, `UserActivity`, `AppUser`, `UserFamily`, `UserInvite` tables created; `IAuditLogService` writes on Create/Update/Delete/Restore; `PersonService.DeleteAsync` is now a soft delete; `PersonService.RestoreAsync` + `GetDeletedAsync` added
- [x] **Export dialog (initial)** — toolbar FileDownload button opens `ExportDialog` with scope (all/immediate/ancestors/descendants) and format (JSON/CSV); downloads via `ftDownloadFile` JS; RFC 4180-compliant CSV
- [x] `FamilyId` schema migration — `Family` table added; `Person.FamilyId` (nullable FK); seeder creates "My Family" and assigns all seeded people to it
- [x] Cross-root couple layout fix — children of two sibling-in-law families (e.g. Rose/Ray and Bud/Florence) no longer tangle horizontally; each partner placed as leaf under own parent group, children centered at couple midpoint
- [x] Former/divorced spouse support — `Relationship.EndDate` distinguishes active (null) from former (set); `PersonDto.FormerSpouseIds` added; dashed grey 💔 connector vs. solid green ❤ in canvas; full UI in `PersonForm`
- [x] Classic pedigree T-bar connectors — replaced bezier arcs with straight horizontal couple lines, vertical stems, T-bars, and vertical drops
- [x] Draggable toolbar and hero overlay — Blazor owns position state; JS calls `OnDragEnd` on mouseup; positions persisted to `localStorage`
- [x] Reset button on toolbar — clears drag positions, removes `localStorage` entries, centers tree
- [x] Focus persistence — `localStorage` saves the focused person's Id; survives page refresh; URL `?focus=<id>` overrides and re-saves
- [x] Shareable link — "Share" button copies `/?focus=<id>` URL to clipboard
- [x] Sibling inference dialog — after adding a sibling, offers to link that sibling's existing siblings
- [x] `QuestionDetector` — extracts questions from biography notes and surfaces them as toasts post-save
- [x] Blob storage — Azure Blob Storage integrated for photo uploads via `BlobStorageService`
- [x] Multi-photo upload with primary photo selection
- [x] All person fields saved (birthplace, gender, maiden name, biography, etc.)
- [x] `ChildIds` and `SiblingIds` persisted in relationship sync
- [x] Dark/light mode toggle with `localStorage` persistence

---

## Vision: A Family Tree App That Tells Stories

### Phase 1 — Core foundation ✅ (largely complete)
| Area | Status |
|------|--------|
| Data model — Person, relationships, notes, photos | ✅ done |
| CRUD UI — add/edit/delete with clean forms | ✅ done |
| Tree view — zoom, pan, focus, T-bar connectors | ✅ done |
| Theme — light/dark mode | ✅ done |
| Hosting — Azure App Service + Blob Storage | ✅ done |
| Accounts — sign in (Google, role-based) | ✅ done |
| Privacy — private tree by default | ✅ done |

---

### Phase 2 — Personalization & storytelling
| Area | Features |
|------|----------|
| Profile cards | Biography, photos, life events |
| Events table | Structured life events per person (bar mitzvah, immigration, military, etc.) — see `FutureFeatures/events-table.md` |
| Stories table | ✅ done — see `FutureFeatures/stories-table.md` |
| Timeline view | Chronological events + stories per person — see `FutureFeatures/person-timeline-view.md` |
| Media | Documents, voice clips (photos already done) |
| Design polish | Smooth transitions |
| Search & filter | By name, year, or relation |
| Responsive layout | ✅ done (2026-06-17) — mobile-friendly tree and forms |

---

### Phase 3 — Collaboration & sharing
| Area | Features |
|------|----------|
| Story invite flow | ✅ done — see `FutureFeatures/story-invite-flow.md` |
| Share a memory / Public profiles | Logged-in family members submit memories; optional public read-only profile URL — see `FutureFeatures/share-memory-public-profile.md` |
| Invites | Share tree with family (viewer/editor roles) |
| Change history | "Recently added/updated" feed |
| Comments | Collaborative memories on person profiles |
| Notifications | Birthday reminders, new photo alerts |
| Public view | Optional public tree with privacy controls |
| Real-time presence | See who else is browsing concurrently |

**Admin dashboard ideas (from 2026-06-06 brainstorm):**
- Track all changes with audit log
- Admin can disable features per user or lock out users
- Optional daily CRUD cap (e.g. 10 ops/day) with ability to request more
- Manual "donor" flag (e.g. Venmo confirmed) unlocking additional privileges
- Visibility limit by relationship depth (e.g. only see people within 3 links) — complex but possible via BFS on the graph

---

### Phase 4 — Intelligence & insights
| Area | Features |
|------|----------|
| Relationship inference | Auto-detect likely missing links |
| Duplicate detection | Merge similar people |
| Age & generation analytics | Oldest ancestor, average lifespan, etc. |
| AI suggestions | "Would you like to add Mary as Emma's grandmother?" |
| Export/import | GEDCOM, PDF, CSV |

---

### Phase 5 — Growth & polish
| Area | Features |
|------|----------|
| Themes | Family crest, color palette, custom fonts |
| Performance | Lazy loading for large trees |
| Localization | Multi-language support |
| Mobile layout | Improved canvas and form experience on phone |

---

## Optional "wow" features
- **Timeline slider** — drag to filter visible generations by year range
- **Animated connectors** — subtle pulse along parent lines when focusing
- **Photo avatars** — circular crops with soft shadows replacing initials
- **GEDCOM import/export** — interoperability with standard genealogy tools
- **AI-assisted suggestions** — relationship completion from biography text