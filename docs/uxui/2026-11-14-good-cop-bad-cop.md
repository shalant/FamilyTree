# UX/UI review: good cop / bad cop (2026-11-14)

First pass at a good cop / bad cop review of ArborKin. The repo is public, so this names no family members and includes no screenshots of live data.

## Score and weights

Scored with the shared system in `career-development/docs/UX_UI_SCORING.md` (link, not copied). **The weights below are fixed for this repo; don't change them between re-scores.** The earlier severity list stays as the bad cop; the good cop is a number.

| Area | Pts |
|---|---|
| Invite to first-tree flow | 20 |
| Tree canvas readability | 20 |
| Identity and first impression | 15 |
| Mobile | 15 |
| Empty, error and loading states | 10 |
| Forms (Add/Edit person) | 10 |
| Craft and consistency | 10 |
| **Total** | **100** |

### Good cop, 2026-11-15: **70 / 100, provisional**

| Area | Score | Why |
|---|---|---|
| Invite to first-tree flow | 14 / 20 | One clear action per screen, good expired-invite copy, the dialog fits a phone, the whole journey passes in E2E on desktop and phone. Costs: the toast over the Create account button, the stacked toasts and the one-time hint are fixed in #96 but not live. |
| Tree canvas readability | 15 / 20 | A real birth-year timeline, photo nodes and distinct former marriages read well on desktop. Costs: known layout gaps (the third-son reorder, lines through names), and a phone first view that is zoomed too tight (P7, P8). |
| Identity and first impression | 11 / 15 | One consistent hand: Georgia italic headings, green palette, logo, the family facts banner. The signed-out login card is clean in light mode (one action, calm). Costs: in dark mode the wordmark nearly disappears (D2) and the facts banner is unreadable (D1). |
| Mobile | 10 / 15 | Touch gestures, drawers and stacked forms exist and the invite flow passes at 390px. Provisional: emulation only, no real phone, and the phone tree view was seen once on three people. |
| Empty, error and loading states | 7 / 10 | Friendly error pages and one continuous loading surface. Costs: the Stories empty state (two identical buttons, glass card) was read in code, not seen. |
| Forms (Add/Edit person) | 7 / 10 | The Add person form is clean, sectioned and plainly labelled, and warns instead of blocking on implausible dates. Costs: five relationship pickers before Notes, small labels (F1). Provisional: no photo upload or Edit with real data seen. |
| Craft and consistency | 6 / 10 | Costs: inline-style drift, mixed button colours, Admin content width that changes per tab (A1), always-visible delete icons in the Admin Stories table (A2). |

**Rows sum to 70** (14 + 15 + 11 + 10 + 7 + 7 + 6).

**What was and wasn't viewed.** Live arborkin.com after the 2026-11-15 deploy (#92 to #95, not #96), desktop 1519px, light theme, signed in as the super-user: Dashboard, FAQ, People, Add person, Admin (Dashboard, Data check, Users, Stories). Phone: E2E emulation at 390px touch only, three screens plus the tree. **Second pass, same day:** dark theme on the live site (tree, People, Dashboard, Stories, Add person), and the signed-out login and forgot-password pages from a local build at 1280px and 390px, light and dark. **Still not viewed:** Edit person with data, photo upload, the Audit log, Activity, Deleted and Messages tabs, and anything on a real phone. The rows that depend on those are provisional and could move about 4 points either way.

**Reviewer.** Claude (Sonnet 5.5) reviewed, and the same model fixed several of these findings, so allow about 3 points either way. Read the number as a consistency check, not a measurement.

**Deliberate decisions.** None in this repo's docs bear on UX, so nothing is excluded from the bad cop.

### Bad cop (severity list, no number)

The numbered findings below stay as written. Open after the 2026-11-15 deploy:

