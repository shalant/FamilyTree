# Share a Memory & Public Profile Pages

**Status:** Planned — Share a Memory depends on Stories table; Public Profile depends on Person Timeline view  
**Goal:** Open the app to two new audiences — family members who want to contribute without admin access, and anyone (recruiters, extended family) who should be able to view a person's story without logging in.

---

## Part 1: Share a Memory

### Problem

Only admins can add stories today. A family member who was invited to view the tree has memories worth capturing but no way to contribute them. Requiring an admin to transcribe memories is a bottleneck that loses stories.

### Solution

A "Share a memory" button on the person detail view, visible to any logged-in user (not just admins). Submissions go into a moderation queue; admins approve before the story is published. Matches the `IsApproved` flag already planned in the Stories schema.

### Flow

1. Any authenticated user clicks **"Share a memory about [Name]"** on the person detail or timeline
2. A simple form collects: Title (required), Body (required), optionally link to an Event (TBD — see stories-table.md)
3. Story is saved with `IsApproved = false` and `AuthorId` = current user
4. Admin sees a **"Pending memories"** badge in the Admin panel
5. Admin reviews and approves or rejects; approved stories appear on the timeline

### Files to Create / Modify

| File | Change |
|------|--------|
| `src/FamilyTree.Web/Modules/Components/ShareMemoryDialog.razor` | **New** — submission form dialog |
| `src/FamilyTree.Core/Services/StoryService.cs` | Add `GetPendingAsync()`, `ApproveAsync()`, `RejectAsync()` |
| `src/FamilyTree.Web/Modules/Pages/Admin.razor` | Add pending memories section with approve/reject actions |
| `src/FamilyTree.Web/Modules/Pages/PersonDetailDrawer.razor` | Add "Share a memory" button (visible to all authenticated users) |

---

## Part 2: Public Read-Only Profile Pages

### Problem

There is no way to share a person's story with someone who doesn't have an account — a distant cousin, a curious neighbor, or a recruiter looking at the portfolio. Every route requires login.

### Solution

A new `/person/{id}/public` route that renders the Person Timeline (name, dates, events, approved stories) without requiring authentication. Visibility is toggled per-person by an admin.

### Design

- **New field on `Person`:** `IsPublic` (`bool`, default `false`)
- Route `/person/{id}/public` is excluded from auth middleware — accessible anonymously
- Renders a read-only `PersonTimeline` with no edit controls
- Admin can toggle `IsPublic` on the person edit form
- If `IsPublic = false` and someone navigates to the URL: show a tasteful "This profile is private" page, not a 404

### Privacy considerations

- Only **approved** stories are shown on public profiles
- `BiographyNotes` is **not** shown on public profiles (it's an internal admin field)
- Living persons (no death date) should default to `IsPublic = false` as a safety guardrail

### Files to Create / Modify

| File | Change |
|------|--------|
| `src/FamilyTree.Shared/DTOs/PersonDto.cs` | Add `IsPublic` field |
| `src/FamilyTree.Core/Entities/Person.cs` | Add `IsPublic` column |
| EF migration | **New** — `AddPersonIsPublic` |
| `src/FamilyTree.Web/Modules/Pages/PublicProfile.razor` | **New** — anonymous-accessible timeline page |
| `src/FamilyTree.Web/Program.cs` | Exclude `/person/{id}/public` from auth policy |
| `src/FamilyTree.Web/Modules/Components/PersonForm.razor` | Add `IsPublic` toggle (admin only) |

---

## Verification

1. `dotnet build FamilyTree.sln`
2. Submit a memory as a non-admin user → confirm it appears in Admin panel as pending
3. Approve the memory → confirm it appears on the timeline
4. Set a person to `IsPublic = true` → confirm `/person/{id}/public` loads without login
5. Navigate to a private person's public URL → confirm "private" page renders (no 404, no data leak)
6. Confirm living persons default to `IsPublic = false` after migration
