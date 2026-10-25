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

## Step 5: the common-practice baseline (2026-10-23)

The prototype is hand-written at every stage, so its scores only said it beat the archived
engine. To check it against the standard algorithm, `LayeredBaselineEngine` (tests only,
never shipped) lays the same families out with MSAGL, Microsoft's open-source layered
(Sugiyama) layout, which is the same family of algorithm as dagre and ELK. Partners are one
box, as in the prototype, and node sizes come from the prototype, so only placement differs.
It runs twice: with MSAGL's own generation rows, and with MSAGL's X but the birth-year Y.

500 generated families (seed 20261010):

| Score | Archived | Prototype | Baseline, generations | Baseline, birth years |
|---|---|---|---|---|
| Families with a couple over 200 | 164 | 107 | 107 | 107 |
| Max partner gap (mean / worst) | 285 / 1690 | 220 / 1600 | 218 / 1200 | 218 / 1200 |
| Crossings (total / families) | 792 / 183 | **683 / 168** | 1010 / 201 | 1094 / 229 |
| Lines through nodes (total / families) | 674 / 212 | 582 / 205 | **529 / 209** | 958 / 263 |
| Node overlaps (total) | 15 | **0** | **0** | 33 |
| Family off-centre (mean of max) | 263 | **207** | 219 | 219 |
| Width (mean) | 1403 | **1197** | 1472 | 1472 |
| Connector length (mean) | 3275 | **2780** | 3644 | 3403 |

The live tree (30 people, with the anchor):

| Score | Archived | Prototype | Baseline, generations | Baseline, birth years |
|---|---|---|---|---|
| Width x height | 1900 x 988 | **1240** x 994 | 2045 x 1490 | 2045 x 994 |
| Couples over 200 | 1 | 0 | 0 | 0 |
| Crossings | 1 | **0** | 9 | **0** |
| Lines through nodes | **1** | 4 | 3 | 7 |
| Family off-centre, worst | 287 | 274 | **100** | **100** |
| Connector length | 6727 | **5911** | 9154 | 8283 |

What it shows:

- **The prototype meets the common-practice bar.** It beats the standard algorithm on
  crossings, width, connector length and family centring over the generated families, and
  on the live tree. It is behind on lines through nodes (582 vs 529), most of them the
  dashed sibling links already listed as a gap.
- **The birth-year axis is what standard tools don't handle.** MSAGL's placement with
  birth-year Y has 33 overlaps and nearly twice the lines through nodes, because rows that
  were apart in generations collide once moved to their years. The prototype's second X
  phase (blocks in different layers keep apart only when they overlap in height) is the
  custom step that pays for keeping the timeline; it isn't reinventing something standard.
- **The 107 loose couples are the block model, not placement.** Two different X solvers
  give exactly the same count, because both draw three or more partners in a line. Better
  placement can't fix them; a different way of drawing multi-partner people would.
