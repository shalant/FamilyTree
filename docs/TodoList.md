# Family App — Todo List

---

## In Progress / Up Next

- [ ] **DB performance indexes** — add filtered composite indexes on `(FamilyId, LastName, FirstName) WHERE DeletedAt IS NULL` (People), `(PersonAId/PersonBId) WHERE DeletedAt IS NULL` (Relationships), and `(EntityType, EntityId, Timestamp DESC)` (AuditLog) via new EF migration `AddPerformanceIndexes`
- [ ] **Testing** — bUnit component tests for `PersonForm`, `FamilyTreeCanvas`; xUnit + FluentAssertions integration tests for `PersonService` and `RelationshipService` against real DB
- [ ] **Email verification & password reset** — `UserManager` token flow + SendGrid; log token URL to console in dev
- [ ] **Family scoping** — add `.Where(p => p.FamilyId == currentFamilyId)` to all service queries; populate `CreatedBy`/`UpdatedBy`/`AuditLog.UserId` from current user on every write
- [ ] **Feature request backend** — store `RequestFeatureDialog` submissions in a DB table or forward via SendGrid

---

## Recently Completed

- [x] **Auth** — ASP.NET Core Identity with cookie auth; Google OAuth + email/password login; invite-only registration (`Auth:RegistrationMode`); rate limiting on `/auth/do-login` (5 req/15 min/IP); account lockout after 5 failed attempts; super-user bootstrap on startup; `LoginOverlay`, `Register.razor` with `?invite=<token>` query param; `DevAuth` bypass for dev; `Admin.razor` with dashboard, deleted people, audit log, users, and activity tabs
- [x] **SVG export — multi-style/theme** — `ExportDialog` updated with Style (Classic/Minimal/Dark), Color Theme (Forest/Ocean/Sepia/Mono), and Degrees of Separation slider; BFS filter to include only people within N hops of focus person; `SvgExportService` rewired with 12 hand-tuned palettes, radial gradients, drop shadows, decade bands (Classic), and flat rendering (Minimal/Dark)
- [x] **Focus persisted to DB** — `AppUser.FocusPersonId` column added (migration `20260609165520_AddUserFocusPerson`); `IAuthService.SaveFocusPersonAsync` / `GetFocusPersonIdAsync`; `ResolveFocusAsync` chains DB → localStorage → first person; login/Google OAuth redirects to `/?focus=<id>` using `FocusPersonId ?? PersonId`
- [x] **Search opens drawer without re-centering** — search now navigates to `/?view=<id>` instead of `/?focus=<id>`; `OnParametersSetAsync` handles `View` param (opens drawer only) separately from `Focus` param (re-centers tree + opens drawer)
- [x] **CI/CD split** — `deploy-web.yml` is now `workflow_dispatch` only (manual deploy); new `ci.yml` runs build + test on every push to `master`/`main`
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
| Privacy — private tree by default | 🔲 family scoping |

---

### Phase 2 — Personalization & storytelling
| Area | Features |
|------|----------|
| Profile cards | Biography, photos, life events |
| Timeline view | Chronological events per person |
| Media | Documents, voice clips (photos already done) |
| Design polish | Smooth transitions |
| Search & filter | By name, year, or relation |
| Responsive layout | Mobile-friendly tree and forms |

---

### Phase 3 — Collaboration & sharing
| Area | Features |
|------|----------|
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
