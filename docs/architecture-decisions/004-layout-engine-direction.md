# ADR 004 — Direction for the tree layout engine: measure, then replace placement

**Date:** 2026-10-17
**Status:** Accepted 2026-10-17. The archived engine is frozen, step 2 is decided (keep the
birth-year timeline, option 1 below), the step 3 prototype exists behind a switch, and step 4
(both engines through the same tests) is done as of 2026-10-18.

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

## Decision

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

Live export (2026-10-18): with Doug's own person as the anchor, as the site passes it, the
archived engine on `master` (deployed, PR #63 included) reproduces the live screenshot
exactly: Ellen + Marc 680 apart for the reason given under Context, and a sibling of Louis
drawn on top of him. The anchor matters: without it the same tree lays out mirrored, with
Ellen + Marc at 200, so fixtures now carry an optional `anchor`. The prototype (which
ignores the anchor so far) lays the tree out 1400 wide instead of 1900, every couple at
200, worst off-centre 300, no overlaps. Details: `docs/Testing/layout-quality-gallery.md`,
"The live tree".

## Step 2 decision (2026-10-17)

Doug keeps the birth-year timeline. Of the three ways considered to keep it:

1. **Exact timeline, generations used only behind the scenes** (chosen). Y stays the birth
   year. Generation layers decide left-to-right order only, and X is solved so two blocks
   must keep apart only if they share a layer or overlap in height.
2. Generation bands with a birth-year nudge inside each band (fallback if 1 doesn't look
   good).
3. Generation rows spaced by time; individual years shown only as labels.

How generation layers cope with families whose generations don't line up: layers aren't
counted from the top. They're solved from "partners share a layer" and "a child is at least
one layer below each parent", and a family with no recorded parents floats down to just
above its children. Bud + Florence (no parents on the tree) therefore sit one layer above
Marc, whatever depth Ellen's side reaches. A marriage across generations is handled by the
"at least": one side's child sits two layers down. Only a cycle (someone their own ancestor
through a marriage) can't be solved; that cluster's incoming edges are dropped.

## The archived engine (2026-10-17)

`FamilyTreeLayoutEngine` stays in place and stays the production default, but is frozen: no
new placement rules, fixes only if production breaks. Its connector builder and output
records are shared with the new engine. Both implement `IFamilyTreeLayoutEngine`;
`Layout:Engine` picks one (`Legacy`, the default, or `Timeline`). Its tests stay as they are
and keep running.

## Step 3 status: `TimelineLayoutEngine` prototype (2026-10-17)

`src/FamilyTree.Web/Services/TimelineLayoutEngine.cs`, about 650 lines. Generation layers
(partner clusters, longest path, then pulled down toward children, with childless families
placed last), one rigid block per set of partners, DFS order plus barycentre sweeps keeping
the order with the fewest crossings, birth years inferred from relatives, then a two-phase
X solve (layer neighbours only, then also blocks overlapping in height), each phase ending
in a pass that makes every separation constraint hold. Tests:
`TimelineLayoutEngineTests` (exact birth-year Y, every couple at partner spacing,
parentless in-laws float down, children centred, dateless inference, input-order
independence, parent cycles, and no overlaps over 200 generated families).

First comparison (500 generated families, seed 20261010):

| Score | Archived | Prototype |
|---|---|---|
| Families with a couple over 200 apart | 164 | 107 |
| Max partner gap (mean / worst) | 285 / 1690 | 218 / 1200 |
| Crossings (total / families with any) | 792 / 183 | 894 / 202 |
| Lines through nodes (total / families) | 674 / 212 | 672 / 230 |
| Node overlaps (total / families) | 15 / 13 | 0 / 0 |
| Family off-centre, mean of max | 263 | 215 |
| Width, mean | 1403 | 1201 |
| Connector length, mean | 3275 | 2893 |

`two-bridges` (the screenshot reconstruction): width 1620 → 1022, crossings 1 → 0, every
couple at 200, worst family off-centre 1000 → 127.

### Second round (2026-10-18)

