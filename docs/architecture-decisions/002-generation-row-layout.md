# ADR 002 — Generation-row layout & orthogonal bus connectors (large trees)

**Date:** 2026-06-23
**Status:** Accepted — core implemented 2026-06-23 (refinements pending; see Implementation status)
**Builds on:** [ADR 001 — JS-free family tree layout](001-js-free-tree-layout.md) (positions still computed in C#, absolute placement, SVG connectors share the same coordinates — this ADR does **not** supersede that; it changes *how* the X/Y are derived)

## Implementation status (2026-06-23)

**Done** in `FamilyTreeLayoutEngine.cs` (behind `UseGenerationRows`, default `true`; the
birth-year timeline is preserved behind the same flag set `false`):
- `Y = generation-row` (`RowHeight = 160`); `ComputeDepths` rewritten as a robust
  per-component BFS across all edge types so every person gets a consistent generation.
- The existing orthogonal connectors (flat couple line → stem → sibling bus → drops) now
  render clean because row Y removes the slant/raggedness — no connector rewrite needed.
- Bands relabeled per generation; canvas height derived from row count.
- **Satellite-spouse adjacency**: a childless second/former marriage's isolated partner
  (degree 1) is pulled next to their anchored partner — kills the canvas-spanning line
  (the Florence↔Harvey case).
- **Per-row collision resolution** (coordinate-assignment phase): each generation row is
  swept left-to-right enforcing a minimum center-to-center gap, so overlapping nodes —
  a person's first + second spouse (Mitchell/Toby/Linda), independently-centered
  subtrees — are separated. Couples (≥ `SpouseSpacingX` apart) are untouched.
- Regression tests in `tests/FamilyTree.Web.Tests/FamilyTreeLayoutEngineTests.cs`
  (couples flat, child one row below, satellite adjacent, no intra-row overlap).

**Deferred (refinements, not blockers):**
- Barycenter/median crossing reduction per row — the rightward separation sweep removes
  overlaps but does not minimize edge crossings or re-center parents over shifted children;
  full crossing-optimal coordinate assignment (Brandes–Köpf) is the next refinement.
- Cross-generation marriages still keep their parent-chain row (couple may not be flat in
  that rare case) — the documented least-bad behavior.
- Exposing the timeline as a user-facing toggle (currently a compile-time const).

## Context

The current `FamilyTreeLayoutEngine` places nodes on a **birth-year timeline** (`Y = birthYear × PxPerYear`). This is distinctive and lovely for a small, focused tree, but a real 255-person import (the Israel Herskovitz tree) made its failure mode obvious:

- Spouses born years apart sit at **different heights**, so the marriage line is slanted.
- Siblings each sit at their **own** birth-year height, so the sibling bar is ragged, not a clean horizontal.
- Cross-family marriages get pulled to opposite edges of two parent groups, so one marriage line can span the whole canvas.

The connectors aren't the disease — they inherit messy endpoints from the timeline. **A line is long because the two nodes it joins are far apart, and that is a layout decision.**

Two genuinely separate lifts surround this: (1) importing the raw data well — largely addressed by the import-pipeline work (page-range, entity-resolution matching, JSON hardening); and (2) drawing a large tree in the least-bad way — this ADR.

## Decision

Adopt a **layered (generation-row) layout** with **orthogonal bus connectors** as the model for large trees:

- `Y = generationDepth × RowHeight` — every person in a generation shares one height. `PxPerYear` is retired from the default path (`RowHeight` ≈ 150–180px). The timeline may return later as an **optional mode**, not the default.
- Connectors reduce to **three primitives, no diagonals ever** (below).
- The **focus + N-degrees view filter is mandatory at scale** — even perfect connectors can't make 255 simultaneous nodes legible. Reuse the existing BFS (`WithinDegrees` in `ExportDialog.razor`) on the live canvas.

## Connector design (the easy half)

With rows fixed, three primitives draw the entire tree:

1. **Marriage link** — spouses sit adjacent on the *same* row. Horizontal segment `(xA + r, y) → (xB − r, y)`; ❤ / 💔 badge at midpoint `mx = (xA + xB) / 2`. Former spouses keep the existing dashed-grey style.
2. **Trunk + sibling bus** — one vertical from `(mx, y)` to a horizontal *bus* at `busY = childRowY − busGap`, spanning leftmost-child.x … rightmost-child.x. A single parent drops the trunk from its own node instead of `mx`.
3. **Risers** — one short vertical per child, `(childX, busY) → (childX, childRowY − r)`.

```
xA        mx        xB
[A]───────❤────────[B]      y          marriage link
          │                            trunk
   ┌──────┴───────┐         busY       bus  (leftChild.x … rightChild.x)
   │      │       │                    risers
 [C1]   [C2]    [C3]        childRowY
```

No line ever exceeds one `RowHeight`. N children cost one bus + N short risers, never N long diagonals. `CoupleHelper.Derive()` already yields the couples this routing needs.

## Layout algorithm (the hard half — the real work)

Connectors are trivial *if* positions are right. Two passes produce them:

1. **Generation assignment.** Depth from the eldest ancestors via parent edges, then **force both spouses onto the same row**. The engine already computes a generation-ish depth; the new part is reconciling marriages that span layers (see hard cases).
2. **Per-row ordering / crossing reduction.** Within each row, order nodes to minimise edges crossing to adjacent rows — the barycenter/median heuristic from Sugiyama layered graph drawing. **This is the single biggest "clean vs. spaghetti" lever**, more than the connector style itself. X within a row uses the existing `NodeSpacingX` / `SpouseSpacingX` constants.

## Least-bad cases (where it stays imperfect — by design)

- **Generation conflict across a marriage.** If a spouse married in from a branch one generation "younger," someone must shift to share a row, which can stretch *that couple's own* parent links by a row. Unavoidable in any 2-D genealogy; choose where to absorb it (shift the married-in partner).
- **Cross-family marriages** (the existing cross-root-couple case). Partners belong to different subtrees but must sit adjacent: one is primary in their family, the spouse a satellite beside them; the spouse's own ancestors are either out of the focus view or produce one long crossing edge. This is precisely why focus+degrees scoping is mandatory. Reuse the current cross-root detection as the starting point.
- **Multiple marriages** — flank the person with spouses, one bus per couple.
- **Pedigree collapse** (cousins marrying) — a true cycle yields one unavoidable long edge; handle as a rare exception, optionally a dashed "see also" link rather than a drawn line.

## Consequences

- **Gained:** short, legible, diagonal-free connectors that scale; the standard genealogy reading; crossing reduction makes dense families readable.
- **Lost (from the default view):** birth year is no longer encoded by height — show years in the node label, and/or bring the timeline back as an opt-in mode.
- **Still true from ADR 001:** all positions computed in C#, `position:absolute`, SVG connectors share coordinates, no post-render DOM measurement.
- **Cost:** a layered layout engine (generation assignment + ordering) is real algorithmic work; the connector renderer that consumes it is small.

## Implementation sketch

| Area | File | Change |
|------|------|--------|
| Layout | `FamilyTreeLayoutEngine.cs` | Replace timeline-Y with `Y = genDepth × RowHeight`; add generation assignment + spouse row-alignment |
| Layout | new ordering pass (same file or `TreeOrdering.cs`) | Barycenter/median crossing reduction per row |
| Connectors | `FamilyTreeCanvas.razor` | Replace T-bar SVG with the 3-primitive bus routing (marriage link, trunk+bus, risers) |
| View scoping | `Home.razor` + port `WithinDegrees` out of `ExportDialog.razor` into a shared filter | Draw only N hops from focus on the live canvas |
| Constants | `FamilyTreeLayoutEngine.cs` | `RowHeight`, `busGap`, reuse `NodeSpacingX` / `SpouseSpacingX`; keep `PxPerYear` only for the optional timeline mode |

The cross-root-couple logic and the existing generation depth are a real head start; `CoupleHelper`, `FormerSpouseIds`, and the BFS filter are reused as-is.

## Verification

1. Unit-test generation assignment: a multi-generation fixture asserts spouses share a row and children are exactly one row below their parents.
2. Unit-test the ordering pass on a known crossing case (two couples whose children intermarry) — assert the crossing count drops vs. naive order.
3. Visual: load the Herskovitz tree with focus+degrees = 2–3 and confirm legible buses, no diagonals, no canvas-spanning lines.
4. `dotnet build FamilyTree.sln`; dark/light mode both render; small-tree appearance doesn't regress.
