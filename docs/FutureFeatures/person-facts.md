# Person Facts Table

**Status:** Planned — not yet implemented
**Goal:** Capture open-ended attributes about a person (occupation, religion, ethnicity, education, military branch, etc.) without adding a new `Person` column every time someone wants to track a new kind of fact.

---

## Problem

The original plan for "Person Data Model Expansion" was to add scalar columns directly to `Person`: `Occupation`, `Religion`, `Ethnicity`. That's exactly the pattern that causes a person/user table to sprawl over time — next year it's education, then native language, then military branch, each needing its own migration, and most rows leave most of those columns null.

## Solution

A new `PersonFacts` table, one row per fact, generic over fact type. Adding a new kind of fact is an enum value, not a migration. This mirrors the standard genealogy data model (GEDCOM treats almost everything beyond name/sex/birth/death as a generic typed fact) and the same shape already used for `Events` (see `events-table.md`) — but facts are *attributes/state*, not *milestones*, so they get their own table rather than being shoehorned into `Events`:

| | Events | Facts |
|---|---|---|
| Shape | Point-in-time milestone | Attribute that may hold over a range, or have no date at all |
| Examples | Immigration, graduation, military service *event* | Occupation, religion, ethnicity, military *branch* |
| Date | Optional single `Date` | Optional `DateRangeStart`/`DateRangeEnd` (e.g. "carpenter, 1948–1965") |

---

## DB Schema

**New table: `PersonFacts`**

| Column | Type | Notes |
|--------|------|-------|
| `Id` | `Guid` | PK |
| `PersonId` | `Guid` | FK → `Persons`, cascade delete |
| `Type` | `FactType` (enum) | See enum values below |
| `Value` | `string(300)` | Required — e.g. "Carpenter", "Catholic", "Irish-American" |
| `DateRangeStart` | `DateOnly?` | Nullable — many facts (religion, ethnicity) have no meaningful range |
| `DateRangeEnd` | `DateOnly?` | Nullable |
| `Notes` | `string(1000)?` | Nullable — additional context |
| `SourceId` | `Guid?` | FK → `Sources`, nullable — see `sources-citations.md` |
| `SortOrder` | `int` | Manual ordering within the same `Type` |

**`FactType` enum (in `FamilyTree.Shared`):**

```
Occupation, Religion, Ethnicity, Nationality, Education,
Language, MilitaryBranch, MilitaryRank, Other
```

---

## Files to Create / Modify

| File | Change |
|------|--------|
| `src/FamilyTree.Shared/Enums/FactType.cs` | **New** — `FactType` enum |
| `src/FamilyTree.Shared/DTOs/PersonFactDto.cs` | **New** — read model |
| `src/FamilyTree.Shared/DTOs/PersonFactUpsertDto.cs` | **New** — write model |
| `src/FamilyTree.Core/Entities/PersonFact.cs` | **New** — EF entity; nav property back to `Person`, optional nav to `Source` |
| `src/FamilyTree.Core/Data/AppDbContext.cs` | Add `DbSet<PersonFact> PersonFacts` |
| `src/FamilyTree.Core/Services/IPersonFactService.cs` | **New** — `GetByPersonAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync` |
| `src/FamilyTree.Core/Services/PersonFactService.cs` | **New** — implementation; returns `ServiceResponse<T>` |
| `src/FamilyTree.Core/Program.cs` | Register `IPersonFactService` → `PersonFactService` |
| EF migration | **New** — `AddPersonFactsTable` |
| `src/FamilyTree.Web/Modules/Pages/` (TBD component) | Facts list + add/edit UI on person detail, grouped by `Type` |

**No changes to:** `Person.cs`, `PersonDto.cs`, `PersonService.cs`, existing migrations. The previously-floated `Occupation`/`Religion`/`Ethnicity` scalar columns are superseded by this table — do not add them to `Person`.

---

## Implementation Notes

- Follow the `ServiceResponse<T>` pattern used throughout
- Use `IDbContextFactory<AppDbContext>` for scoped contexts, not a singleton
- A person can have multiple facts of the same `Type` over time (e.g. two `Occupation` rows for two careers) — this is intentional, not a data error
- Display grouped by `Type`, each group's facts sorted by `DateRangeStart` ascending (nulls last), then `SortOrder`
- Keep `PersonFacts` and `Events` as separate tables rather than merging — their date semantics (point-in-time vs. range-or-none) and editing UX differ enough that a shared table would need nullable-everything compromises on both sides

---

## Verification

1. `dotnet build FamilyTree.sln`
2. `dotnet ef database update` — confirm `PersonFacts` table created with correct FK and indexes
3. Add two `Occupation` facts with different date ranges to one person → confirm both display, sorted correctly
4. Delete a person → confirm their facts cascade-delete
5. Link a fact to a `Source` → confirm the citation displays (once `sources-citations.md` is implemented)
