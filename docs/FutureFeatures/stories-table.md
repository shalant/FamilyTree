# Stories Table

**Status:** Planned — not yet implemented  
**Goal:** Allow multiple attributed prose narratives per person, replacing the single `BiographyNotes` blob with a richer, multi-author storytelling model.

---

## Problem

`Person.BiographyNotes` is a single 5000-character free-text field. It can only hold one narrative, has no author attribution, no creation timestamp, and no way to distinguish a childhood memory from an obituary excerpt. Family members can't each contribute their own memory of a person.

## Solution

A new `Stories` table with a one-to-many relationship to `Person`. Each story has a title, body, and is attributed to the `AppUser` who wrote it. `BiographyNotes` is **not removed** — it stays as a legacy field and migration path.

---

## DB Schema

**New table: `Stories`**

| Column | Type | Notes |
|--------|------|-------|
| `Id` | `Guid` | PK |
| `PersonId` | `Guid` | FK → `Persons`, cascade delete |
| `AuthorId` | `Guid` | FK → `AspNetUsers`; set null on user delete |
| `Title` | `string(300)` | Required — e.g. "Her years in Cleveland" |
| `Body` | `string(10000)` | Required — prose narrative |
| `CreatedAt` | `DateTime` | UTC; set on insert |
| `UpdatedAt` | `DateTime` | UTC; updated on save |
| `EventId` | `Guid?` | FK → `Events`, nullable — **TBD: see note below** |
| `IsApproved` | `bool` | For moderation (see Share a Memory feature) |

**Relationship to Events — TBD**

Whether a story can be attached to a specific event (e.g. a story *about* the wedding linked to the wedding `Event` row) is undecided. Leaving `EventId` nullable in the schema now keeps the option open without committing to it.

---

## Files to Create / Modify

| File | Change |
|------|--------|
| `src/FamilyTree.Shared/DTOs/StoryDto.cs` | **New** — read model; includes `AuthorDisplayName`, `CreatedAt` |
| `src/FamilyTree.Shared/DTOs/StoryUpsertDto.cs` | **New** — write model |
| `src/FamilyTree.Core/Entities/Story.cs` | **New** — EF entity; nav properties to `Person` and `AppUser` |
| `src/FamilyTree.Core/Data/AppDbContext.cs` | Add `DbSet<Story> Stories` |
| `src/FamilyTree.Core/Services/IStoryService.cs` | **New** — `GetByPersonAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync` |
| `src/FamilyTree.Core/Services/StoryService.cs` | **New** — implementation |
| `src/FamilyTree.Core/Program.cs` | Register `IStoryService` → `StoryService` |
| EF migration | **New** — `AddStoriesTable` |
| `src/FamilyTree.Web/Modules/Pages/` (TBD component) | Stories list + add/edit UI on person detail |

**No changes to:** `Person.cs`, `PersonDto.cs` (BiographyNotes stays), `PersonService.cs`.

---

## Implementation Notes

- `AuthorId` should be captured from the current user's claims at write time — do not trust client-submitted author IDs
- `IsApproved` defaults to `true` for admin-created stories; defaults to `false` for stories submitted via the "Share a Memory" flow (see separate feature doc)
- Display stories sorted by `CreatedAt` descending by default; allow manual reordering later
- The invite email feature (`story-invite-email.md`) currently pulls from `BiographyNotes` — once Stories are implemented, it should prefer the first approved story body instead

---

## Verification

1. `dotnet build FamilyTree.sln`
2. `dotnet ef database update` — confirm `Stories` table with correct FKs
3. Create a story via UI → confirm author and timestamp are recorded correctly
4. Delete a person → confirm cascade delete on their stories
5. Delete a user → confirm `AuthorId` is set to null (not cascade-deleted)
