# UX/UI review: good cop / bad cop (2026-11-14)

First pass at a good cop / bad cop review of ArborKin. The repo is public, so this names no family members and includes no screenshots of live data.

## Scope and method

**Looked at, live (arborkin.com, signed in as the super-user, read-only, desktop width):** the tree canvas, `/people`, `/stories`, `/settings`, `/dashboard`.

**Read in code only, not seen rendered:** `LoginOverlay`, `Faq`, `About`, `Home` empty states, `Stories` empty state.

**Not covered:**
- Phone widths. The automation can't shrink the real viewport (see `CLAUDE.md`, "Testing mobile CSS...").
- The invitee journey (invite → `/register` → `LinkToTreeModal` → first tree view), `StoryRespond`, the Add/Edit person form, photo upload, and the Admin tabs. These are the highest-value next pass, because a first-time relative meets them first.
- A logged-out view of the login page. I didn't sign out of the live session.

Severity: **High** (misleads or blocks a new user), **Medium** (rough edge a relative will notice), **Low** (polish).

## Good cop: keep these

- **The tree canvas.** A real birth-year timeline with the year ruler down the left edge, photo nodes, and former marriages drawn differently from current ones. It's the product, and nothing in the template-site tell list applies to it.
- **The family facts banner** ("the youngest family member is …") adds warmth with no extra chrome.
- **Typography and palette.** The italic Georgia headings in dark green against pale green surfaces are consistent across Stories, People, Settings, Dashboard and the empty states. It reads as one hand-made product, not a default Material kit.
- **Stories feed.** Title, "About <person> · by <author> · date", body: simple and readable. Names link to people.
- **People table.** Living / Deceased / All filter, search, and a live/deceased dot beside each name. Born, Birthplace, Died and Age columns give the useful facts at a glance.
- **Dashboard.** The stat tiles, "Recently added", "Request a feature" and "Contact the admin" give a family member somewhere to go with a question, and a working GEDCOM download backs the "you own your data" promise.
- **Error pages** (`NotFoundPage`, `Error`) use one shared `ErrorStateCard`, are friendly, and carry the right status codes.
- **Login error copy.** Each failure has its own specific, kind message (locked, too many attempts, no invite for that Google account).
- **Loading.** The tree's loading surface and splash read as one wait (2026-10-25).

## Bad cop: what a skeptical first-time relative hits

### High

1. **The FAQ describes a product that no longer exists.** `Faq.razor` says:
   - "Authentication is coming soon… you can use it fully without logging in"
   - "Currently only you… can view" the tree
   - account deletion "is coming when authentication launches"
   - exports to GEDCOM, Excel, CSV and PDF are "on the roadmap"

   The app is invite-only with real accounts, and GEDCOM export works on the Dashboard. A relative who opens the FAQ before sending a first story gets told something false about who can see their words. It also says "What is FamilyTree?" while every other surface says ArborKin. The FAQ also makes specific security and retention claims (TLS 1.3, "backups retained for 7 days", East US, "no analytics") that nobody has checked against the current deployment; verify each or soften it. **Fix:** rewrite the FAQ against reality; every claim gets checked or removed.

2. **"Browse public trees without signing in →" on the login card goes nowhere real.** `LoginOverlay` offers it, and `HideIntro` just removes the overlay. There are no public trees, and a guest is scoped to nothing, so they land on the empty-tree state: "Your family tree… Add first person" (`Home.razor`). A stranger sees a call to add people to a tree they can't have. **Fix:** remove the link, since the product is private and invite-only. If a demo is wanted, `TreeDemoOverlay` already exists.

3. **Settings is a page of dummy controls.** `Settings.razor` has three switches ("Show profile photos", "Compact layout", "Show birth/death years") bound to local fields that nothing reads, and two permanently disabled GEDCOM buttons. The GEDCOM export is live on the Dashboard, so Settings contradicts it. A relative who flips "Show profile photos" sees nothing change. **Fix:** hide Settings from the menu until something real is in it, or wire the switches to the canvas.

### Medium

4. **A delete button on every People row.** Bright red trash icons sit next to the edit pencils on all rows, always visible, at a small hit size. One mis-tap while scrolling on a phone sends a person to the deleted list. Restore exists and a confirm dialog presumably guards it, but the destructive control should not be the loudest thing in the table. **Fix:** move delete into the edit page or a row menu, and keep the icon neutral until hovered.

5. ~~**Small, low-contrast type throughout.**~~ **Retracted 2026-11-14.** I judged this from screenshots scaled to 60%. Measured on the live People table: header and cell text are 14px, `#424242`. That is fine. Only the Birthplace cell (13px, `--ft-gray-500`) and the Stories meta line are smaller and paler; worth a contrast check, not a rewrite.

6. **Dashboard: three disabled "Coming soon" rows.** Excel, CSV and PDF export sit dimmed beside the one that works. Half the export card is things you can't do. **Fix:** show only what works; mention the roadmap in the FAQ if at all.

7. **Dashboard "Generations 3" looks wrong for a tree that visibly spans four or five.** `ComputeGenerations` does a breadth-first walk with a `visited` set, so a person reached first by a shallower path keeps the shallower depth; with marriages joining families it undercounts. **Confirmed and fixed 2026-11-14** (`TreeStats.ComputeGenerations` now takes the longest parent chain; a test reproduced the undercount first).

8. **Stories empty state has two identical "Add story" buttons** (header and card) and uses a different card style from the rest of the page (a translucent blurred card, `rgba(245,245,245,0.4)` with `backdrop-filter`) — one of the glassmorphism tells in the baseline. It also uses a hard-coded hex white on a button. **Fix:** one button; use the standard card tokens.

9. **Mixed styling approaches.** Many pages carry inline `style="…"` blocks instead of the design-token classes the Style Guide asks for (`Home.razor` empty states, `Settings`, `Stories`). It shows as small differences in radius, border and button colour between pages (`Color.Success` button on Stories, a token-green button in the empty state, `Color.Primary` on login). **Fix:** one button treatment per intent (primary / secondary / destructive).

10. **Tables and pages end abruptly.** `/people` and `/stories` leave the rest of the viewport blank under short content, and there's no footer or "back to tree" path other than the icon row. Minor on desktop; check on phones.

### Low

11. **Login card:** two "or" paths plus "Forgot password?", "Create an account" and the browse link make five exits from a card meant for one action. With invite-only registration, "New here? Create an account" leads to an error for anyone without a link (`noinvite` / `closed`). Say "Have an invite? Open the link in your email" instead.
12. **Placeholder text on the password field** is ten bullet characters (`••••••••••`), which reads as an already-filled field.
13. **The toolbar's layout picker** ("Newest (since v1)") is super-user only, so family never sees it. Fine; keep it out of everyone else's UI.
14. **`/settings` and `/dashboard` have different title treatments** (Settings: bare "Settings" heading, Dashboard: "Your Family Tree" with subtitle). Align the page headers.
15. **Page titles:** `Stories` and `Settings` say just that; others say "— ArborKin". Make them uniform.

## Suggested order

1. Rewrite the FAQ (1) and remove the guest link (2). Both are small, both are the first thing a new relative reads.
2. Hide or finish Settings (3); trim the Dashboard export card (6).
3. Move the delete control off People rows (4); raise body type size (5).
4. ~~Verify the Generations count (7).~~ Done 2026-11-14.
5. Then do the pass I couldn't do here: the invitee journey on a real phone, Add/Edit person, and the Admin tabs.

Findings 1, 3 and 6 are plainly checkable in the code; 4, 5, 8 and 9 are visible in the screenshots taken; 7 was a hypothesis, since confirmed.
