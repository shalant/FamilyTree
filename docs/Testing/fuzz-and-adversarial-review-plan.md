# Plan: Layout Fuzz Harness, Transition-Bug Hunt, and Scoping/Auth Adversarial Review

Written 2026-10-10, ahead of inviting more family members to arborkin.com. Three legs,
each on its own branch with its own docs update in the same PR.

| # | Branch | Cost profile | Why this order |
|---|---|---|---|
| 1 | `testing/layout-fuzz-harness` | Opus writes it once, then free to run forever | Its invariants also catch bugs in leg 2's new scenarios |
| 2 | `testing/layout-transition-regressions` | Opus | Builds on leg 1's generators |
| 3 | `security/scoping-adversarial-review` | Opus, read-only first | Independent of 1 and 2, can run in parallel |

If only one leg starts, start with leg 3: it bears most directly on whether it is safe to
invite people.

## Leg 1: Layout fuzz harness

**Where:** `tests/FamilyTree.Web.Tests/ServiceTests/FamilyTreeLayoutFuzzTests.cs`, next to
`FamilyTreeLayoutEngineTests.cs`. `ComputeLayout(people, couples, focusId)` is a pure
function, so no infrastructure is needed.

**Approach**
- Add **FsCheck.Xunit** (or CsCheck to avoid the F# dependency). Regenerate
  `packages.lock.json` (lock files are enabled via `Directory.Build.props`).
- Generator builds a random genealogy from a seed: couples, children, remarriages, orphan
  siblings, cross-root marriages, people with no dates. Derive `PersonDto` ID lists the way
  `PersonMapper` does, and reuse `CoupleHelper.Derive` so the test uses the production
  derivation rather than reimplementing it.
- On failure, print the seed and a minimal family dump. Shrinking matters: a 40-person
  failure is not actionable.

**Invariants asserted on every generated tree**
1. No two nodes overlap (center distance >= sum of radii). This is the bug class behind the
   open 2026-09-09 sibling-overlap entry in `docs/TodoList.md`.
2. Every input person appears exactly once in the output.
3. A child's Y is below all of their parents' Y values, including the dateless case.
4. Couples share a Y and sit within `SpouseSpacingX`.
5. Determinism: same input gives same output; shuffling input list order does not change
   relative placement.
6. Stability under an edit: adding one childless leaf must not move unrelated components'
   nodes (the general form of the "classification transition" property).
7. No exception and no NaN/infinite coordinates on any input, including cycles and dangling
   IDs.

**Expect failures.** The first run will probably fail (the 2026-09-09 bug is still open).
Triage each by hand; log real bugs as separate TodoList entries; mark known-open ones with
`[Fact(Skip = "...")]` and a link, or a small allowlist. Never weaken an invariant to get
green. Fix each real bug on its own branch with its own before/after test.

**CI:** fixed case count (about 200) with a pinned seed in `ci.yml`, so it is
deterministic. A separate local-only "soak" test runs thousands of cases with a random seed.

**Done when:** the harness runs in `dotnet test` and its test count appears in the output;
re-breaking the engine (reverting one of the old fixes) makes it fail.

**Status (2026-10-10):** done on `testing/layout-fuzz-harness`. The first run found root
causes R0-R10 (`docs/TodoList.md`, "Layout Fuzz Harness: first-run findings"). **R0 was
critical:** a parent cycle through a person with 3+ parents overflowed the stack and killed
the web process for every family. Fixed 2026-10-11 on `fix/r0-parent-cycle` (write paths
reject cycles, the engine survives existing ones, `Invariant7b` un-skipped, `Invariant7c`
added); not deployed until merged and deployed by hand.

## Leg 2: Transition-bug hunt

Enumerate classification transitions beyond the four documented in CLAUDE.md. Candidates:
- **Adoption.** No `Adopted` handling was found in the layout engine or in what was grepped
  of `PersonMapper`. Confirm what `ChildIds`/`ParentIds` do with an `Adopted` relationship
  first; if the mapper ignores it, that is a product bug, not just a missing test.
- A third spouse; a second marriage ending (`EndDate` set) while a first is still active.
- Death of a spouse, and how the death-date default for `FormerSpouseIds` (commit 073ae02)
  interacts with a remarriage.
- Deleting a middle generation: grandchildren orphaned from their roots, one component
  splitting into two.
- Merging components: a link added between two previously separate trees.
- A sibling link gained or lost after the sibling already has a spouse and children.
- Half-siblings (shared parent, different spouses).
- A person gaining a birth date when relatives have none (changes Y inference).
- A focus person who is removed or sits in a different component.

**Deliverable:** per transition, a before/after test pair in `FamilyTreeLayoutEngineTests.cs`
(same people, exactly one differing edit). Tag `[Trait("Category","Regression")]` only if it
reproduces a real shipped bug. For a transition that exposes a new bug, write the failing
test first and fix on a separate branch. Verify-fails-first still applies: revert the fix,
confirm the test fails, restore.

**Done when:** each new test runs in the suite and each new bug has its own TodoList entry.

## Leg 3: Scoping and auth adversarial review

**Step 1, read-only:** `/code-review xhigh` (user-triggered) scoped to:
- `PersonService`, `RelationshipService`, `StoryService`, `MediumService`, `GedcomExportService`
- `StoryInviteService`, `AuthService` (register, invite, Google callback,
  `LinkUserToTreeAsync`, `CreateUnlinkedPersonAsync`)
- `AppUserClaimsPrincipalFactory`, `ICurrentUserService`, `AdminUserService`, `AdminAuditService`

**Attack checklist for the reviewer**
- Every method taking an ID: is the family check applied on the write path and on
  related-entity lookups (a `MediumService` upload against another family's person, a story
  linked to a foreign `PersonId`, a relationship retargeted by `UpdateAsync`)?
- Different messages for "exists in another family" vs "doesn't exist" (ID enumeration).
- Invite tokens: can one family's token create a user in another family? Reuse or guessing?
  Can `CreateInviteAsync` auto-creating a `Family` put a second family's user in the first?
- Google OAuth new-account path: correct `UserFamily` assignment?
- Anything reading `IsSuperUser` or `FamilyId == null` that falls through to "see
  everything" (the shape of the 2026-07-04 Critical bug).
- Export, search and autocomplete endpoints (common places to forget the filter).
- Blob URLs: guessable or unauthenticated across families?

**Step 2, lock findings in:** a **cross-tenant test matrix** on the existing in-memory EF
infrastructure (`TestInfrastructure.cs`). Seed two families; for every public service
method, call as a family-A user against family-B IDs and assert failure or empty results.
The next new method then gets checked automatically.

**Done when:** each confirmed finding has a failing-then-passing test, a fix PR if needed,
and a line in `docs/security.md` or TodoList.

**Status (2026-10-10):** done on `security/scoping-adversarial-review`. Findings S1-S9 are
in `docs/security.md` ("Scoping and auth adversarial review"). S1 (anonymous page access
and anonymous creates, confirmed in a local run) and S2 (any Admin could take over the
super-user via copyable reset links) were exploitable with a single family. S1-S4 and S6
are fixed, S5 mostly; S7-S9 are TodoList follow-ups. The matrix is
`tests/FamilyTree.Core.Tests/CrossTenantMatrixTests.cs` (96 cases). 57 of its first 90
failed against the original code; failing rows were skipped per fix group and un-skipped
by the fix commit. Every fix ran its rows red first.

## Token accounting

Leg 1 is the first measurement: note tokens used for (a) generator and invariants,
(b) triage of first-run failures. Record the figures here after the leg so legs 2 and 3
can be budgeted from real numbers.

| Leg | Tokens used | Notes |
|---|---|---|
| 1 | ~300k: (a) ~150k generator + invariants, (b) ~150k first-run triage | Measured 2026-10-10 from the session's remaining-token counter, so approximate. (a) includes reading the 1,550-line engine and CsCheck API discovery. (b) covers 10 root causes (R0-R10 in `docs/TodoList.md`), 4 local engine experiments to separate causes, 2 harness bugs found and fixed (leaf variant re-picked the anchor; rejection filter starved), and the re-break check. Docs ~10k on top. |
| 2 | _not yet measured_ | |
| 3 | ~520k: (a) ~330k review (~120k main session + ~210k `/code-review` xhigh subagent), (b) ~180k matrix + 8 fix commits, (c) ~10k docs | Measured 2026-10-10 the same way as leg 1 (remaining-token counter; subagent figure from its own usage report), so approximate. (a) includes a duplicate manual read pass before the run was restarted with fuller instructions (~65k), two local app runs to confirm S1 before and after, and merging 47 agent findings with the manual ones. (b) covers 96 matrix cases, 2 page-auth tests, re-pointing 3 existing test fixtures whose callers had no family (PersonService, MediumService, AuthService linking), and the E2E run (9/9). |