- **Anchor:** the anchor's first parent's side goes on the left (the same rule as the
  archived engine), by mirroring the finished order, which keeps every crossing.
- **Orphan siblings** take their sibling's layer and stay beside them in every order tried,
  on the sibling's free side, or the quieter end of a block when the sibling stands between
  two partners. Their marriages then grow outward without moving the sibling's family.
- **Dateless years** are held between parents and children (at least 20 years either side,
  following chains of dateless relatives to the nearest recorded year), and dateless
  partners share one year unless they are also parent and child.
- **Connectors as obstacles, partly:** a family's line from the parents down to their
  children is kept clear of other families.
- **Block turning:** each child pulls their own parents' end of a block, and a partner with
  no other relatives who can't sit beside their spouse stays on the right end.

Scores, 500 generated families: crossings 894 → 683 (archived 792), lines through people
672 → 582 (archived 674), still no overlaps, 107 loose couples. Live tree: 1240 wide
(archived 1900), every couple at 200 (archived: Ellen + Marc 680 apart), no crossings, 4 lines
through people, all dashed sibling links (archived 1), worst off-centre 274 (archived 287).

**Step 4 done:** `TimelineEngineSuiteTests` reruns every fuzz invariant and transition pair
against the prototype. All pass except `Invariant4` on the full domain (a person with three
or more partners; a parent and child who are co-parents), and Kat landing past her uncle Sam.
One test is judged not applicable: the prototype turns a couple so newly linked brothers sit
side by side.

Known gaps, roughly in the order to tackle them:

- **Three or more partners in one block** sit in a line, so two partners who aren't each
  other's spouse can be 400+ apart (the 107 "loose couples"; worst 1600 in random data).
- **Dashed sibling links** (siblings with no shared parent) are straight lines, so one can
  cross a block (Libby and Lillian are sisters, with their husband Louis between them).
  Curving them in the canvas is the likely fix, not placement.
- **Ordering ignores free space:** it minimises crossings only, so a childless uncle isn't
  moved aside to make room for his brother's children (Kat past Sam).
- **Speed** was 41 ms at 100 people, 121 ms at 250, ~500 ms at 500 (archived 9 / 23 / 84 ms,
  Release build) before the second round; not re-measured since.
- **The X solver is coordinate descent**, which can stall when blocks press against each
  other. The VPSC algorithm (merging blocks that touch) is the standard fix if it shows up.

## Collapsing ancestors' siblings (decided 2026-10-18)

The live tree showed a conflict no placement can remove. Ray + Rose's daughter Ellen married
Marc, from Bud + Florence's family. Ray's siblings sit on one side of Ray + Rose and Rose's
siblings on the other, so whichever side Bud + Florence take, a set of siblings ends up
between the two sets of parents. A layout then has to give up one of three things:

1. children directly under their parents, letting the couple drift apart (the archived
   engine: Ellen + Marc 680 apart on the live tree);
2. the couple together, letting a child sit off-centre from their parents (the prototype:
   Ellen about 210 to one side of Ray + Rose);
3. drawing everyone at once.

Most genealogy apps take the third way (from general knowledge of these products, not
checked against their current versions). Ancestry's and FamilySearch's default views show a person's direct
ancestors and their children, and you open an ancestor to see their siblings. MyHeritage
and Geni draw a wider tree but let branches collapse.

Doug's call: collapsing older generations is the least-bad approach. The plan is that
ancestors' siblings (Fannie and Max beside Rose, William beside Ray) collapse by default
into a small "+2" marker beside the ancestor that opens on click. Until that's built, the
prototype uses option 2. The cost is losing the at-a-glance whole-family view the site
draws today. Not started; see `docs/TodoList.md`, "Layout engine direction".

## Consequences

- The scores give a baseline to beat; nothing fails on them yet. Once a replacement
  engine exists, the scores can become regression thresholds.
- The live fixture never enters git. Anyone else working on layout needs their own export.
- Steps 3 and 4 are a real rewrite of placement. The connector-building code and the
  `FamilyTreeLayout` output records can likely stay as they are, so `FamilyTreeCanvas`
  doesn't change.
