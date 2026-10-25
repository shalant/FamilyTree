# Timeline layout engine versions

Added 2026-10-25. `TimelineLayoutEngine` (the ADR 004 prototype) changes often, and a later
version can be worse than an earlier one in ways the scores miss. So each version that's worth
keeping is cut: it gets a number, a git tag, and a row in the history below. The best version
so far also stays live in the app as a frozen copy, so it can be compared with the newest code
on the real tree.

## What's live

The super-user's layout picker (toolbar, desktop) offers three engines. Everyone else sees
only the site default (`Layout:Engine`, archived unless set).

| Option | Key | Code |
|---|---|---|
| Current layout | `Legacy` | `FamilyTreeLayoutEngine`, archived, what the family sees |
| Best so far (vN) | `TimelineBest` | `BestTimelineLayoutEngine`, a frozen copy of the best version cut so far |
| Newest (since vN) | `Timeline` | `TimelineLayoutEngine`, the current code: the last cut plus anything since |

Only these three stay as live code; every other version lives in its git tag. Keeping more
live costs a build and both test suites per engine for little gain.

`BestTimelineLayoutEngine.cs` is never edited by hand; the script replaces it whole. It still
calls shared code that isn't frozen (`FamilyTreeLayoutEngine`'s connector builder and output
records), so change that code only by adding parameters, as `clearLabels` did, or the best
version's drawing changes with it.

## Cutting a version

On a feature branch, once a round of layout work is done:

1. Run the ratchet and the gallery, and look at the new drawings (`layout-quality-gallery.md`).
2. Cut it. PowerShell, from the repo root:
   ```
   ./scripts/cut-layout-version.ps1 -Version v2          # a new version
   ./scripts/cut-layout-version.ps1 -Version v2 -Best    # a new version that beats the best so far
   ```
   It sets `TimelineLayoutEngine.Version`, and with `-Best` copies the engine over
   `BestTimelineLayoutEngine.cs`.
3. Print the history rows and paste the newest one into the table below, filling in the date
   and what changed:
   ```
   dotnet test tests/FamilyTree.Web.Tests --filter "FullyQualifiedName~LayoutVersionHistory" --logger "console;verbosity=detailed"
   ```
   Say in "Better by eye" what this version does better or worse than the best, scores aside.
4. Run the full suite, open the PR. After it's merged, tag the merge commit:
   `git tag layout-v2 <merge sha>` and `git push origin layout-v2`.

To look at an old version: `git worktree add ../ft-v2 layout-v2`, then run the gallery there.
To make an old version the best again, check out its `TimelineLayoutEngine.cs` from the tag
into a branch (`git checkout layout-v2 -- src/FamilyTree.Web/Services/TimelineLayoutEngine.cs`),
run the script with `-Best` and that version's number, then restore the newest code
(`git checkout HEAD -- src/FamilyTree.Web/Services/TimelineLayoutEngine.cs`) and check
`TimelineLayoutEngine.Version` is unchanged.

"Best" is a judgement: scores plus the drawings. The ratchet still only lets the newest code's
scores get better (`ratchet.json`), so a version that wins by eye but loses a score needs a
hand-loosened limit with the reason in the commit message.

## History

Scores are lower-is-better. "Generated" is the 500 fuzz families (seed 20261010): families
with a couple over 200 apart, total crossings, lines through nodes, lines through names, lines
on top of another family's, mean worst family off-centre, mean width. "Fixtures" is the sum
over the checked-in fixtures (the six neutral families plus `two-bridges`): crossings, lines
through nodes or names, overlaps, couples over 200, worst family off-centre.

| Version | Date | Change | Gen: loose | Gen: crossings | Gen: thru nodes | Gen: thru names | Gen: merged | Gen: off-centre | Gen: width | Fix: crossings | Fix: thru lines | Fix: overlaps | Fix: loose | Fix: off-centre | Better by eye |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| v1 (best) | 2026-10-25 | First cut: the prototype as of PR #75 (lines kept out of names), scored on the neutral families in PR #78 | 107 | 659 | 403 | 233 | 683 | 205 | 1197 | 13 | 11 | 0 | 4 | 3815 | Baseline. Weak on many partners, three parents, two families joined twice (`layout-quality-gallery.md`, "Neutral test families") |
