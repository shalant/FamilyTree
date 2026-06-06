# Family App Todo

## Quick Wins (In Progress)
- [ ] Auth — Identity/OAuth setup + token validation across API & Web
- [x] ~~Blob~~ — Azure Blob Storage integrated for photo uploads
- [x] ~~Validation~~ — Data validation on DTOs + business logic + file handling
- [x] ~~Toolbar~~ — Floating draggable toolbar + draggable header

## Completed Features
- [x] ~~Delete photo~~
- [x] ~~Edit photo captions~~
- [x] ~~Load preview URLs in PersonDetailDrawer~~
- [x] ~~Multi-photo upload with primary photo selection~~
- [x] ~~Siblings field in person form~~
- [x] ~~Multiple parents/spouses/children (autocomplete reset)~~
- [x] ~~ChildIds and SiblingIds persisted in relationship sync~~
- [x] ~~All person fields saved (birthplace, gender, maiden name, etc.)~~
- [x] ~~Tree generation height compressed for 17" laptops~~

## Relationship Enhancements
- [ ] **Divorce** — UI to set EndDate on a Spouse relationship (infrastructure already exists: `Relationship.EndDate`). Needs: "Mark as divorced" action on spouse chips, ex-spouse label in drawer, dashed connector in tree for ended marriages. Schema: no migration needed, `RelationshipDto` needs `EndDate` + `IsActive` exposed, `PersonDto.SpouseIds` split into active/former.
- [ ] **Focus persistence** — persist the user's default focus person to localStorage or a user preferences table so it survives page refresh

---

# Vision: A Family Tree App That Tells Stories

## 🧭 Phase 1 — Core foundation (MVP)
**Goal:** make it usable and trustworthy for everyday people.

### ✅ Essential features
| Area | Features |
|------|----------|
| Accounts | Simple sign in (Google, Microsoft, Apple, email/password) |
| Data model | Person, relationships (parent, spouse, child), notes, photos |
| CRUD UI | Add/edit/delete people with clean forms |
| Tree view | Interactive visualization (zoom, pan, focus on person) |
| Theme | Light/dark mode toggle |
| Hosting & storage | Azure App Service + Blob Storage for photos |
| Privacy | Private tree by default; shareable link later |

**Outcome:** Users can sign in, build their own tree, and see it beautifully rendered.

---

## 🌿 Phase 2 — Personalization & storytelling
**Goal:** make it emotionally engaging.

### ✨ Features
| Area | Features |
|------|----------|
| Profile cards | Add biography, photos, and life events |
| Timeline view | Chronological life events per person |
| Media upload | ~~Photos~~ (done), documents, voice clips |
| Design polish | Generational color bands, smooth transitions |
| Search & filter | Quick search by name, year, or relation |
| Responsive layout | Mobile friendly tree and forms |

**Outcome:** Users feel like they're telling their family's story, not just entering data.

---

## 🧩 Phase 3 — Collaboration & sharing
**Goal:** make it social and communal.

### 🤝 Features
| Area | Features |
|------|----------|
| Invites | Share tree with family members (viewer/editor roles) |
| Change history | "Recently added" or "updated" feed |
| Comments | Add notes or memories collaboratively |
| Notifications | Birthday reminders, new photo alerts |
| Public view | Optional public tree with privacy controls |

**Outcome:** Families can co-create and preserve their history together.

---

## 🧠 Phase 4 — Intelligence & insights
**Goal:** make it smart and helpful.

### 🧬 Features
| Area | Features |
|------|----------|
| Relationship inference | Auto detect missing links ("Linda is likely James's spouse") |
| Duplicate detection | Merge similar people |
| Age & generation analytics | Oldest ancestor, youngest member, average lifespan |
| AI suggestions | "Would you like to add Mary as Emma's grandmother?" |
| Export/import | GEDCOM, PDF, or CSV export for genealogy enthusiasts |

**Outcome:** The app feels intelligent — it helps users build and understand their tree.

---

## 🌍 Phase 5 — Growth & polish
**Goal:** make it delightful and scalable.

### 🚀 Features
| Area | Features |
|------|----------|
| Themes & customization | Family crest, color palette, fonts |
| Performance optimization | Lazy loading for large trees |
| Localization | Multi-language support |
| Marketing site | Showcase demo trees, testimonials |
| Analytics | Track engagement and growth |

**Outcome:** A polished, scalable product ready for public launch.

---

## 🧭 Suggested build order
1. MVP (Phase 1) — 6–8 weeks
2. Storytelling (Phase 2) — 4–6 weeks
3. Collaboration (Phase 3) — 6–8 weeks
4. Intelligence (Phase 4) — ongoing enhancements
5. Polish (Phase 5) — continuous refinement



GEDCOM import/export

Collaboration mode

Timeline view

AI‑assisted relationship suggestions

Mobile layout improvements


🌈 Optional “wow” features

Timeline slider: drag to filter visible generations by year range.

Animated connectors: subtle pulse along parent lines when focusing.

Photo avatars: circular crops with soft shadows.

Shareable view: generate a read‑only link for a specific focus person.





Brainstorming on auth, 6 June 26:
- I'd like an admin dashboard to track changes
- admin should have the ability to turn off various features for a user. additional, i can lock out users. perhaps even cap daily CRUD totals at 10x unless they ask for permission
- would there be a way to limit a user's visilibity to something like 3 linkages? is this too complex?
- is there an easy way to  manually mark if they have donated via Venmo and have additionally privileges?
- would instant messaging and knowing if other people are using concurrently be too complex?
