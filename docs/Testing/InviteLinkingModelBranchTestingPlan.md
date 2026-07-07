# Invite + Linking Modal Branch — Manual Testing Plan

Structured checklist covering everything touched in the self-service tree-linking /
family-scoping security session (2026-07-04), roughly in order of risk. `+` marks an
item the user has run, confirmed, and (where needed) corrected.

## Family scoping (biggest, highest-stakes change)

+- Log in as a non-superuser Member (not Doug) — confirm they see only their own family's tree, not Smith/Wilson, not each other's data
+- As that Member, use "Add Person" — confirm the new person gets the correct FamilyId (check via the SQL you've been running), not null and not the wrong family
+- Confirm Doug (superuser) still sees everything across both families
+- Try navigating directly to a Smith/Wilson person's URL while logged in as a Rosenberg/Small-family Member — confirm it's blocked, not just hidden from the list

## Story invite + linking flow (the core feature)

+- Send an invite about someone already on the tree (e.g. Rose Small) — confirm the linking modal never asks a subject-first question about the invite's subject (e.g. no "Is Rose related to anyone on this tree?"), since the subject is already linked. **Note:** if the recipient also already has their own `PersonId` (like Ellen did) and answers "Yes, I'm on this tree," the flow ends there — "How are you related to them?" is a *different* sub-path (see below), not something that's supposed to appear in this case.
+- If the recipient says "No, I'm not on this tree" and instead picks an existing person they know, confirm "How are you related to them?" appears with correct, directional relationship options (e.g. "They're my parent" vs "I'm their parent" — not a single ambiguous "Parent")
+- Try sending an invite without picking anyone — confirm it's blocked with the toast, and "Add someone new…" actually opens /people/add prefilled correctly
+- Full fresh cycle: new invite → recipient writes story → registers → links → confirm hero overlay shows their name, not whoever was previously logged in in that browser. **Must actually complete the linking modal** (don't click "Skip for now") — an unlinked account has no DB-persisted focus and previously fell back to the browser's shared `ft-focus` localStorage key (found 2026-07-06: a new "Test Test" account showed Ellen's tree because that key isn't scoped per-account and still held Ellen's id from earlier testing on the same browser). Fixed: authenticated users never read/write that shared key anymore, DB is their sole source of truth.
+- If you click "Skip for now" (or the linking modal reappears via "Complete your profile" recovery) — confirm the background shows a dedicated **"Let's get you linked to the tree"** empty state with a **"Complete your profile →"** button that reopens the linking modal, not the generic "Choose your starting point → /people" state (found 2026-07-06: the generic state's "Set focus person" button sent an unlinked user to the plain people list with no obvious way to actually focus/link themselves from there — the right action for an unlinked user is always to resume linking, never "browse the people list"). Also confirm only **one** "Choose a person to center your tree" toast appears (was firing twice on every re-render — e.g. after browser back-navigation to `/` — since nothing gated it to fire only once).
+- After linking, sign out and back in — confirm the tree still shows correctly (this is the stale-cookie thing we just hit)
+- Invite someone who **already has an account** (e.g. Ellen, invited to write about Morton) to write a story — after submitting, confirm the conversion screen offers **"Sign in"** (pre-filled email), not "Create a free account" → `/register` (found 2026-07-06: it always redirected to register, which would've failed with "account already exists")

## Layout / rendering

+- Add a third mutually-linked sibling to an existing pair — confirm nobody already-positioned moves
+- Marry an orphan-sibling's child to someone with no other relatives — confirm they land at the same generation, not decades off
+- **FIXED (2026-07-06)** — Add a spouse to someone whose sibling-group is next to another sibling — confirm the neighbor gets pushed right, no overlap (this is the Tom/Bill/Morton case). Initially failed: marrying Bill himself to a new spouse (Gish) sent his *entire* nuclear group (Gish, Bill, Willa, Tom) to the far left of the tree, past Bud+Florence, instead of Morton getting pushed right. Root cause: root-group order came from `CoupleHelper.Derive`'s dictionary insertion order, which is triggered by whichever *child's* shared-parent check runs first — Willa's last name ("Kuh") sorts before "Rosenberg"/"Small," so the couple jumped to the front the instant she gained a second parent. Fixed in `FamilyTreeLayoutEngine.BuildNuclearGroups` by sorting couples by their own identity (position in the alphabetically-ordered people list) instead. Regression test: `MarryingAnOrphanSiblingRoot_DoesNotJumpTheirGroupPastUnrelatedRootsByAlphabeticalAccident`. Please re-verify live in the browser — remarry Bill/Gish (or an equivalent case) and confirm Bud+Florence/Ray+Rose stay put and Morton is the one who moves.
- Check a cross-root couple (like Marc+Ellen) still renders correctly with the new sibling logic in the mix

## Delete/restore

- Delete a person who has relationships, then restore them — confirm the relationships come back too, not just the person
- Delete a person, confirm their relationships don't linger as "ghost" rows still showing up elsewhere

## Refresh button

- Edit/delete something via Admin in one tab, click "Refresh" in another tab showing the tree — confirm it picks up the change without a full page reload

## Pre-auth pages (added after AuthLayout fix)

- Visit `/register`, `/forgot-password`, `/reset-password`, and a story-respond link while signed out — confirm none of them show the search bar / nav icons, only the centered auth card
- Confirm `/about` and `/faq` still show full navigation when visited from inside the app while logged in
