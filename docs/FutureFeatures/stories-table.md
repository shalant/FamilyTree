# Stories Table

**Status:** Implemented (2026-06-17), with some additions beyond this plan — see below.
**Goal:** Allow multiple attributed prose narratives per person, replacing the single `BiographyNotes` blob with a richer, multi-author storytelling model.

## What actually shipped vs. this plan

- Two extra columns not in the original schema: `IsHidden` (bool) and `SortOrder` (int) — added so admins can take an already-approved story down without re-triggering moderation, and manually reorder the public feed. See `AddStoryManagement` migration.
- Admin UI ended up as a full "Manage stories" table (status chip, approve/hide-show toggle, edit, delete, reorder) rather than just an approval queue — see `story-invite-flow.md`'s admin notes and `Admin.razor`.
- The public-facing `/stories` page (`Stories.razor`) wasn't in this plan at all — it's a family-wide feed of approved + non-hidden stories, with its own "Add story" entry point via `StoryFormDialog`.
- Everything else below (schema, unlinked-story handling, service method shapes) matches what was built.

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
| `PersonId` | `Guid?` | FK → `Persons`, cascade delete — **nullable**, see "Unlinked stories" below |
| `UnlinkedPersonName` | `string(200)?` | Free-text name when `PersonId` is null; cleared once linked |
| `AuthorId` | `Guid` | FK → `AspNetUsers`; set null on user delete |
| `Title` | `string(300)` | Required — e.g. "Her years in Cleveland" |
| `Body` | `string(10000)` | Required — prose narrative |
| `CreatedAt` | `DateTime` | UTC; set on insert |
| `UpdatedAt` | `DateTime` | UTC; updated on save |
| `EventId` | `Guid?` | FK → `Events`, nullable — **TBD: see note below** |
| `IsApproved` | `bool` | For moderation (see Share a Memory feature) |

**Unlinked stories**

A respondent (especially via the invite flow) may write about a family member who isn't in the system yet. Rather than blocking submission, `PersonId` is nullable: when null, `UnlinkedPersonName` holds the free-text name the submitter typed, and the story sits in an admin **"unlinked stories"** queue (same shape as the `IsApproved` moderation queue) until an admin links it to a real `Person` — either an existing one or one they create for the occasion. Linking sets `PersonId` and clears `UnlinkedPersonName`. A story is never both linked and carrying a stale `UnlinkedPersonName`.

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
| `src/FamilyTree.Core/Services/IStoryService.cs` | **New** — `GetByPersonAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`, `GetUnlinkedAsync`, `LinkToPersonAsync` |
| `src/FamilyTree.Core/Services/StoryService.cs` | **New** — implementation |
| `src/FamilyTree.Core/Program.cs` | Register `IStoryService` → `StoryService` |
| EF migration | **New** — `AddStoriesTable` |
| `src/FamilyTree.Web/Modules/Pages/` (TBD component) | Stories list + add/edit UI on person detail |
| `src/FamilyTree.Web/Modules/Pages/Admin.razor` | Surface "unlinked stories" queue alongside pending-approval queue; pick-existing-person or create-new-person action |

**No changes to:** `Person.cs`, `PersonDto.cs` (BiographyNotes stays), `PersonService.cs`.

---

## Implementation Notes

- `AuthorId` should be captured from the current user's claims at write time — do not trust client-submitted author IDs
- `IsApproved` defaults to `true` for admin-created stories; defaults to `false` for stories submitted via the "Share a Memory" flow (see separate feature doc)
- A story can be unapproved AND unlinked at the same time (e.g. a fresh invite-flow submission about someone not yet in the tree) — the two flags are independent
- Display stories sorted by `CreatedAt` descending by default; allow manual reordering later
- The invite email feature (`story-invite-email.md`) currently pulls from `BiographyNotes` — once Stories are implemented, it should prefer the first approved story body instead

---

## Verification

1. `dotnet build FamilyTree.sln`
2. `dotnet ef database update` — confirm `Stories` table with correct FKs
3. Create a story via UI → confirm author and timestamp are recorded correctly
4. Delete a person → confirm cascade delete on their stories
5. Delete a user → confirm `AuthorId` is set to null (not cascade-deleted)
6. Create a story with `PersonId = null` and `UnlinkedPersonName` set → confirm it appears in the admin "unlinked stories" queue
7. Link an unlinked story to a `Person` → confirm `PersonId` is set, `UnlinkedPersonName` is cleared, and it disappears from the unlinked queue
