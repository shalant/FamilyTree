# Family App — Todo List

---

## In Progress / Up Next

- [ ] **Auth** — ASP.NET Core Identity + Google social login; Admin and Viewer roles; resolve `FamilyId` from claims and filter all service queries by it
- [ ] **Photo upload** — replace `ProfilePhotoUrl` plain-string with actual file upload to Azure Blob Storage (infrastructure already exists: `BlobStorageService`)

---

## Recently Completed

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
| Accounts — sign in (Google, role-based) | 🔲 next |
| Privacy — private tree by default | 🔲 with auth |

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
