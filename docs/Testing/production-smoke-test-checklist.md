# Production Smoke-Test Checklist

Manual pass to run against **https://arborkin.com** after each deploy, and before inviting
new family members. First written 2026-10-11 for the deploy of PR #46 (Leg 3: family-scoping
and auth adversarial review), which put `[Authorize]` on every page by default and changed
what Admins can see and do. Automated tests cover the logic; this pass covers what only a
real browser against real production can show.

Deploy is manual (`deploy-web.yml`), so confirm the deploy actually finished before testing.

## Before you start

- [ ] Deploy workflow finished green and the version you expect is live.
- [ ] A **private/incognito window** for signed-out checks and a **normal window** for
      signed-in checks.
- [ ] Accounts on hand: a regular family member (non-Admin), a family Admin (non-super-user),
      and the super-user.
- [ ] A fresh invite token and a throwaway email address for the registration pass.
- [ ] Know the login limit: `/auth/do-login` allows **5 requests per 15 min per IP**. Seeing
      `?loginError=toomany` is the rate limiter, not a bug. Don't log in repeatedly.
- [ ] Use a throwaway test account or test data for anything that writes. Don't test destructive
      actions on real family records.

## 1. Signed out (incognito)

The riskiest area after PR #46: every page now requires sign-in unless it is listed in
`PageAuthorizationTests.AnonymousRoutes`. A public page missing the exemption would lock out
invitees.

Should load without signing in:
- [x] `/` shows the login overlay and **no tree data**.
- [x] `/register?invite=<token>` loads; complete a full registration with a fresh invite.
      (2026-10-11: invite generated in Admin > Users for a `+t1` address; account created.
      Findings: the success state cut off its button on shorter windows, fixed in `862e6ac`.)
- [ ] `/forgot-password` loads and submits; follow the emailed link to `/reset-password` and
      complete the reset. *(not yet run)*
- [ ] `/story/respond/<token>` loads without login; submit a story. *(not yet run)*
- [x] `/about` and `/faq` load (deliberately public).
- [x] A made-up URL shows the custom 404, not a redirect loop or raw error.

Should **not** be reachable signed out (redirect to login or deny):
- [x] `/people` (redirects to `/?ReturnUrl=%2Fpeople`)
- [x] `/people/add` (redirects to `/?ReturnUrl=...`)
- [x] `/stories` (redirects to `/?ReturnUrl=...`)
- [x] `/dashboard` (redirects to `/?ReturnUrl=...`; from `/faq` it first hit the nested-layout
      crash, fixed in `862e6ac`)
- [x] `/admin` (redirects to `/?ReturnUrl=%2Fadmin`)
- [ ] No person data, story text or media visible on any of the above. *(not explicitly
      confirmed)*

## 2. Signed in as a regular family member (non-Admin)

- [ ] Login works; the tree loads and lands on your saved focus person.
- [ ] Open a person's drawer: pending or hidden stories do **not** appear.
- [ ] You can edit your own story; you cannot edit someone else's.
- [ ] Add a person, edit a person, add a relationship (use test data).
- [ ] Send a story invite; the emailed link works and points to the right place.
- [ ] Photo upload and viewing work.
- [ ] `/admin` is denied.

## 3. Signed in as family Admin (non-super-user)

- [ ] The **Users, Audit log and Activity** tabs are gone (now super-user only).
- [ ] The tabs an Admin should still have are present and work.
- [ ] Restoring a deleted person works (requires Admin).

## 4. Signed in as super-user

- [ ] All Admin tabs are present.
- [ ] Generate a password reset for a test user; the link works.
- [ ] Resetting your own password still works.
- [ ] Messages and Dashboard tabs: note what they show. PR #46 recorded them as unscoped.

## 5. Flows the change might have broken

- [ ] Brand-new invitee goes through the tree-linking modal end to end and ends up on the tree
      with the right family's people.
- [ ] Google sign-in, if configured for production (new account, existing email, returning
      user).
- [ ] Logout then login again lands correctly (no stale user shown).
- [ ] Mobile: repeat sections 1 and 2 on a real phone, not just a resized window.

## Known issues (not regressions)

- The page flashes and loads twice on first load and after login. Tracked in
  `docs/TodoList.md` (prerender bug); PR #46 did not touch it.
- Still open from the Leg 3 review: role or password changes don't end existing sessions (S7);
  blob URLs are public with no SAS (S8); Messages and Dashboard admin tabs unscoped.

## Results

Fill in per run. Copy this block for each deploy.

| | |
|---|---|
| Date | 2026-10-11 |
| Deploy / PR | Production with PR #46 (Leg 3) live; the fixes below are not deployed yet |
| Tester | Doug |
| Browser / device | Desktop Chrome, incognito (signed-out checks) |
| Overall | In progress: section 1 partly done (forgot-password and story-respond not yet run); sections 2-5 not started |

| # | Section / item | What happened | Expected | URL / steps |
|---|---|---|---|---|
| 1 | 1 / protected pages | Signed out on `/faq`, "Go to dashboard" froze the page behind a tall red banner (circuit crash: nested `MainLayout`, duplicate `MudPopoverProvider`) | "Access denied" or login redirect, no error | `/faq` > Go to dashboard. Fixed in `862e6ac` |
| 2 | 1 / error banner | The Blazor error banner rendered as a red slab on the left edge (MudBlazor CSS beat ours) | Small amber toast, top centre | Same repro. Fixed in `862e6ac` |
| 3 | 1 / register | After "Create account" the form stayed on screen and the "Link to your tree" button fell below the fold on shorter windows | Next step visible | `/register?invite=<token>`, shorter window. Fixed in `862e6ac` |
| 4 | 4 / invite | Admin > Users "Generate link" stayed disabled until the email field lost focus | Enabled once a valid email is typed | Admin > Users. Fixed in `862e6ac` |
| 5 | 5 / toasts | "Tip: Drag to explore the tree" toast showed twice | One toast | Sign in as a new user, land on the tree. Fixed in `862e6ac` |
| 6 | Known issue | Page flashes and loads twice on first load | Single paint | Cold load of any page. Prerender turned off, uncommitted; re-check after deploy |
| 7 | 1 / protected pages | Redirect to `/?ReturnUrl=...` works, but `ReturnUrl` is never read, so sign-in drops you on the tree | Decide whether to honour it | Open decision in `docs/TodoList.md` |

For each failure: add a `docs/TodoList.md` entry and a regression test (confirm it fails
against the broken code first), and fix on its own branch.
