# FamilyTree — Backlog

> Open items only. Completed work lives in `TodoList.md` ("Recently Completed") and is described as shipped architecture in `CLAUDE.md` — this file is the longer-horizon backlog, organized by phase.

---

## Phase 2 — Auth & Identity (remaining)
- 🔲 Email verification on registration
- 🔲 Onboarding page (post-registration): "Create a family" or "I have an invite code"
- 🔲 Public family search / "find my node" wizard — visibility-tier design already documented in `security.md`, the wizard itself isn't built
- 🔲 Super-admin user deletion (`UserManager.DeleteAsync` in `/admin → Users`)
- 🔲 `UserActivity` daily action counts — schema exists, write-path population pending
- 🔲 Data visibility tiers enforced in the service layer (public / member / admin data shape) — design is in `security.md`, enforcement not yet built
- 🔲 MFA for Super-user/Admin — tracked in `TodoList.md`

---

## Phase 3 — Dashboard & Navigation
- 🔲 Dashboard "Generations" stat: wire to real tree depth calculation (currently placeholder)
- 🔲 Dashboard "Recently added": sort by `CreatedAt`
- 🔲 About page roadmap: keep in sync as items ship

---

## Phase 4 — Import (remaining)
- 🔲 GEDCOM parsing (5.5.1) — preview + commit flow. (AI-based paste/PDF/CSV import already shipped via `ClaudeImportService`.)
- 🔲 Excel/CSV column-mapping import — current import UI's CSV tab is paste-based, not file-upload + column mapping

---

## Phase 5 — Export (remaining)
- 🔲 GEDCOM export — tracked in `TodoList.md`
- 🔲 Excel export (ClosedXML, one sheet per entity)
- 🔲 PDF report (QuestPDF)
- 🔲 Wire remaining "Coming soon" Dashboard export buttons

---

## Phase 6 — Tree Visualization
- 🔲 Virtual/windowed rendering for trees > 200 nodes
- 🔲 Mobile layout (touch-pan, pinch-zoom, condensed nodes) — tracked in `TodoList.md`
- 🔲 Ancestor-only / descendant-only views
- 🔲 Print/screenshot tree
- 🔲 Generation band labels on canvas
- 🔲 Search highlight pulse on matched node

---

## Phase 7 — Person Data Model Expansion
- 🔲 Scalar fields on `Person`: occupation, religion, ethnicity/heritage
- 🔲 Child table `PersonAlternateName` — nicknames, name changes
- 🔲 Child table `PersonResidence` — address/city/country with date range
- 🔲 Marriage date range UI (`StartDate`/`EndDate` already in DB) + divorce/separation relationship type
- 🔲 Generic life-event log — see `FutureFeatures/events-table.md` (supersedes the old ad-hoc event-log idea formerly here)

---

## Phase 8 — Sharing & Multi-User
- 🔲 Invite email send — see `FutureFeatures/story-invite-email.md` (admin invite emails) and `FutureFeatures/story-invite-flow.md` (story invites)
- 🔲 Permission enforcement: gate writes behind Member+ role
- 🔲 Activity feed: recent edits visible to collaborators
- 🔲 Optimistic concurrency conflict resolution (`RowVersion` already on entities, UI not wired)

---

## Phase 9 — Settings & Quality of Life
- 🔲 Settings: wire tree display toggles (photos, compact mode, show years)
- 🔲 Profile page: name/email/avatar
- 🔲 Email notifications: invites, feature request receipts
- 🔲 Keyboard shortcuts (j/k navigation, e = edit, / = search, etc.)
- 🔲 Undo last destructive action (soft-delete + restore within session)
- 🔲 Bulk operations on People list (bulk tag, bulk delete)

---

## Phase 10 — Infrastructure & Production
- 🔲 Custom domain + SSL — tracked in `TodoList.md` (`AllowedHosts` update)
- 🔲 Azure Key Vault for production secrets
- 🔲 Verify Azure SQL backup retention policy in the portal
- 🔲 Application Insights or Sentry for error monitoring
- 🔲 GDPR: cookie notice, data export, right-to-erasure workflow

---

## Ongoing
- 🔲 Integration tests: `PersonService` CRUD against a real DB
- 🔲 Blazor component tests (bUnit): `PersonForm`, `FamilyTreeCanvas`
- 🔲 Accessibility audit: ARIA labels, keyboard navigation, screen reader support
- 🔲 Performance baseline: < 300ms TTFB on dashboard, < 1s tree render for a 100-person tree
- 🔲 SEO: Open Graph tags already added for shared tree links (`Home.razor`) — revisit meta coverage on other pages

---

*Last updated: 2026-06-16.*
