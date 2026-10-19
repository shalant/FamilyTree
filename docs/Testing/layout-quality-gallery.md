# Layout quality scores and gallery

Added 2026-10-17 as step 1 of [ADR 004](../architecture-decisions/004-layout-engine-direction.md).
The fuzz invariants check that a layout is *correct*. These tools score how it *looks*, and
render it so you can judge it by eye. Code: `tests/FamilyTree.Web.Tests/LayoutQuality/`.

## The scores (`LayoutQualityMetrics`)

All in layout units (canvas pixels at zoom 1). Nothing fails on them yet; they're a baseline.

| Score | What it means |
|---|---|
| Partner gap | Horizontal distance between the two partners of every drawn couple. Over 200 (the engine's `SpouseSpacingX`) counts as a loose couple. |
| Crossings | Connector lines from two different families that cross. Lines that only touch or meet at an end don't count. |
| Lines through nodes | A connector passing through a person it doesn't start or end at. |
| Node overlaps | Two people's circles overlapping. |
| Connector length | Total length of all connector lines. Lower means more compact. |
| Family off-centre | For each set of children sharing the same parents, how far the children's centre sits from the parents' centre. |

## Fixtures

A fixture is a family as Person + Relationship rows in JSON, loaded through `PersonMapper`
and `CoupleHelper` exactly as the site loads it.

- `Fixtures/two-bridges.json` (checked in): a reconstruction of the 2026-10-16 screenshot
  from the people visible in it. "Ex Placeholder" stands in for Joseph's former spouse, whose
  node was off-screen. David, Esther and "TF" are left out because their relationships
  weren't readable. It does **not** reproduce the live layout exactly (see "What it showed").
- `Fixtures/private/` (gitignored): live exports. **The repo is public; never commit one.**

### Exporting the live tree

1. Find your family's id: Azure portal > the SQL database > Query editor,
   `SELECT Id, Name FROM Families;`
2. Run `scripts/export-layout-fixture.sql` against the production database with that id, e.g.

   ```
   sqlcmd -S <server>.database.windows.net -d <database> -G -y 0 -v FamilyId="<family guid>" -i scripts/export-layout-fixture.sql -o tests/FamilyTree.Web.Tests/LayoutQuality/Fixtures/private/live.json
   ```

   Or paste the script into the portal's Query editor (replace `$(FamilyId)` with the id),
   copy the single result cell, and save it as `Fixtures/private/live.json`. Header lines or
   stray text around the JSON are ignored by the loader.
3. Add `"focus": "<your person guid>", "anchor": "<your person guid>"` after the opening `{`.
   Focus sets node sizes; anchor is the signed-in user's own person, which the archived
   engine uses to choose each lineage's side. Without it the layout can be mirrored from
   what you see on the site (added 2026-10-18, after it hid the live Ellen + Marc gap).

The script is read-only and exports only ids, first/last names, birth dates and relationship
type/end date.

## Running

```
# Scores for every fixture, printed in the test output (runs in CI too):
dotnet test tests/FamilyTree.Web.Tests --filter "FullyQualifiedName~LayoutFixtureTests" --logger "console;verbosity=detailed"

# The gallery (local only), PowerShell:
$env:FAMILYTREE_LAYOUT_GALLERY = "1"; dotnet test tests/FamilyTree.Web.Tests --filter "FullyQualifiedName~LayoutGalleryTests" --logger "console;verbosity=detailed"
```

The gallery writes `artifacts/layout-gallery/index.html` (gitignored): every fixture, then
the four worst generated families for each score (judged under the archived engine). Since
2026-10-17 each one is drawn twice, by the archived engine and by the `TimelineLayoutEngine`
prototype, each with its scores and an SVG drawn from the same connectors the canvas draws. Couples wider than 200 are labelled in red; overlapping
people get a red ring. The test output also prints aggregate scores over all generated
families. Options: `FAMILYTREE_LAYOUT_GALLERY_CASES` (default 500) and
`FAMILYTREE_LAYOUT_GALLERY_OUT` (output path); generated families use the fuzz harness's
seed (`FAMILYTREE_FUZZ_SEED`, default 20261010).

## Baseline (2026-10-17, `master` at e1c21bd)

500 generated families, seed 20261010:

| | |
|---|---|
| Families with a loose couple | 164 of 500 |
| Max partner gap | mean 285, worst 1690 |
| Crossings | 792 in total, in 183 families |
| Lines through nodes | 674 in total, in 212 families |
| Node overlaps | 15 in total, in 13 families |
| Family off-centre (max per family) | mean 263 |

## Trying the prototype in the real app

The app picks its engine from `Layout:Engine` (`Legacy` by default, or `Timeline`). Locally:

```
dotnet user-secrets set "Layout:Engine" "Timeline" --project src/FamilyTree.Web
cd src/FamilyTree.Web && dotnet watch
```

Sign in as yourself (the dev sign-in has no family and no linked person, so it shows an
empty tree). `dotnet user-secrets remove "Layout:Engine" --project src/FamilyTree.Web`
switches back. Nothing changes in production unless `Layout__Engine` is set on the App
Service.

## What it showed on day one

`two-bridges` on `master` (with PR #63): Ellen + Marc sit 200 apart, but Ray is 1000 away
from under his parents, Louis + Lillian, and his connector runs across Rebecca + Joseph's
whole family. On the code before PR #63 the same fixture had Ellen + Marc 360 apart and a
worst family off-centre of 760. Closing one gap moved the cost elsewhere, which is the
pattern ADR 004 describes. The live screenshot's gap is wider than either: about 830 screen
pixels, roughly 680 layout units if Rebecca + Joseph (245 screen pixels apart) are at the
standard 200. So the live tree has relatives the reconstruction lacks, and a live export is
needed to see why.

## The live tree (exported 2026-10-18)

30 people, 8 couples, exported through the portal Query editor (replace `'$(FamilyId)'` on
the `DECLARE` line with the family's id first; the portal doesn't substitute sqlcmd
variables and fails with "Conversion failed when converting from a character string to
uniqueidentifier"). The copied result cell loaded without truncation.

Set `"focus"` and `"anchor"` to your own person id. The site passes the signed-in user's
person as the anchor, and the archived engine uses it to choose which lineage goes on which
side; without it this tree laid out mirrored and looked fine.

| Score | Archived, `master` (deployed) | Prototype |
|---|---|---|
| Couples over 200 | 1 (Ellen + Marc, 680) | 0 |
| Width | 1900 | 1400 |
| Family off-centre, worst | 287 (Rose's siblings) | 300 (William) |
| Node overlaps | 1 | 0 |
| Crossings | 1 | 1 |
| Lines through nodes | 1 | 6 |
| Connector length | 6727 | 6709 |

- **Why Ellen + Marc are wide:** the archived engine reproduces the 2026-10-18 live
  screenshot node for node. With the anchor, Rose's family sits between Louis + Lillian's
  and Bud + Florence's; Rose takes the edge next to Ray, so Fannie and Max land between
  Ellen and Marc. Ellen can't move past Ray, nor Marc past Florence, which is the mechanism
  described in ADR 004's Context. PR #63 is deployed (8b18c70, 2026-10-17 03:17 UTC) and
  doesn't change this. (A first pass without the anchor wrongly concluded the gap was
  pre-#63 code.)
- **The overlap:** a sibling of Louis with no parents on the tree is drawn 10 units from
  Louis on the same row, also visible live.
- **The prototype** doesn't use the anchor yet, so its layout is the same either way: Ray 20
  from under Louis + Lillian, Rose beside him, her siblings beside her. Its weak spots are
  William 300 from under Libby + Louis, more lines through people (6), and Rebecca's
  sibling-only relative placed far from her, since a Sibling link with no shared parent
  doesn't pull in the ordering yet.
- `two-bridges.json` has no anchor, so its archived-engine numbers above aren't what Doug
  sees either.
