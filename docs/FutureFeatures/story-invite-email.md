# Story-Driven Invite Email

**Status:** Superseded (2026-06-17) — see `story-invite-flow.md`, which was built instead.
**Goal:** Replace the manual copy-link flow with a one-click beautiful email that tells the story of a family member, pulling distant relatives back in emotionally.

## Why superseded, not implemented as written

This doc proposed featuring an *existing* person's `BiographyNotes` in a generic "join the app" invite email. What actually got built is a different, more direct feature: invite someone to *write* a new story about a person (existing or not-yet-in-the-tree), with a no-login response page, via the new `StoryInvite` token system. It solves the same "1 in 4 invited members registers" problem from a different angle — give them something concrete and personal to do (write one memory) rather than a generic "come join" email. See `story-invite-flow.md` for what shipped.

---

## Problem
1 in 4 invited family members actually registers. The current flow requires the admin to manually copy a generated link and share it however they can — no email is sent by the app.

## Solution
A single "Send invite" button that:
1. Creates the invite token (existing behavior)
2. Optionally features a specific family member's story in the email (admin picks from a dropdown, sorted oldest-first)
3. Sends a beautiful HTML email immediately — no third-party services, uses existing SMTP infrastructure

---

## Email Design
Pure HTML/CSS, inline styles, ~600px wide, mobile-responsive.

**If a featured person is selected:**
- Dark green ArborKin header
- Circular photo (from `ProfilePhotoUrl`) or green initials circle (fallback)
- Name in large type + life dates (`"Born 1921, Cleveland, OH"` / `"1921–2003"`)
- First ~280 chars of `BiographyNotes` as an italic pull-quote
- Hook: *"Someone in your family is building a record of where you all came from."*
- CTA button: `"Open the family tree →"`

**If no featured person:** Hook + CTA only — still elegant.

---

## Files to Create / Modify

| File | Change |
|------|--------|
| `src/FamilyTree.Web/Services/InviteEmailBuilder.cs` | **New** — static class, `Build(PersonDto?, inviteUrl, recipientEmail) → string` |
| `src/FamilyTree.Web/Modules/Pages/Admin.razor` | Add person dropdown, inject `IEmailSender`, send email in `CreateInviteAsync()` |

**No changes to:** `AuthService.cs`, `IAuthService.cs`, `Program.cs`, DB schema.

---

## Implementation Notes
- `IEmailSender` is already registered in DI (`SmtpEmailSender` in prod, `LogEmailSender` in dev)
- Person list for dropdown: call `PersonService.GetAllAsync()` in `OnInitializedAsync`, sort by `BirthDate` ascending (oldest first)
- Dropdown display format: `"Margaret Rose · 1921–2003"` or `"John Smith · 1945"`
- Keep the copy-link UI as a fallback (useful in dev where email logs to console)
- Button label: "Generate link" → "Send invite"

---

## Verification
1. `dotnet build FamilyTree.sln`
2. Dev: check console for `LogEmailSender` HTML output
3. Prod: configure `Email:SmtpHost` via user secrets → confirm email arrives with story and working CTA link