- **High:** none open. The three High findings (stale FAQ, dead guest link, dummy Settings) are live-fixed.
- **Medium:** P1, P2, P3 (fixed in #96, not yet merged or deployed); P7 (phone default zoom); the Stories empty-state styling (#8); inline-style drift (#9); blank space under short pages (#10); A1, A2, D1 and D2 below.
- **Low:** P4, P5, P6, P8, #11 to #15, A3, A4, F1, L1 below.

New from the Admin and Add person pass (2026-11-15):

- **A1 (Medium):** the Admin content width changes per tab (Data check is narrow, Users and Stories are wide), so the page's right edge jumps as you switch tabs.
- **A2 (Medium):** in the Stories tab each row has a red trash icon next to the green approve check, always visible: the same loud destructive control removed from People rows. Dates wrap to three lines and "About" and "Submitted by" wrap too.
- **A3 (Low):** Users shows "Last login" as a dash for everyone, which looks broken. Fill it or drop the column.
- **A4 (Low, data hygiene):** the Stories list still holds leftover test stories.
- **F1 (Low):** Add person asks for Identity, Photos, then five relationship pickers (Parents, Spouses/partners, Former partners, Siblings, Children) before Notes, with labels near 12px. A name-only add scrolls past all of it; consider folding relationships behind an "Add relationships" step.
- **D1 (Medium):** in dark mode the facts banner at the top of the tree is unreadable. `[data-theme="dark"] .facts-banner-text` uses `var(--ft-green-200)`, which in the inverted dark palette is `#1e4d33`, a dark green, on a dark green banner: roughly 1.5:1 contrast (measured by hand from the token values, not with a tool). One-line fix: use `--ft-green-800`. It also runs under the tree's top connector ticks.
- **D2 (Medium):** in dark mode the "ArborKin" wordmark on the login card is dark letters on a dark card, so only the leaves and roots show. The logo is a single PNG (`arborkin-logo__transparent.png`) with no dark variant. Needs a light-text version or a light plate behind it.
- **L1 (Low):** a signed-out visitor sees a blurred ghost of the app bar (logo, a green avatar circle) behind the login card. That is the "am I already in?" false signal `AuthLayout` exists to prevent on the other auth pages.
- **Dark mode otherwise holds together:** People, Dashboard, Stories and Add person are consistent and readable. The app bar is a mid-grey band lighter than the page, and the People search box is a filled light-grey field, both a little off, not broken. The theme toggle's first click on the tree page did not take once (unconfirmed, not reproduced).
- **Keep:** the Data check tab explains itself ("Nothing is changed for you: open a person to check and fix them") and offers a Check link per person; the Dashboard is calmer now that only the working export shows.
- **Verified live:** after the deploy the Dashboard "Generations" tile reads 6 (it read 3), and the FAQ is the rewritten one.

### Score log

| Date | Lens | Build scored | Score | What changed or cost points |
|---|---|---|---|---|
| 2026-11-15 | good cop | live arborkin.com, deployed #92 to #95 (not #96) | 70 (provisional) | First score. Unviewed: dark theme, login card, Edit person, photo upload, real phone. Costs: P1 to P3 live until #96, P7, A1, A2, style drift. |
| 2026-11-15 | bad cop | same | 0 High, 9 Medium, 12 Low open (before the dark and login pass) | Severity list above; a number would claim more precision than a flow review has. |
| 2026-11-15 | good cop | same, plus dark theme and the signed-out login (local build) | 70 (provisional) | Unchanged: the clean login card (+1 identity) offsets the dark-mode wordmark and banner (-1). Unviewed now: Edit person, photo upload, four Admin tabs, a real phone. |
| 2026-11-15 | bad cop | same | 0 High, 11 Medium, 13 Low open | Adds D1 and D2 (Medium), L1 (Low). |

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

## Addendum 2026-11-15: invitee journey on a 390px touch phone

**Method:** `InviteeJourneyTests` (story invite → thank-you → register → link to tree → first tree view), run locally against LocalDB with `FAMILYTREE_E2E_SCREENSHOTS` set, desktop and a 390px touch phone. The screenshots are not committed (repo is public; they stay local). All 20 E2E tests pass locally without screenshots. I looked at the phone screenshots for the thank-you page, register, the link-to-tree dialog and the expired-invite page. **I did not see the phone tree view**: see finding P3.

**Good cop**
- The thank-you page has one clear primary action ("Create a free account") and a quiet way out.
- The expired-invite page says why, what to do if the account already exists, and who to ask. It has the right tone for a relative who clicked an old link.
- The link-to-tree dialog fits the phone viewport (the test asserts it), and "Who do you know?" is a plain question, not a form.
- The whole journey completes on a phone; the invited email is pre-filled.

**Bad cop**
- **P1 (Medium): a toast sits on the Create account button.** On the register screen the fixed-bottom toast ("Your memory of … has been saved!") covers the lower half of the one action the page exists for. It clears on its own, but a relative reading the form is looking at it right then. **Fix:** don't show the carried-over "saved" toast on the register page, or show it once on the thank-you page only.
- **P2 (Medium): two stacked toasts over the link-to-tree dialog.** "Account created — let's link you to your tree!" and the earlier "memory saved" toast stack at the bottom and take about a fifth of the phone screen while the user is choosing who they are. The first repeats the dialog's own heading; the second is about the previous page. **Fix:** one toast at most, and none that repeats what is on screen.
- **P3 (Medium): the only touch hint is a one-time toast, and the test that checks it is flaky.** "Tip: Drag to explore the tree. Pinch to zoom." appears once per browser (`ft-treehint` in localStorage) and then disappears. A first-time phone user who looks away misses it for good. The E2E assertion on it (`StoryInvite_ToFirstTreeView(phone)`) failed 2 of 2 runs with screenshots on and passed 2 of 2 without: it races the toast's disappearance. The CI run is not affected today, but it can flake. **Fix:** make the hint persist until the first drag or pinch (or until dismissed), which fixes both the experience and the race. Until then the phone tree screenshot can't be taken by that test.
- **P4 (Low): thank-you subtitle looks low-contrast.** The grey-blue "Want to see the full family tree…" line on white looks below AA. Not measured; measure before changing.
- **P5 (Low): the open dropdown covers "Skip for now / Continue"** in the link dialog while a list is open, as expected for a dropdown, but on a phone the buttons are out of reach until a choice is made.
- **P6 (Low): the long pre-filled email is printed twice** (the Google hint and the field) and wraps awkwardly at 390px.

**Still not covered:** the phone tree view, Add/Edit person, photo upload, and the Admin tabs.
