# Story Invite Flow

**Status:** Planned — depends on Stories table  
**Goal:** Let any user invite a non-member to tell a story about a specific family member via a beautiful, token-based email — no account required to submit. After submitting, the respondent is offered a frictionless path to join the app.

---

## Problem

The "Share a Memory" flow (see `share-memory-public-profile.md`) requires the contributor to already have an account. Most family members who hold the richest memories — elderly relatives, distant cousins — will never create an account unprompted. Asking them to register before contributing is the exact friction that loses their stories forever.

## Solution

A two-step flow:

1. **Invite** — a user sends a targeted email to a specific person asking them to tell a story about a specific family member. The email contains a time-limited token link to a no-login storytelling interface.
2. **Respond** — the recipient opens a beautiful, minimal writing interface, fills in their memory, and submits. After submission they are offered (not required) to create an account.

Submitted stories feed directly into the existing `Stories` table and the admin approval queue.

---

## DB Schema

**New table: `StoryInvites`**

| Column | Type | Notes |
|--------|------|-------|
| `Id` | `Guid` | PK |
| `Token` | `string(128)` | URL-safe random token; unique index |
| `PersonId` | `Guid` | FK → `Persons` — the person the story is about |
| `InvitedEmail` | `string(256)` | Recipient's email address |
| `InvitedByUserId` | `Guid` | FK → `AspNetUsers` — who sent the invite |
| `CreatedAt` | `DateTime` | UTC |
| `ExpiresAt` | `DateTime` | UTC — token is invalid after this |
| `IsUsed` | `bool` | Set to true when a story is submitted |

**Changes to `Stories` table** (additive — see `stories-table.md`):

| Column | Type | Notes |
|--------|------|-------|
| `InviteId` | `Guid?` | FK → `StoryInvites`, nullable — null for stories composed directly |
| `AuthorName` | `string(200)?` | Free-text name for anonymous submitters; stays as display fallback even after conversion |

`AuthorId` (FK → `AspNetUsers`) is already planned on `Stories`. It is set to null until the submitter converts to a full user, at which point it is populated on the existing `Story` row.

---

## Flow Detail

### Sending the invite
1. User clicks **"Invite someone to share a memory"** on a person's detail view
2. A dialog collects: recipient email, optional personal note
3. App creates a `StoryInvite` row and sends a beautiful HTML email (same SMTP infrastructure as `story-invite-email.md`)
4. Token expiry: **30 days**

### The email
- Beautiful, minimal — ArborKin green header, circular photo or initials of the featured person, their name and life dates
- Subject: *"[InviterName] would love to hear your memory of [PersonName]"*
- Body: warm, personal tone; one CTA button — **"Share your memory →"**
- No mention of creating an account

### The storytelling interface (`/story/respond/{token}`)
- Unauthenticated route — no login required
- Shows the featured person's name, photo, and dates for context
- Two fields: **Title** (optional) and **Your memory** (required, large textarea)
- Submit button: **"Share this memory"**
- On submit: create `Story` row (`IsApproved = false`, `InviteId` set, `AuthorName` from a name field on the form), mark `StoryInvite.IsUsed = true`

### The conversion screen (post-submit)
- Shown immediately after successful submission — same page, no redirect
- Warm confirmation: *"Thank you — your memory of [Name] has been saved."*
- Below: *"Want to see the full family tree and the memories others have shared? Create a free account."*
- Two options: **"Create account"** (links to `/register`) or **"No thanks, I'm done"** (closes/exits)
- Creating an account does NOT re-trigger approval — the story is already submitted

### Token validation
- If token is expired: show a graceful "This link has expired" page with a contact prompt
- If token is already used (`IsUsed = true`): show "This memory has already been submitted — thank you"
- If token is invalid: 404

---

## Files to Create / Modify

| File | Change |
|------|--------|
| `src/FamilyTree.Core/Entities/StoryInvite.cs` | **New** — EF entity |
| `src/FamilyTree.Core/Data/AppDbContext.cs` | Add `DbSet<StoryInvite>` |
| `src/FamilyTree.Core/Entities/Story.cs` | Add `InviteId` and `AuthorName` columns |
| `src/FamilyTree.Core/Services/IStoryInviteService.cs` | **New** — `CreateInviteAsync`, `ValidateTokenAsync`, `SubmitResponseAsync` |
| `src/FamilyTree.Core/Services/StoryInviteService.cs` | **New** — implementation |
| `src/FamilyTree.Core/Program.cs` | Register `IStoryInviteService` |
| EF migration | **New** — `AddStoryInvites` |
| `src/FamilyTree.Web/Services/StoryInviteEmailBuilder.cs` | **New** — builds HTML email (same pattern as planned `InviteEmailBuilder.cs`) |
| `src/FamilyTree.Web/Modules/Pages/StoryRespond.razor` | **New** — unauthenticated storytelling page at `/story/respond/{token}` |
| `src/FamilyTree.Web/Program.cs` | Exclude `/story/respond/{token}` from auth policy |
| `src/FamilyTree.Web/Modules/Pages/Admin.razor` | Surface pending (unapproved) stories in moderation queue |
| `src/FamilyTree.Web/Modules/Components/` (TBD) | "Invite someone" button + dialog on person detail |

**No changes to:** `AuthService.cs`, `IAuthService.cs`, `UserInvites` table, registration flow.

---

## Implementation Notes

- Token generation: `Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)).Replace("+", "-").Replace("/", "_").TrimEnd('=')` — URL-safe, 86 chars
- `StoryInviteService.SubmitResponseAsync` should be idempotent on the token — check `IsUsed` before writing
- The conversion CTA links to `/register` — it does not auto-populate the email or pre-link the story to the new account (keep it simple; admin can manually link later if needed)
- `StoryRespond.razor` should be visually distinct from the main app — full-bleed, minimal nav, focus entirely on the writing experience
- Keep `BiographyNotes` out of the storytelling interface entirely; this page is about the *recipient's* memory, not existing data

---

## Verification

1. `dotnet build FamilyTree.sln`
2. Send a story invite → confirm email arrives with correct person details and working token link
3. Open the token link in an incognito window (no session) → confirm page loads without auth
4. Submit a story → confirm `Story` row created with `IsApproved = false`, `IsUsed = true` on the invite
5. Open the token link again → confirm "already submitted" message
6. Let a token expire → confirm graceful expired page
7. Confirm submitted story appears in Admin moderation queue
