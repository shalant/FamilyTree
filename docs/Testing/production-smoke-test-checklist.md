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
- [ ] `/` shows the login overlay and **no tree data**.
- [ ] `/register?invite=<token>` loads; complete a full registration with a fresh invite.
- [ ] `/forgot-password` loads and submits; follow the emailed link to `/reset-password` and
      complete the reset.
- [ ] `/story/respond/<token>` loads without login; submit a story.
- [ ] `/about` and `/faq` load (deliberately public).
- [ ] A made-up URL shows the custom 404, not a redirect loop or raw error.

Should **not** be reachable signed out (redirect to login or deny):
- [ ] `/people`
- [ ] `/people/add`
- [ ] `/stories`
- [ ] `/dashboard`
- [ ] `/admin`
- [ ] No person data, story text or media visible on any of the above.

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
| Date | |
| Deploy / PR | |
| Tester | |
| Browser / device | |
| Overall | pass / fail |

| # | Section / item | What happened | Expected | URL / steps |
|---|---|---|---|---|
| | | | | |

For each failure: add a `docs/TodoList.md` entry and a regression test (confirm it fails
against the broken code first), and fix on its own branch.
