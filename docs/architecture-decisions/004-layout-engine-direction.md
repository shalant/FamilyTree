# ADR 004 — Direction for the tree layout engine: measure, then replace placement

**Date:** 2026-10-17
**Status:** Proposed (step 1 under way; steps 2-4 need a decision first)

## Context

After the layout-fuzz hardening round (R0-R13, 2026-10-10 to 2026-10-17),
`FamilyTreeLayoutEngine.cs` is about 2,300 lines and every fuzz invariant passes, but the
live tree still doesn't look refined. The screenshot from 2026-10-16 is the example:
Rebecca + Joseph and Florence + Bud sit at a normal partner distance, while Ellen + Marc
are about 830px apart.

Why that specific gap exists:

1. Ellen and Marc are a **cross-root couple** (they come from two families that are each
   already on the tree). Each is placed under their own parents first, then a post-pass
   pulls them together (`Pull cross-root partners together (R6/R12)`). That pass never
   lets a partner move past the outer edge of their own parents. Ellen is already directly
   under Ray, the right end of her parents, and Marc directly under Florence, the left end
   of his, so neither can move.
2. Rose's family (Rebecca + Joseph's children) sits between two families it connects to:
   Rose married Ray, from Louis + Lillian's family on the left, and her daughter Ellen
   married into Bud + Florence's family on the right. Rose can only take one edge of her
   siblings, so Fannie and Max end up between Ellen's line and Marc's. Space is reserved
   per family, regardless of row, so Bud's family can't use the empty space under Fannie
   and Max even though they are drawn about 25 years higher.

The shape of the problem is the engine, not this one couple:

- **Placement is greedy, plus a growing list of special cases.** Root groups are placed
  one by one, each family reserves its full width, and post-passes nudge things (pull
  partners together, turn couples, sort children to an edge, attach second marriages).
  Each R-fix added a rule; the rules now interact (a fix rejected because it moved a
  family 240px; two rules wanting opposite edges for Rose).
- **A family tree with marriages between lineages is a graph, not a tree.** The engine
  uses a tree-style method and treats every cross-family marriage as an exception, and
  those exceptions are the worst-looking parts of the live tree.
- **The Y axis is birth year, not generation.** Rows don't line up, so the engine can't
  pack row by row. `docs/FutureFeatures/bulk-import-deactivated.md` already floats moving
  to generation rows.
- **The tests measure correctness only.** The fuzz invariants catch overlaps, children
  away from their parents and order-dependence. Nothing scores compactness or partner
  distance, so every test can pass while the tree looks loose.

## Decision (proposed)

Stop adding placement rules to the current engine. Keep its test harness, and replace the
placement algorithm, in this order:

1. **Measure looks, not just correctness.** (Started on `feat/layout-quality-metrics`,
   see "Step 1 status" below.)
   - Export the live tree into a test fixture, kept out of git because the repo is public.
   - Quality scores rather than pass/fail checks: partner distance, total width, connector
     crossings, connectors running through nodes, connector length, how far each family's
     children sit from under their parents.
   - A gallery page that renders each fixture (the live tree plus the worst generated
     cases) as SVG, so "does it look refined" can be judged in a minute.
2. **Decide: keep the birth-year timeline, or switch to generation rows.** A product call.
   Generation rows make the standard layered layout usable and remove the birth-year
   inference code (`EnforceGenerationConstraints` and friends). Keeping the timeline is
   possible but makes every layout harder.
3. **Prototype a layered layout in a fixed time box.** The standard method for family
   graphs:
   - each marriage becomes a small family node linking two partners to their children,
     so cross-family marriages are ordinary edges, not special cases;
   - each person gets a row (generation);
   - people within a row are ordered to minimize crossings;
   - horizontal positions are solved for together (partners close, children under
     parents, minimum spacing per row), so compactness is an objective rather than a side
     effect of rules.

   Check Microsoft's MSAGL (MIT-licensed C# graph layout with a layered layout) before
   writing this by hand; check how actively it's maintained first. Being C#, it fits
   ADR 001.
4. **Run both engines side by side** behind one interface. Compare them on the scores and
   in the gallery, and switch only when the new one wins on both.

Keep throughout: the fuzz harness (`FamilyTreeLayoutFuzzTests`), the transition pairs
(`FamilyTreeLayoutTransitionTests`) and the cross-tenant matrix. They're what make a
rewrite safe. Most invariants carry over; a few that encode current engine behavior (such
as a partner never leaving their parents' span) may need rethinking.

Don't:

- fix Ellen + Marc with another rule (it would likely work and break something else);
- refactor the 2,300 lines for readability first (a tidier version of the same approach
  has the same limits).

## Step 1 status

Added 2026-10-17 on `feat/layout-quality-metrics`, all in `tests/FamilyTree.Web.Tests/LayoutQuality/`:

- `LayoutQualityMetrics` computes the scores listed above from a `FamilyTreeLayout`.
- `LayoutFixture` loads a family as Person + Relationship rows (JSON) through
  `PersonMapper` and `CoupleHelper`, the same path the site uses.
  `Fixtures/two-bridges.json` is a checked-in reconstruction of the 2026-10-16 screenshot.
  Live exports go in `Fixtures/private/` (gitignored).
- `scripts/export-layout-fixture.sql` exports one family from the database in that format.
- `LayoutGalleryTests.WriteGallery` (skipped unless `FAMILYTREE_LAYOUT_GALLERY=1`) writes
  `artifacts/layout-gallery/index.html`. How to run it is in
  `docs/Testing/layout-quality-gallery.md`, along with the baseline scores.

First finding: on `master` (with PR #63), `two-bridges` has Ellen + Marc 200 apart, but
Ray sits 1000 units away from under his parents, Louis + Lillian, with Rebecca + Joseph's
family between them. Before PR #63 the same fixture had Ellen + Marc 360 apart. Fixing one
gap moved the cost to another family, which is the pattern this ADR is about.

Still to do for step 1: export the live tree and find out why it shows a much wider gap
than the reconstruction (`docs/TodoList.md`, "Layout engine direction").

## Consequences

- The scores give a baseline to beat; nothing fails on them yet. Once a replacement
  engine exists, the scores can become regression thresholds.
- The live fixture never enters git. Anyone else working on layout needs their own export.
- Steps 3 and 4 are a real rewrite of placement. The connector-building code and the
  `FamilyTreeLayout` output records can likely stay as they are, so `FamilyTreeCanvas`
  doesn't change.