- **Where the baseline is better:** on the live tree it centres every family within 100
  (the prototype has Rebecca's nephews 274 off-centre). Over random families the prototype
  centres better, so this is one case to look at, not a general weakness.

Limits of the comparison: the baseline ignores the anchor and draws orphan siblings as
separate trees (a Sibling link isn't an edge), which makes it wider on the live tree. MSAGL
minimises crossings between boxes, not between drawn connector lines, and runs on its
default settings.

## Lines on top of each other (found 2026-10-23)

Looking at the live tree side by side, Doug saw relationships that aren't in the data: Ray
appeared to have brothers Morry and Isador. In the prototype, the line from their mother
("Unknown Polish Victim" Eisenberg) ran along Ray's line from Louis + Lillian at the same
height, so the two families read as one. None of the scores caught it; crossings and lines
through people don't count lines lying on top of each other. A new score, "lines on top of
another family's", does, and the ratchet now holds it:

| Engine | Live tree | 500 generated families |
|---|---|---|
| Archived (production) | 0 | 721, in 149 families |
| Prototype | 1 (Ray and the Eisenbergs) | 646 |
| Prototype with bracket-shaped sibling links (not merged) | 1 | 827 |

Consequences: the switch to the prototype waits until this is 0 on the live tree, because a
tree that states wrong relationships is worse than a loose one. This is now the first gap to
close, ahead of the list above. The bracket-shaped sibling links (tried the same day; fewer
lines through people, 582 to 309) also raised this score by 28%, so they weren't merged.

**Fixed on the live tree (2026-10-24).** The cause was the side the orphan sibling took.
"Unknown Polish Victim" Eisenberg has no parents on the tree and is glued beside her sister
Rebecca, on Rebecca's end of the Joseph + Rebecca block. `Orient` turned that block so
Rebecca faced her own parent, Gimpel, who is alone in his row and so always counts as the
middle of it. That put Rebecca, and her sister, on the inner side towards Louis + Lillian,
and the sister's sons ended up past Ray. Nothing in the sweeps turns a block against
`Orient`, so the order with no crossings (sister, Rebecca, Joseph, her sons at the edge) was
never tried. `OrderLayers` now tries each block with a glued orphan sibling the other way
round, re-sorts the rows by their parents, and keeps it if the order crosses less.

| | Before | After |
|---|---|---|
| Live tree: lines on top of another family's | 1 | 0 |
| Live tree: worst family off-centre | 274 | 92 |
| 500 generated: lines on top of another family's | 646 | 641 |
| 500 generated: crossings | 683 | 685 |
| 500 generated: connector length (mean) | 2780 | 2784 |

The turn is judged by crossings in row order, not in the drawn layout, so it helps 7 of the
500 generated families and hurts 4 (one from 10 to 19 crossings). Doug accepted the trade
because it removes a relationship the tree stated wrongly; the two ratchet limits were raised
by hand. Judging each turn after `SolveX` is the follow-up (`docs/TodoList.md`).

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

## Trying the prototype on live data (2026-10-24)

The live tree finds problems generated families don't, but switching `Layout__Engine` would
show the prototype, with its known open bugs, to the whole family. So the engine is now
picked per viewer: `LayoutEngineSelector` (scoped) holds a list of named engines, each
marked `SuperUserOnly` or not, and `SelectedLayoutEngine` hands the canvas and SVG export
whichever one the viewer selected. Everyone starts on the site default; the super-user gets
a picker in the toolbar and sees the engine's name there while off the default, so a
screenshot says which engine drew it. The intended loop: fix on a branch, check the live
fixture in the gallery, merge and deploy (invisible to the family), then look on the live
site and bring findings back as fixtures or tests.

Built to grow into a choice for every viewer: clearing an engine's `SuperUserOnly` shows the
picker to everyone. Not done yet: a mobile picker (the toolbar is a mini pill there), and
remembering the choice per account rather than per browser.

## Sibling links as bars (2026-10-24)

On the live tree, through the picker, Doug found the old relatives around Louis unreadable:
his ended marriage to Lillian, Lillian and Libby's sister link and his own links to Tilly
and David were all grey dashed lines, the straight sibling links ran through people, and
Ray and William didn't read as half-brothers. Two changes:

1. **Line styles (both engines, every viewer, PR #72).** An ended marriage has long dashes
   and a 💔; a sibling link is light grey dots. Ray then visibly hangs from a marriage.
2. **Routing (prototype, PR #73).** Each set of siblings with no shared parent hangs from
   one bar over them, drawn as a chain left to right. A set's route is chosen first so no
   piece lies along another line within 10 (the merged-lines score uses 3, which left two
   bars 7 apart, still a double line), then by people passed through, crossings and length;
   a straight line stays when nothing is in its way (Rebecca and her sister). Where another
   line crosses a bar, the bar breaks (a line jump): Louis sits between the two sisters and
   his own siblings are beyond Libby, so one crossing is unavoidable, and without the break
   Louis read as hanging from the sisters' bar. The score still counts a jump as a crossing.

| | Before | After |
|---|---|---|
| Live: lines through people | 4 | 0 |
| Generated: lines through people | 579 | 405 |
| Generated: lines on top of another family's | 641 | 628 |
| Generated: crossings | 685 | 697 |
| Live: crossings | 0 | 1 (the Louis jump) |

The first bracket attempt (2026-10-23) raised merged lines to 827 because it never checked
for lying along a line and drew every recorded pair (Louis to Tilly and Louis to David) as
its own bracket. Doug accepted the crossings: lines through people and lines on top of each
other state relationships that aren't there, crossings only make a tree busier.

## Consequences

- The scores give a baseline to beat. Since 2026-10-23 they are also a ratchet for the
  prototype: `LayoutRatchetTests` fails if any of `TimelineLayoutEngine`'s scores (500
  generated families and every fixture) is worse than its limit in
  `tests/FamilyTree.Web.Tests/LayoutQuality/ratchet.json`. Improvements are tightened with
  `FAMILYTREE_RATCHET_UPDATE=1`, which never loosens; loosening a limit is a hand edit with
  the reason in the commit message. Checked by widening the prototype's node spacing
  (120 to 130): 13 scores failed, while crossings and lines through nodes improved, the
  trade-off this is meant to surface. The archived engine isn't ratcheted; it's frozen.
- The live fixture never enters git. Anyone else working on layout needs their own export.
- Steps 3 and 4 are a real rewrite of placement. The connector-building code and the
  `FamilyTreeLayout` output records can likely stay as they are, so `FamilyTreeCanvas`
  doesn't change.
