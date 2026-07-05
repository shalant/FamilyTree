# Bulk Import — Deactivated (2026-07-04)

## Status

Deactivated, not deleted. The `/import` route, `ImportFormPanel`, `ClaudeImportService`,
`ImportsTab`, and the `ImportBatch` model are all still in the codebase — every UI
entry point (AppBar icon + mobile drawer, Dashboard quick action, Admin's "Imports"
tab) has been removed, and `/import` itself renders a "temporarily unavailable"
placeholder instead of the real form.

## Why

This session's work on self-service tree linking (see
`Commentary/UnlinkedInviteProblem.md`) surfaced a pattern: hand-entering a handful of
edge-case people — a sibling with no shared parent, a married sibling whose spouse has
no other recorded relatives, a 3-way sibling cluster, a pre-existing cross-root couple
interacting with a new sibling group — broke the layout engine in a different way each
time. Each was fixable because a human was watching, could screenshot the broken
render, and could describe exactly what looked wrong.

A bulk GEDCOM/CSV import is the same category of problem at much larger scale and with
none of that feedback loop. Real genealogy data routinely contains multiple marriages,
adopted children, unknown/inferred parents, and disconnected branches merged into one
file — all arriving at once, silently, with no per-person checkpoint. A single test
import during this session rendered a "crazy"-looking tree, confirming the risk in
practice, not just in theory.

## Is a good-looking bulk import achievable?

**Not for arbitrary GEDCOM data rendered on the current timeline-based layout** — that
combination is open-ended enough to be a poor long-term investment.

**Yes, for a reasonably clean/moderate import, if the Y-axis stops being a birth-year
timeline.** The birth-year *inference* machinery (interpolating a continuous historical
timeline from partial/missing dates) is what broke three separate times this session —
it's inherently fragile because it's trying to solve a much harder problem than the
tree structure itself requires. If Y becomes pure generational depth (an integer ×
fixed row height, no date math), the layout problem becomes a standard
hierarchical/layered graph layout — closer to how most commercial genealogy software
(and tools like Graphviz's `dot`) actually render trees, and much more tolerant of
sparse/inconsistent imported data.

## Suggested path back (not started)

1. Add a display mode toggle or a hard switch: generational-depth Y-axis instead of
   birth-year timeline (`FamilyTreeLayoutEngine.ComputeBirthYears` → replaced by
   `ComputeDepths`-only positioning, no interpolation).
2. Re-run the existing `FamilyTreeLayoutEngineTests` suite against that mode to confirm
   the same edge cases (orphan siblings, married orphan siblings, sibling clusters,
   cross-root couples) still render correctly without birth-year math involved.
3. Test import specifically with GEDCOM files that deliberately include those edge
   cases at small scale before trusting it with a large real-world export.
4. Re-add the UI entry points (AppBar, Dashboard, Admin tab) once the above holds up.

## Reference

- `Commentary/UnlinkedInviteProblem.md` — the session that motivated this decision
- `tests/FamilyTree.Web.Tests/ServiceTests/FamilyTreeLayoutEngineTests.cs` — regression
  coverage for the layout edge cases that make bulk import risky today
- `docs/FEATURE_PLAN_INVITE_LINKING_MODAL.md` — related scope-boundary decision from
  the same session (one-hop self-service linking)
