# Sources & Citations

**Status:** Planned — not yet implemented; depends on nothing, but is most useful once `Stories` (`stories-table.md`), `Events` (`events-table.md`), and `PersonFacts` (`person-facts.md`) exist
**Goal:** Let any story, event, or fact say *how we know this* — a document, an interview, a photo caption — so the next generation can trust the data instead of just inheriting unsourced claims.

---

## Problem

Right now every piece of family data — a story, a birthdate, a claimed occupation — is a bare assertion with no provenance. Genealogy data rots in exactly this way: once the person who remembered *why* something is true is gone, an unsourced fact and a fabricated one are indistinguishable. This is a standard part of genealogy data models (GEDCOM has had `SOUR` records since its first version) and is cheap to add now, before there's a backlog of unsourced data to retrofit.

## Solution

A new `Sources` table — a document, interview, photograph, public record, or website. Citable records (`Stories`, `Events`, `PersonFacts`) get an additive, nullable `SourceId` FK. A source is optional everywhere it applies; nothing requires backfilling existing data.

Kept deliberately simple for v1: **one source per citable row**, not a many-to-many join table. A story that draws on three documents is the edge case, not the common case — if that need shows up later, upgrading a nullable FK to a join table is a small, additive migration. Don't build the join table speculatively now.

---

## DB Schema

**New table: `Sources`**

| Column | Type | Notes |
|--------|------|-------|
| `Id` | `Guid` | PK |
| `Type` | `SourceType` (enum) | See enum values below |
| `Title` | `string(300)` | Required — e.g. "Ellis Island manifest, 1921" |
| `Url` | `string(500)?` | Nullable — link to a digitized record or website |
| `Notes` | `string(1000)?` | Nullable — where the physical original is, who provided it, etc. |
| `CreatedAt` | `DateTime` | UTC |
| `CreatedBy` | `Guid?` | FK → `AspNetUsers` |

**`SourceType` enum (in `FamilyTree.Shared`):**

```
Document, Interview, Photograph, PublicRecord, Website, FamilyTradition, Other
```

`FamilyTradition` is deliberately included — "Grandma always said so" is a real, common provenance level in family trees, and distinct from a vetted public record. It's honest to be able to say a fact's source is oral tradition rather than force it into `Other`.

**Additive changes to existing/planned tables:**

| Table | Column | Notes |
|-------|--------|-------|
| `Stories` | `SourceId` | `Guid?`, FK → `Sources` |
| `Events` | `SourceId` | `Guid?`, FK → `Sources` |
| `PersonFacts` | `SourceId` | `Guid?`, FK → `Sources` (already included in `person-facts.md`'s schema) |

---

## Files to Create / Modify

| File | Change |
|------|--------|
| `src/FamilyTree.Shared/Enums/SourceType.cs` | **New** — `SourceType` enum |
| `src/FamilyTree.Shared/DTOs/SourceDto.cs` | **New** — read model |
| `src/FamilyTree.Shared/DTOs/SourceUpsertDto.cs` | **New** — write model |
| `src/FamilyTree.Core/Entities/Source.cs` | **New** — EF entity |
| `src/FamilyTree.Core/Entities/Story.cs` / `Event.cs` / `PersonFact.cs` | Add nullable `SourceId` + nav property |
| `src/FamilyTree.Core/Data/AppDbContext.cs` | Add `DbSet<Source> Sources` |
| `src/FamilyTree.Core/Services/ISourceService.cs` | **New** — `GetAllAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync` |
| `src/FamilyTree.Core/Services/SourceService.cs` | **New** — implementation; returns `ServiceResponse<T>` |
| `src/FamilyTree.Core/Program.cs` | Register `ISourceService` → `SourceService` |
| EF migration | **New** — `AddSourcesTable` |
| `src/FamilyTree.Web/Modules/Components/SourcePicker.razor` | **New** — searchable "pick or create a source" control, reused on Story/Event/Fact forms |

**No changes to:** `Person.cs`, `PersonDto.cs`, `PersonService.cs`.

---

## Implementation Notes

- Deleting a `Source` should **not** cascade-delete the story/event/fact that cites it — set `SourceId` to null instead (`ON DELETE SET NULL`). Losing the citation is a downgrade, not a reason to lose the family data itself.
- `SourcePicker` should support free-text "create a new source inline" so citing something doesn't require leaving the form you're already filling out
- Sort sources by `CreatedAt` descending in the picker's "recent" list, with search by `Title`
- This is intentionally the last of the Person-expansion docs to build — it adds the most value once there's actual story/event/fact data to attach citations to

---

## Verification

1. `dotnet build FamilyTree.sln`
2. `dotnet ef database update` — confirm `Sources` table and the three new nullable FKs
3. Create a source and cite it from a story → confirm the citation displays on the story
4. Delete that source → confirm the story survives with `SourceId` cleared, not deleted
5. Cite the same source from a fact and an event → confirm one `Source` row serves all three
