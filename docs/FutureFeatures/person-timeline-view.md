# Person Timeline View

**Status:** Planned — depends on Events table and Stories table being implemented first  
**Goal:** A chronological, visual timeline of a person's life rendered in the UI — the "wow" feature that ties structured events and prose stories together into a single narrative view.

---

## Problem

Even after Events and Stories are implemented, the data will exist only as separate lists. There is no unified, chronological view that lets a family member (or a recruiter looking at the portfolio) see a person's full life arc at a glance.

## Solution

A new Blazor component `PersonTimeline` that merges Events and Stories into a single time-sorted feed, rendered as a vertical timeline. Accessible from the `PersonDetailDrawer` or a dedicated person profile page.

---

## UI Design

- Vertical timeline with a center spine
- Each entry is either an **Event** (icon + date + place) or a **Story** (author avatar + title + excerpt)
- Events and Stories interleave by date — a story attached to an event appears directly beneath it
- Entries without a date float to the bottom in a separate "Undated" section
- Responsive: collapses to a single-column layout on mobile

**Visual language:**
- Event icons map to `EventType` (e.g. ✡ for BarMitzvah, ✈ for Immigration, ⚔ for Military)
- Story cards show the author's display name and `CreatedAt`
- ArborKin green accent on the spine; dark/light mode aware via existing CSS variables

---

## Component Responsibilities

| Component | Role |
|-----------|------|
| `PersonTimeline` | Orchestrator — fetches events + stories, merges and sorts them, renders the spine |
| `TimelineEventEntry` | Presentational — renders a single Event row |
| `TimelineStoryEntry` | Presentational — renders a single Story card with excerpt |

---

## Files to Create / Modify

| File | Change |
|------|--------|
| `src/FamilyTree.Web/Modules/Components/PersonTimeline.razor` | **New** — main timeline component |
| `src/FamilyTree.Web/Modules/Components/TimelineEventEntry.razor` | **New** — event row |
| `src/FamilyTree.Web/Modules/Components/TimelineStoryEntry.razor` | **New** — story card |
| `src/FamilyTree.Web/Modules/Pages/PersonDetailDrawer.razor` | Add timeline tab or section |
| `src/FamilyTree.Web/wwwroot/css/` | Timeline spine + card styles |

**No new DB tables.** Purely a UI/presentation layer over the Events and Stories services.

---

## Implementation Notes

- Merge strategy: create a unified `TimelineEntryViewModel` (local, not a shared DTO) with a `SortDate`, `EntryType` (Event or Story), and the underlying object
- Sort by `SortDate` ascending; use `DateTime.MaxValue` as sentinel for undated entries
- Load events via `IEventService.GetByPersonAsync` and stories via `IStoryService.GetByPersonAsync` — two parallel service calls in `OnInitializedAsync`
- This component is a strong portfolio piece: consider making it the default view on the public read-only profile page (see `share-memory-public-profile.md`)

---

## Verification

1. `dotnet build FamilyTree.sln`
2. Open a person with at least one event and one story — confirm both appear in date order
3. Add an undated story — confirm it appears in the "Undated" section
4. Toggle dark/light mode — confirm timeline renders correctly in both
