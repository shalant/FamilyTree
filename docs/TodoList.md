# Family App — Todo List

---

## In Progress / Up Next

- [x] **Deploy** — pushed to production; user testing in progress at arborkin-erbufqfkhzcka4cb.centralus-01.azurewebsites.net
- [ ] **[SECURITY — Critical] Family-scope check in `GetByIdAsync`** — `PersonService`, `RelationshipService`, and `MediumService` all lack a `FamilyId` guard on single-entity lookups; any authenticated user who knows a GUID can read across family boundaries
- [x] **[SECURITY — Critical] `DevAuth` production guard** — add a startup assertion that `DevAuth:Enabled` is false outside of Development environment
- [ ] **[SECURITY — High] Lock `AllowedHosts`** — change `"AllowedHosts": "*"` to the actual production domain in `appsettings.json`
- [ ] **[SECURITY — High] Add security response headers** — Content-Security-Policy, X-Frame-Options, X-Content-Type-Options, Referrer-Policy (middleware in `Program.cs`)
- [ ] **[SECURITY — High] Strengthen password policy** — re-enable at least one of `RequireDigit`/`RequireUppercase`/`RequireNonAlphanumeric` in `Program.cs` ~L74
- [ ] **[SECURITY — Medium] Move reset/invite tokens out of URL** — submit via POST body instead of query string to prevent token leakage via `Referer` and server logs (`AuthService.cs` ~L204)
- [ ] **[SECURITY — Medium] Upgrade `Azure.Storage.Blobs`** — replace `12.29.0-beta.1` with a stable release in `FamilyTree.Core.csproj`
- [ ] **[SECURITY — Medium] Surface `AuditLogService` failures** — exceptions are silently swallowed (~L48); at minimum log at Error level so admins are aware
- [ ] **[SECURITY — Medium] MFA/2FA** — `security.md` documents this as required for Admin/SuperUser but it is not yet implemented
- [ ] **[SECURITY — DO BEFORE 2ND FAMILY] Family scoping gap** — `RegisterAsync` does not create a `UserFamily` row after invite acceptance; users land in a permissive state where they see all families' data. Safe while single-family; becomes a real data-isolation failure the moment a second family is created. Fix requires: (1) add `UserFamily` insert in `AuthService.RegisterAsync` after marking invite accepted; (2) run backfill SQL on prod DB (`INSERT INTO UserFamilies` for any users missing a row). See security audit notes.
- [ ] **[SECURITY — Low] NuGet lock file** — add `RestorePackagesWithLockFile = true` to prevent dependency-confusion attacks
- [ ] **GEDCOM export** — wire the "Coming soon" stub in Dashboard; `GedcomExportService` writing standard `.ged` format from the current family's people + relationships
- [ ] **Mobile layout** — tree canvas and forms are not optimized for small screens; no `@media` breakpoints exist yet
- [ ] **Story invite email** — one-click beautiful invite email featuring a family member's story; see `docs/FutureFeatures/story-invite-email.md`

---

## Recently Completed

- [x] **Admin Imports tab** — replaced placeholder alert with full `ImportFormPanel` component; extracted import UI (GEDCOM/PDF/CSV/paste tabs + action bar) into shared `ImportFormPanel.razor`; used in both `/import` page and admin tab; import history table preserved below the form; Blazor stale-circuit reconnect modal also added
- [x] **Dashboard** — `/dashboard` hub with family stats (total/living/generations/photos), quick actions, recently added list, "Coming soon" export section, feature-request and contact-admin buttons
- [x] **Import family data** — `/import` page with GEDCOM / PDF / CSV / paste-text tabs; `IImportService` (`ClaudeImportService`) backend; `ImportBatch` model + `AddImportBatch` migration; preview-before-commit flow
- [x] **Feature request backend** — `UserMessage` DB model + `AddUserMessages` migration; `IUserMessageService.SubmitFeatureRequestAsync`; Admin panel "Messages" tab shows all submissions with type badge (Feature / Message)
- [x] **Password reset email** — `IEmailSender` abstraction; `SmtpEmailSender` (Gmail SMTP) + `LogEmailSender` (dev console fallback); `AuthService` sends reset link automatically; Gmail App Password configured in Azure
- [x] **Family scoping** — `ICurrentUserService` (interface in Core, impl in Web); `FamilyId` claim baked into auth cookie; all service queries scoped; `CreatedBy`/`UpdatedBy`/`DeletedBy` stamped on every write; super-users bypass scoping
- [x] **DB performance indexes** — filtered composite indexes on People, Relationships, AuditLogs via `AddPerformanceIndexes` migration
- [x] **Testing** — 11 xUnit tests with InMemory EF; covers create/audit stamping/family scoping/soft delete/restore/canonical ordering/duplicate prevention
- [x] **Auth** — ASP.NET Core Identity with cookie auth; Google OAuth + email/password login; invite-only registration (`Auth:RegistrationMode`); rate limiting on `/auth/do-login` (5 req/15 min/IP); account lockout after 5 failed attempts; super-user bootstrap on startup; `LoginOverlay`, `Register.razor` with `?invite=<token>` query param; `DevAuth` bypass for dev; `Admin.razor` with dashboard, deleted people, audit log, users, and activity tabs
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
| Stories table | Multiple attributed prose narratives per person — see `FutureFeatures/stories-table.md` |
| Timeline view | Chronological events + stories per person — see `FutureFeatures/person-timeline-view.md` |
| Media | Documents, voice clips (photos already done) |
| Design polish | Smooth transitions |
| Search & filter | By name, year, or relation |
| Responsive layout | Mobile-friendly tree and forms |

---

### Phase 3 — Collaboration & sharing
| Area | Features |
|------|----------|
| Story invite flow | Token-based email inviting non-members to contribute a memory; no login required to submit — see `FutureFeatures/story-invite-flow.md` |
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