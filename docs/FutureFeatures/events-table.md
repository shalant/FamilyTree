# Events Table

**Status:** Planned — not yet implemented  
**Goal:** Add structured, queryable life events to each person's record, going beyond the current birth/death date fields to capture the full arc of a life.

---

## Problem

`Person` currently stores only `BirthDate`, `BirthPlace`, `DeathDate`, and `DeathPlace`. There is no way to record richer milestones — immigration, military service, bar/bat mitzvah, graduation, marriage — as structured data. These end up buried in `BiographyNotes` as free text, making them unsearchable and impossible to render on a timeline.

## Solution

A new `Events` table with a one-to-many relationship to `Person`. Each event has a type, an optional date and place, and optional notes. The existing date fields on `Person` are **not removed** — this is purely additive.

---

## DB Schema

**New table: `Events`**

| Column | Type | Notes |
|--------|------|-------|
| `Id` | `Guid` | PK |
| `PersonId` | `Guid` | FK → `Persons`, cascade delete |
| `Type` | `EventType` (enum) | See enum values below |
| `Title` | `string(200)` | Optional custom label (e.g. "Arrived at Ellis Island") |
| `Date` | `DateOnly?` | Nullable — not all events have known dates |
| `Place` | `string(300)?` | Nullable — city, country, or free text |
| `Notes` | `string(2000)?` | Nullable — additional context |
| `SortOrder` | `int` | Manual ordering within the same date |

**`EventType` enum (in `FamilyTree.Shared`):**

```
Birth, Death, Marriage, Divorce, BarMitzvah, BatMitzvah,
Immigration, Emigration, Graduation, Military, Other
```

---

## Files to Create / Modify

| File | Change |
|------|--------|
| `src/FamilyTree.Shared/Enums/EventType.cs` | **New** — `EventType` enum |
| `src/FamilyTree.Shared/DTOs/EventDto.cs` | **New** — read model; `PersonId`, `Type`, `Title`, `Date`, `Place`, `Notes` |
| `src/FamilyTree.Shared/DTOs/EventUpsertDto.cs` | **New** — write model for create/update |
| `src/FamilyTree.Core/Entities/Event.cs` | **New** — EF entity; nav property back to `Person` |
| `src/FamilyTree.Core/Data/AppDbContext.cs` | Add `DbSet<Event> Events` |
| `src/FamilyTree.Core/Services/IEventService.cs` | **New** — `GetByPersonAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync` |
| `src/FamilyTree.Core/Services/EventService.cs` | **New** — implementation; returns `ServiceResponse<T>` |
| `src/FamilyTree.Core/Program.cs` | Register `IEventService` → `EventService` |
| EF migration | **New** — `AddEventsTable` |
| `src/FamilyTree.Web/Modules/Pages/` (TBD component) | Add events list + add/edit UI on person detail |

**No changes to:** `Person.cs`, `PersonDto.cs`, `PersonService.cs`, existing migrations.

---

## Implementation Notes

- Follow the `ServiceResponse<T>` pattern used throughout — `ServiceResponse.Ok(data)` / `ServiceResponse.Fail(message)`
- Use `IDbContextFactory<AppDbContext>` for scoped contexts, not a singleton
- Sort events by `Date` ascending (nulls last), then `SortOrder`
- `EventType.Birth` and `EventType.Death` may eventually sync with `Person.BirthDate`/`DeathDate` — leave that as a future migration decision; don't auto-create them on seed
- Display format for dates: if only year is known, store as `new DateOnly(year, 1, 1)` and render as just the year (requires a display flag or convention — TBD)

---

## Verification

1. `dotnet build FamilyTree.sln`
2. `dotnet ef database update` — confirm `Events` table created with correct FK and indexes
3. Add an event via UI → confirm it appears on the person detail
4. Delete a person → confirm their events cascade-delete
