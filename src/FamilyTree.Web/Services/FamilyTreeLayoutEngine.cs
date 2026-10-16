using FamilyTree.Shared.DTOs;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FamilyTree.Web.Services;

/// <summary>
/// Computes complete 2D layout for a family tree visualization.
///
/// HIGH‑LEVEL MODEL
/// ─────────────────────────────────────────────────────────────────────────────
/// The layout engine separates concerns into three conceptual axes:
///
///   • Data axis: people + relationships (parents, children, spouses)
///   • Time axis (Y): birth years → vertical placement
///   • Structure axis (X): connected components + relative depth → horizontal placement
///
/// The goal is a layout that is:
///   • Stable as data changes
///   • Tolerant of missing / partial data
///   • Easy to extend with new relationship types
///   • Easy to reason about when debugging
/// </summary>
public class FamilyTreeLayoutEngine
{
    private readonly ILogger<FamilyTreeLayoutEngine> _logger;

    // Cycles already warned about by this instance (scoped per circuit), so a canvas that
    // re-lays out on every interaction does not repeat the same warning.
    private readonly HashSet<string> _warnedCycles = [];

    // The logger is optional so tests (and anything else) can still say `new FamilyTreeLayoutEngine()`.
    public FamilyTreeLayoutEngine(ILogger<FamilyTreeLayoutEngine>? logger = null) =>
        _logger = logger ?? NullLogger<FamilyTreeLayoutEngine>.Instance;

    // ─────────────────────────────────────────────────────────────────────────
    // LAYOUT CONSTANTS
    // These are the primary knobs for visual tuning. They are intentionally
    // centralized so designers can tweak spacing without touching logic.
    // ─────────────────────────────────────────────────────────────────────────

    private const int NodeSpacingX = 120;   // base horizontal gap between node centers
    private const int PaddingX = 90;        // left/right canvas padding
    private const int PaddingY = 10;        // top/bottom canvas padding

    private const int FocusSize = 80;       // diameter for the focused person
    private const int Gen1Size = 70;        // diameter for immediate relatives (±1 depth)
    private const int DefaultSize = 60;     // diameter for everyone else

    // pixels per year — controls vertical compression of the timeline
    private const double PxPerYear = 6.5;

    // extra spacing for couples so the U‑arc has room to breathe
    //private const int SpouseSpacingX = 160;
    private const int SpouseSpacingX = 200;

    // Gap between two DISCONNECTED components (separate trees entirely, no relationship
    // path between them) on the same canvas — distinct from PaddingX, which is only the
    // canvas-edge margin. Found 2026-07-07: two genuinely unrelated families rendered
    // close enough together (PaddingX's 90px) to read as ambiguously related, especially
    // once test/admin data introduced extra components on the same canvas.
    private const int ComponentGapX = 320;

    // Extra slack reserved specifically at a boundary the cross-root-couple step forces
    // zero-gap-adjacent, so an orphan sibling anchoring off either side afterward (e.g.
    // Morton, next to Ray) has somewhere to actually land instead of tunnelling into the
    // neighboring family. See the forcedAdjacencyAfterIndex comment in ComputeLayout.
    private const int OrphanBufferX = 2 * NodeSpacingX;

    /// <summary>
    /// Entry point: computes a complete layout for all people and relationships.
    ///
    /// This method orchestrates the full pipeline:
    ///   1. Compute relative generational depths
    ///   2. Compute birth years (with inference)
    ///   3. Identify connected components (separate trees)
    ///   4. Position all nodes (X/Y, size, focus)
    ///   5. Build decade bands
    ///   6. Build connectors (couples + parents + siblings)
    ///
    /// NOTE: The public API is intentionally simple. Internally, the engine is
    /// modular so we can evolve the relationship model over time.
    /// </summary>
    public FamilyTreeLayout ComputeLayout(
        List<PersonDto> people,
        List<CoupleDto> couples,
        Guid? focusPersonId,
        Guid? anchorPersonId = null)
    {
        if (!people.Any())
            return FamilyTreeLayout.Empty;

        WarnAboutParentCycles(people);

        // ─────────────────────────────────────────────────────────────────────
        // PHASE 1: Compute generational depths (relative family positions)
        // ─────────────────────────────────────────────────────────────────────
        //
        // Depths are relative, not absolute generations. We pick an arbitrary
        // focus person and walk the graph:
        //
        //   • Parents: depth + 1
        //   • Children: depth - 1
        //   • Spouses: same depth
        //
        // Then we normalize so the oldest ancestor becomes depth 0. This keeps
        // the tree visually stable even as new people are added.
        var depths = ComputeDepths(people);

        var focusDepth = focusPersonId.HasValue &&
                         depths.TryGetValue(focusPersonId.Value, out var fd)
            ? fd
            : 0;

        // ─────────────────────────────────────────────────────────────────────
        // PHASE 2: Identify connected components (separate family trees)
        // ─────────────────────────────────────────────────────────────────────
        //
        // A connected component is a maximal set of people connected through
        // parent/child/spouse relationships. Each component is rendered as an
        // independent tree, laid out side‑by‑side horizontally. Computed before
        // birth years so a component with no known birth dates of its own can be
        // seeded independently — see ComputeBirthYears.
        var components = IdentifyConnectedComponents(people);

        // ─────────────────────────────────────────────────────────────────────
        // PHASE 3: Compute birth years (absolute timeline positions)
        // ─────────────────────────────────────────────────────────────────────
        //
        // We build a robust birth‑year map that:
        //   • Uses explicit BirthDate when present
        //   • Infers parents from children (parent ≈ child - 25)
        //   • Infers children from parents (child ≈ parent + 25)
        //   • Defaults remaining unknowns to the median year
        //
        // This produces a smooth temporal axis even with sparse data.
        var birthYears = ComputeBirthYears(people, components);
        // Snap to decade boundaries so band labels land on round numbers (1910, 1920 …)
        var minYear = (birthYears.Values.Min() / 10) * 10;
        var maxYear = ((birthYears.Values.Max() + 9) / 10) * 10;
        var yearRange = maxYear - minYear + 1;
        var canvasHeight = (int)(yearRange * PxPerYear + PaddingY * 2);

        // ─────────────────────────────────────────────────────────────────────
        // PHASE 4: Nuclear-family bottom-up / top-down X layout
        // ─────────────────────────────────────────────────────────────────────
        //
        // Each couple (or single parent) + their children forms a NuclearGroup.
        // Subtree widths are computed bottom-up so every group knows how much
        // horizontal space its entire sub-tree needs. A single top-down pass
        // then places every node — parents centred over their children, siblings
        // spread evenly under the midpoint of their parents.
        var personById = people.ToDictionary(p => p.Id);
        var nuclearGroups = BuildNuclearGroups(people, couples);
        var nodeMap = new Dictionary<Guid, LayoutNode>();
        var siblingLinks = new List<SiblingLink>();
        var componentDividers = new List<ComponentDivider>();
        var xOffset = PaddingX;
        double? previousComponentEndX = null;

        foreach (var component in components)
        {
            // A lightweight visual boundary between two genuinely disconnected components
            // (no relationship path between them at all) — placed at the midpoint of the
            // gap, rendered as a subtle line so unrelated families never read as ambiguously
            // adjacent even when a canvas happens to show more than one at once.
            if (previousComponentEndX is { } prevEnd)
                componentDividers.Add(new ComponentDivider((prevEnd + xOffset) / 2.0, PaddingY, canvasHeight - PaddingY));

            var compIds = component.Select(p => p.Id).ToHashSet();

            var compGroups = nuclearGroups
                .Where(g =>
                    (g.ParentAId.HasValue && compIds.Contains(g.ParentAId.Value)) ||
                    (g.ParentBId.HasValue && compIds.Contains(g.ParentBId.Value)))
                .ToList();

            var childrenInGroups = compGroups.SelectMany(g => g.ChildIds).ToHashSet();
            var parentsInGroups  = compGroups
                .SelectMany(g => new[] { g.ParentAId, g.ParentBId })
                .Where(id => id.HasValue).Select(id => id!.Value)
                .ToHashSet();

            // Map each person to the one group they head as a parent. A group WITH
            // children is always preferred over one without — the primary group is what
            // anchors this person's actual children during recursive placement, so a
            // childless group (e.g. a second marriage with no kids together) must never
            // win that slot just by sorting earlier; a couple is then preferred over a
            // single-parent group as the final tiebreak.
            //
            // Found 2026-07-07: a person with two recorded spousal relationships (e.g.
            // Florence — an active marriage to Bud with their child Marc, plus a second,
            // childless "Harvey Fleishman" relationship with no EndDate recorded, likely
            // a past marriage missing its end date) had the CHILDLESS one win as her
            // primary group, purely because "Fleishman" sorts alphabetically before
            // "Rosenberg" (the couple-ordering fix from 2026-07-06 sorts by identity, with
            // no concept of which relationship actually has children on the tree). Marc
            // then had nowhere correct to anchor from, and the whole subtree sprawled.
            //
            // Among couples that both have children, the one with the OLDEST child wins
            // (R11, found 2026-10-11). Before, list order decided, so the moment a
            // childless second marriage gained its first child it could take the slot
            // just by sorting first ("Libby" before "Lillian"), and the first family's
            // child Ray was thrown to the right edge. A later half-sibling arriving can't
            // change which group holds the oldest child, so the choice is stable across
            // that edit.
            var primaryGroup = new Dictionary<Guid, NuclearGroup>();
            int OldestChildYear(NuclearGroup g) => g.ChildIds.Count == 0
                ? int.MaxValue
                : g.ChildIds.Min(c => birthYears.GetValueOrDefault(c, int.MaxValue));
            foreach (var g in compGroups
                .OrderByDescending(g => g.ChildIds.Count > 0 ? 1 : 0)
                .ThenByDescending(g => g.ParentBId.HasValue ? 1 : 0)
                .ThenBy(OldestChildYear))
            {
                if (g.ParentAId.HasValue) primaryGroup.TryAdd(g.ParentAId.Value, g);
                if (g.ParentBId.HasValue) primaryGroup.TryAdd(g.ParentBId.Value, g);
            }

            // Root groups: parents that are not children of any group (tree tops). A
            // group only competes for its own independent placement slot if it's the
            // PRIMARY group of EVERY parent it has (not just at least one) — otherwise a
            // person's second, childless relationship (e.g. a widow's second husband,
            // with no kids together) would get its own root slot and SetNode that shared
            // parent there FIRST, locking in their X position via the wrong anchor
            // before their real family (the one with actual children) is ever
            // processed. The children then get positioned relative to an anchor that
            // doesn't match where their parent visually ended up, sending them far from
            // where they belong. Using OR here (survives if EITHER parent treats it as
            // primary) is NOT enough: Harvey Fleishman has no competing group of his
            // own, so this trivially IS his "primary" group even though it is NOT
            // Florence's — the check must fail if ANY parent's real primary group is
            // something else, hence AND across every parent that has one at all. Found
            // 2026-07-07: Harvey (Florence's second, childless husband) claimed
            // Florence's position before Bud+Florence's group (with their real child
            // Marc) was processed, sending Marc's position wildly out of alignment.
            var rootGroups = compGroups.Where(g =>
                (!g.ParentAId.HasValue || !childrenInGroups.Contains(g.ParentAId.Value)) &&
                (!g.ParentBId.HasValue || !childrenInGroups.Contains(g.ParentBId.Value)) &&
                (!g.ParentAId.HasValue || !primaryGroup.TryGetValue(g.ParentAId.Value, out var pgA) || pgA == g) &&
                (!g.ParentBId.HasValue || !primaryGroup.TryGetValue(g.ParentBId.Value, out var pgB) || pgB == g))
                .ToList();

            // Anchor-based lineage-side ordering: sort root groups by which of the
            // anchor person's two parents' "sides" they belong to (paternal vs maternal),
            // BEFORE the cross-root adjacency step below runs. This establishes the base
            // left-to-right column each root family lives in; the cross-root step is a
            // harder correctness constraint (must not tangle a couple's own connector)
            // and needs to be free to make small local adjacency perturbations to that
            // base order afterward — running this the other way round would let the
            // side-sort undo the cross-root adjacency guarantee. A stable sort, so within
            // a side the existing CoupleSortKey-derived order is left untouched. No-ops
            // entirely when anchorPersonId is null or the anchor has fewer than two
            // recorded parents.
            rootGroups = ApplyLineageSideOrder(people, rootGroups, anchorPersonId);

            // Root individuals: people in no group at all (unlinked singletons).
            var rootIndividuals = component.Where(p =>
                !childrenInGroups.Contains(p.Id) &&
                !parentsInGroups.Contains(p.Id))
                .ToList();

            // ── Cross-root couple detection ───────────────────────────────────
            // When a descendant of root group A (at any depth, not just a direct
            // child) marries a descendant of root group B, PlaceGroup would recurse
            // into their NuclearGroup from both sides, writing conflicting X
            // positions (tangle). Fix:
            //   1. Remove the couple from primaryGroup → each partner becomes
            //      a leaf placed under their own parent group.
            //   2. Sort root groups so the two bridged groups are adjacent and
            //      ParentA's group is to the left.
            //   3. Sort children so cross-root partners land at the inner edges,
            //      producing a natural couple connector that spans between families.
            // After all root groups are placed, PlaceGroup is called once more
            // for each cross-root couple to position their children at the midpoint.

            // Map every person to the root group they ultimately descend from, at ANY
            // depth — not just as a direct child of the root's own ChildIds. Built by
            // walking the same primaryGroup-driven chain PlaceGroup itself will later
            // follow. A single-hop "direct child of root" check (the original approach)
            // stops working the moment a bridging couple's group becomes nested under a
            // grandparent instead of being a root itself.
            //
            // Found 2026-07-07: Marc (Bud+Florence's son) married Ellen (Ray+Rose's
            // daughter) — a cross-root marriage the layout engine already knew how to
            // detect and reorder root groups for, as long as Bud+Florence's group was
            // itself a root. Once Dora (Bud's mother, restored from a soft delete) made
            // Bud her recorded child, Bud+Florence's group became nested one level down —
            // Marc is now a GRANDCHILD of a root (Dora), not a direct child, so the old
            // check silently stopped firing. Both Dora's subtree and Ray+Rose's subtree
            // then independently tried to place Marc and Ellen via ordinary recursion,
            // and whichever ran first hijacked the other's position, sprawling Marc away
            // from his real parents.
            var descendantOfRoot = new Dictionary<Guid, NuclearGroup>();
            // Groups on the CURRENT recursion path (R0). Bad data can record a Parent cycle
            // (someone as their own descendant's child); every recursion below follows the
            // primaryGroup chain, so without this guard a cycle reachable from a root recursed
            // until the process died of a StackOverflow, which can't be caught. It is the
            // current PATH, not a global visited set, on purpose: a group legitimately reached
            // twice by different routes (a child with three recorded parents sits under more
            // than one group) must be walked both times exactly as before, so a tree with no
            // cycle lays out identically. Hitting a group already on the path means a cycle;
            // that link is not followed.
            var markPath = new HashSet<GroupKey>();
            void MarkDescendants(NuclearGroup g, NuclearGroup root)
            {
                if (!markPath.Add(GroupKey.Of(g))) return;
                // A partner with no parents on the tree belongs to the family whose group is
                // their primary one (R2 (a), 2026-10-11). Ellen Kuh, with a child by Max
                // Small (Max Sr's son), later married Ellen Small (Florence's daughter);
                // unmarked, that marriage wasn't seen as joining two families, and Florence's
                // side placed Ellen Kuh on the far side from Max, 520px from him. Only
                // parentless partners: anyone with parents gets their family from them, and
                // claiming them here could take them from it, depending on which root is
                // walked first (it broke CrossRootCouple_StillDetectedWhenOneSideIsNested...).
                foreach (var p in new[] { g.ParentAId, g.ParentBId })
                    if (p is { } pid && !childrenInGroups.Contains(pid) && primaryGroup.GetValueOrDefault(pid) == g)
                        descendantOfRoot.TryAdd(pid, root);
                foreach (var cid in g.ChildIds)
                {
                    descendantOfRoot.TryAdd(cid, root);
                    if (primaryGroup.TryGetValue(cid, out var childGroup))
                        MarkDescendants(childGroup, root);
                }
                markPath.Remove(GroupKey.Of(g));
            }
            foreach (var rg in rootGroups)
            {
                if (rg.ParentAId.HasValue) descendantOfRoot.TryAdd(rg.ParentAId.Value, rg);
                if (rg.ParentBId.HasValue) descendantOfRoot.TryAdd(rg.ParentBId.Value, rg);
                MarkDescendants(rg, rg);
            }

            // ── Sibling-linked root groups follow their sibling's family (R13) ──
            // A root group's parents have no parents on the tree, so any SiblingIds they
            // carry are explicit Sibling links. Unmarried, such a person is a root
            // individual and the sibling-anchor pass puts them just right of their
            // sibling. Married, they become a root group and used to take their own
            // CoupleSortKey slot: Bill (Ray's sibling) marrying Gish jumped to the far
            // left ("Bill" before "Ray") and shifted Ray's whole family 320px. Decided
            // 2026-10-12: such a group sorts right AFTER the root group its sibling
            // belongs to, so the marriage moves nobody before it and keeps Bill on the
            // side he already sat on. This is a sort key derived from structure, applied
            // before anything is placed; it doesn't move already-placed nodes, which is
            // what the "never reordered by sibling relationships" rule (see the
            // place-roots loop) is about. Runs before the cross-root step, which may still
            // make its own adjacency moves afterward.
            //
            // siblingFollowers records, by parent pair, each such group's target, so the
            // placement loop keeps a follower in the same run as its target (no orphan
            // slack between them).
            var siblingFollowers = new Dictionary<(Guid?, Guid?), (Guid?, Guid?)>();
            static (Guid?, Guid?) PairKey(NuclearGroup g) =>
                Nullable.Compare(g.ParentAId, g.ParentBId) <= 0 ? (g.ParentAId, g.ParentBId) : (g.ParentBId, g.ParentAId);
            {
                var siblingTarget = new Dictionary<NuclearGroup, NuclearGroup>();
                var listIndex = new Dictionary<Guid, int>();
                for (var i = 0; i < people.Count; i++) listIndex.TryAdd(people[i].Id, i);
                // Who leads when two root groups' parents are each other's siblings (Ray's
                // family and Bill's): the older sibling. A recorded birth date outranks an
                // inferred one (Bill with no date is inferred from Ray, so they'd tie), then
                // the earlier year, then list order. All three are the person's own data, so
                // the choice doesn't change when either of them marries; list order of the
                // GROUPS did (a single parent sorts after couples, a couple by name), which
                // flipped who followed whom the moment Bill married.
                (int, int, int) Seniority(Guid id) => (
                    personById[id].BirthDate is null ? 1 : 0,
                    birthYears.GetValueOrDefault(id, int.MaxValue),
                    listIndex.GetValueOrDefault(id, int.MaxValue));
                foreach (var rg in rootGroups)
                {
                    var link = new[] { rg.ParentAId, rg.ParentBId }
                        .Where(id => id.HasValue)
                        .SelectMany(id => (personById[id!.Value].SiblingIds ?? [])
                            .Select(sid => (Member: id!.Value, Sibling: sid, Root: descendantOfRoot.GetValueOrDefault(sid))))
                        .Where(x => x.Root is not null && x.Root != rg)
                        .OrderBy(x => rootGroups.IndexOf(x.Root!))
                        .FirstOrDefault();
                    var target = link.Root;
                    if (target is null) continue;
                    // The sibling heads the target group too, so that group points back
                    // here: only the younger sibling's group follows.
                    if ((target.ParentAId == link.Sibling || target.ParentBId == link.Sibling) &&
                        Seniority(link.Member).CompareTo(Seniority(link.Sibling)) < 0) continue;
                    // A longer loop would have no order to follow; the group that would
                    // close it keeps its own slot.
                    var t = target;
                    while (t is not null && t != rg) t = siblingTarget.GetValueOrDefault(t);
                    if (t == rg) continue;
                    siblingTarget[rg] = target;
                    siblingFollowers[PairKey(rg)] = PairKey(target);
                }

                if (siblingTarget.Count > 0)
                {
                    var ordered = new List<NuclearGroup>();
                    void Emit(NuclearGroup g)
                    {
                        ordered.Add(g);
                        foreach (var follower in rootGroups.Where(f => siblingTarget.GetValueOrDefault(f) == g))
                            Emit(follower);
                    }
                    foreach (var rg in rootGroups.Where(g => !siblingTarget.ContainsKey(g)))
                        Emit(rg);
                    rootGroups = ordered;
                }
            }

            var crossRootInfo = new List<(NuclearGroup couple, NuclearGroup rootA, NuclearGroup rootB, Guid memberOfA, Guid memberOfB)>();
            // Index (into the FINAL rootGroups list) of a boundary forced zero-gap by
            // step 2 below — consumed by the main placement loop to reserve OrphanBufferX
            // slack there instead of butting the two groups flush against each other.
            var forcedAdjacencyAfterIndex = new HashSet<int>();
            foreach (var g in compGroups)
            {
                if (!g.ParentAId.HasValue || !g.ParentBId.HasValue) continue;
                if (!descendantOfRoot.TryGetValue(g.ParentAId.Value, out var r1)) continue;
                if (!descendantOfRoot.TryGetValue(g.ParentBId.Value, out var r2)) continue;
                if (r1 == r2) continue;

                // Anchor rgA/rgB by their CURRENT position in rootGroups (already sorted
                // stably) — NOT by the married couple's own canonical PersonA/PersonB GUID
                // order. Using the couple's own GUID order means whichever spouse happens
                // to have the lower GUID (utterly arbitrary, and different every time IDs
                // are regenerated) decides which root group anchors the reorder below —
                // which can shove an unrelated third root group to the very front of the
                // tree depending on random GUID comparison. Found 2026-07-06 as a flaky
                // regression test (MarryingAnOrphanSiblingRoot...) once Bill+Gish's group
                // was added into the mix alongside the pre-existing Marc/Ellen cross-root
                // couple: the reorder's outcome depended on whether Marc's or Ellen's GUID
                // happened to be lower, not on any genealogical or rendering-order fact.
                var idx1 = rootGroups.IndexOf(r1);
                var idx2 = rootGroups.IndexOf(r2);
                var aFirst = idx1 <= idx2;
                var rgA = aFirst ? r1 : r2;
                var rgB = aFirst ? r2 : r1;
                var memberOfA = aFirst ? g.ParentAId.Value : g.ParentBId.Value;
                var memberOfB = aFirst ? g.ParentBId.Value : g.ParentAId.Value;
                crossRootInfo.Add((g, rgA, rgB, memberOfA, memberOfB));
            }

            if (crossRootInfo.Count > 0)
            {
                // 1. Remove from primaryGroup so partners are placed as simple leaves.
                foreach (var (cg, _, _, _, _) in crossRootInfo)
                {
                    if (cg.ParentAId.HasValue && primaryGroup.TryGetValue(cg.ParentAId.Value, out var pA) && pA == cg)
                        primaryGroup.Remove(cg.ParentAId.Value);
                    if (cg.ParentBId.HasValue && primaryGroup.TryGetValue(cg.ParentBId.Value, out var pB) && pB == cg)
                        primaryGroup.Remove(cg.ParentBId.Value);
                }

                // 2. Make each cross-root pair adjacent (rgA immediately left of rgB)
                // without disturbing the relative order of any OTHER root group. A
                // global score-based sort here previously could push rgB (or rgA) past
                // unrelated root groups that have nothing to do with this couple — e.g.
                // a newly-added orphan-sibling group (Bill) ending up displaced past its
                // natural position just because an unrelated pre-existing cross-root
                // couple (Marc + Ellen) needed Ray/Rose's group pushed rightward.
                //
                // Record WHICH boundary (by index — stable even though step 3 below
                // replaces the NuclearGroup instances at these indices with reordered
                // copies) is a forced zero-gap join, so the placement loop can reserve a
                // little slack there. Found 2026-07-07 in production: Morton, an orphan
                // sibling of Ray with no group of his own, anchors off Ray and needs room
                // to slot in — but Ray+Rose's own measured width only ever accounts for
                // Ray+Rose themselves, never a future orphan sibling. Normally there's
                // open canvas to expand into; here there wasn't, because this exact
                // boundary was forced flush against Bud+Florence's group for the Marc/
                // Ellen marriage connector, leaving zero slack for Morton to land in.
                foreach (var (_, rgA, rgB, _, _) in crossRootInfo)
                {
                    var idxA = rootGroups.IndexOf(rgA);
                    var idxB = rootGroups.IndexOf(rgB);
                    if (idxA < 0 || idxB < 0) continue;
                    if (idxB == idxA + 1) { forcedAdjacencyAfterIndex.Add(idxB); continue; } // already adjacent, correct order

                    var moving = rootGroups[idxB];
                    var withoutB = rootGroups.Where((_, i) => i != idxB).ToList();
                    idxA = withoutB.IndexOf(rgA);
                    withoutB.Insert(idxA + 1, moving);
                    rootGroups = withoutB;
                    forcedAdjacencyAfterIndex.Add(idxA + 1);
                }

                // 3. Sort children: whoever belongs to the left-positioned root group
                // (rgA) → rightmost (the inner edge, nearest rgB); whoever belongs to
                // rgB → leftmost (its own inner edge, nearest rgA). Keyed by memberOfA/
                // memberOfB (which root group each spouse actually belongs to) rather
                // than the couple's own ParentA/ParentB GUID label, so the edge each
                // child sorts to always matches which SIDE their root group ended up on.
                //
                // This must apply at ANY nesting depth, not just a root's own direct
                // ChildIds — mirrors the same lesson as descendantOfRoot above. Found
                // 2026-07-07: Elliot (Marc's sibling) rendered between Marc and Marc's
                // cross-root spouse Ellen, visually interrupting their marriage connector.
                // Marc is Bud+Florence's son, and Bud+Florence's own group is nested one
                // level under grandmother Dora's root — Dora's own ChildIds is [Bud,
                // Gladys], neither of which is Marc, so a root-only reorder is a silent
                // no-op for him. Fix: walk the same primaryGroup chain MarkDescendants
                // follows, and reorder ChildIds on whichever specific group actually
                // contains a crossEdge member as a direct child, at whatever depth that is.
                var crossEdge = new Dictionary<Guid, bool>(); // true = belongs to rgA (right edge)
                foreach (var (_, _, _, memberOfA, memberOfB) in crossRootInfo)
                {
                    crossEdge[memberOfA] = true;
                    crossEdge[memberOfB] = false;
                }

                var reorderPath = new HashSet<GroupKey>(); // same cycle guard as markPath
                void ReorderCrossEdgeChildren(NuclearGroup g, int? rootIndex)
                {
                    if (!reorderPath.Add(GroupKey.Of(g))) return;
                    if (g.ChildIds.Any(crossEdge.ContainsKey))
                    {
                        var reordered = g with
                        {
                            ChildIds = [.. g.ChildIds.OrderBy(cid =>
                                crossEdge.TryGetValue(cid, out var isA) ? (isA ? 2 : 0) : 1)]
                        };

                        // NuclearGroup is a record (structural equality) — reordering
                        // ChildIds produces a new instance, so every place that still
                        // holds a reference to the OLD instance must be repointed to the
                        // new one before PlaceGroup/MeasureGroup read them.
                        if (rootIndex.HasValue) rootGroups[rootIndex.Value] = reordered;
                        if (g.ParentAId.HasValue && primaryGroup.TryGetValue(g.ParentAId.Value, out var pA) && pA == g)
                            primaryGroup[g.ParentAId.Value] = reordered;
                        if (g.ParentBId.HasValue && primaryGroup.TryGetValue(g.ParentBId.Value, out var pB) && pB == g)
                            primaryGroup[g.ParentBId.Value] = reordered;

                        g = reordered;
                    }

                    foreach (var cid in g.ChildIds)
                        if (primaryGroup.TryGetValue(cid, out var childGroup))
                            ReorderCrossEdgeChildren(childGroup, null);
                    reorderPath.Remove(GroupKey.Of(g));
                }

                for (int i = 0; i < rootGroups.Count; i++)
                    ReorderCrossEdgeChildren(rootGroups[i], i);
            }

            // ── Second marriages placed beside their shared partner ──────────
            // A person whose primary group is a couple can have one more couple group
            // (a second marriage) whose other partner has no parents on the tree and no
            // better group of their own: Louis's second wife Libby, or Florence's second
            // husband Harvey. That group is never a root (it isn't Louis's primary), so
            // before this its partner went through the spouse safety net, which always
            // tried the right side first and could land on the first spouse (R7), and its
            // children were appended at the right edge of the tree (R11). Now the partner
            // sits SpouseSpacingX out on the shared person's free side (ParentA is always
            // drawn on the left, so A's second marriage goes left and B's goes right) and
            // the group's children sit under that pair, with the space reserved in
            // MeasureGroup so nothing else lands there. The slot is reserved even while
            // the second marriage is childless, so its first child arriving moves nobody.
            // At most one per person; a third marriage still goes through the safety net.
            //
            // The shared person's primary group can also be a single-parent group (R2,
            // 2026-10-11): Ann has a son with no other parent recorded and a childless
            // marriage to Lauren. Before, only a couple could carry an attachment, so Lauren
            // went through the safety net and could walk off the left edge of the canvas.
            // A single parent is drawn at the slot's centre and has no partner on either
            // side, so the attachment goes on the right.
            var crossRootCouples = crossRootInfo.Select(c => c.couple).ToHashSet();
            var attachedFor = new Dictionary<Guid, (NuclearGroup Group, Guid? PartnerId)>();
            foreach (var g in compGroups
                .OrderByDescending(g => g.ChildIds.Count > 0 ? 1 : 0)
                .ThenBy(OldestChildYear))
            {
                if (!g.ParentAId.HasValue || !g.ParentBId.HasValue || crossRootCouples.Contains(g)) continue;
                foreach (var (x, s) in new[] { (g.ParentAId.Value, g.ParentBId.Value), (g.ParentBId.Value, g.ParentAId.Value) })
                {
                    if (attachedFor.ContainsKey(x)) continue;
                    if (!primaryGroup.TryGetValue(x, out var px) || px == g) continue;
                    if (primaryGroup.TryGetValue(s, out var ps) && ps != g) continue;
                    if (childrenInGroups.Contains(s)) continue;
                    attachedFor[x] = (g, s);
                }
            }

            // ── Children with no other recorded parent, beside the primary couple (R2) ──
            // A person married with children (Pat and Alex, child Kim) can also have
            // children whose other parent isn't on the tree (Kit and Kat). That
            // single-parent group is never Pat's primary (a couple wins), so it was never
            // a root and nothing placed it: Kit and Kat fell to the safety net at the right
            // edge, ungrouped, past Pat's own siblings, and anyone they married was
            // anchored wherever that left them (found 2026-10-11 by the layout fuzz
            // harness). It now hangs on Pat's free side like a second marriage with no
            // partner node: the children sit outward of the couple's own children, under
            // Pat's side. Runs after the second-marriage loop so a person who already has
            // an attached second marriage keeps it; their single-parent children still go
            // through the old path.
            foreach (var g in compGroups.Where(g => !g.ParentBId.HasValue && g.ChildIds.Count > 0))
            {
                if (g.ParentAId is not { } x || attachedFor.ContainsKey(x)) continue;
                if (!primaryGroup.TryGetValue(x, out var px) || px == g || !px.ParentBId.HasValue) continue;
                attachedFor[x] = (g, null);
            }

            // The second marriage (or single-parent group) hanging off `parent`'s side of g,
            // if g is that person's primary group (only the primary group carries it).
            (NuclearGroup Group, Guid? PartnerId)? AttachmentOf(NuclearGroup g, Guid? parent) =>
                parent is { } p && primaryGroup.TryGetValue(p, out var pg) && pg == g &&
                attachedFor.TryGetValue(p, out var att) ? att : null;

            // ── Turn a couple so its cross-root partner faces the other family (R12) ──
            // PlaceGroupBody always draws ParentA on the left, and which partner is A comes
            // from CoupleHelper's GUID order. When one of them is also in a cross-root
            // couple (Pat, married to Alex, has a child with Blair from another root
            // family), half the time the other spouse (Alex) ended up between Pat and
            // Blair, 460-660px apart. Step 3 above already sorts a cross-root member's
            // slot to the inner edge of their parents' children; this does the same for
            // the side they sit on inside their own couple: rgA's member goes right, rgB's
            // left. Skipped when that person has an attached second marriage (it hangs on
            // their free side, which would then face the other family) and when two
            // cross-root couples want opposite sides of the same person (first one wins).
            //
            // Not done for a sibling-linked follower group (R13): turning it would flip a
            // married couple the moment a Sibling link is added to one of them
            // (SiblingLinkBetweenTwoMarriedParents_BothFamiliesKeepTheirShape).
            if (crossRootInfo.Count > 0)
            {
                var wantRight = new Dictionary<Guid, bool>();
                foreach (var (_, _, _, memberOfA, memberOfB) in crossRootInfo)
                {
                    wantRight.TryAdd(memberOfA, true);
                    wantRight.TryAdd(memberOfB, false);
                }
                foreach (var (person, right) in wantRight)
                {
                    if (attachedFor.ContainsKey(person)) continue;
                    if (!primaryGroup.TryGetValue(person, out var g) || !g.ParentBId.HasValue) continue;
                    var isRight = g.ParentBId == person;
                    if (isRight == right) continue;
                    var partner = isRight ? g.ParentAId!.Value : g.ParentBId.Value;
                    if (wantRight.TryGetValue(partner, out var partnerRight) && partnerRight == right) continue;
                    if (attachedFor.ContainsKey(partner)) continue;

                    Turn(g);
                }

                // The same for an attachment (R2, 2026-10-11): it hangs on its holder's side
                // of the couple, outward of the couple's own children. If one of those
                // children is a cross-root partner sorted to that same edge, the attached
                // family sits between them and their partner's family and blocks the pull
                // below (Ellen's son Bill, by no recorded father, landed between her daughter
                // Rose and Rose's husband from the next family). Turn the couple so the
                // attachment hangs on the other side. Skipped if both parents hold an
                // attachment or the children want both edges.
                foreach (var g in primaryGroup.Values.Distinct().Where(g => g.ParentBId.HasValue).ToList())
                {
                    var holdsA = AttachmentOf(g, g.ParentAId) is not null;
                    var holdsB = AttachmentOf(g, g.ParentBId) is not null;
                    if (holdsA == holdsB) continue;
                    var edges = g.ChildIds.Where(wantRight.ContainsKey).Select(c => wantRight[c]).Distinct().ToList();
                    if (edges.Count != 1) continue;
                    // ParentA's attachment hangs left, ParentB's right; turn if a child wants that edge.
                    if (edges[0] == holdsB) Turn(g);
                }
            }

            void Turn(NuclearGroup g)
            {
                var turned = g with { ParentAId = g.ParentBId, ParentBId = g.ParentAId };
                var rootIndex = rootGroups.IndexOf(g);
                if (rootIndex >= 0) rootGroups[rootIndex] = turned;
                foreach (var key in primaryGroup.Where(kv => kv.Value == g).Select(kv => kv.Key).ToList())
                    primaryGroup[key] = turned;
            }

            // ── Memoised bottom-up width measurement ─────────────────────────
            var widthCache = new Dictionary<NuclearGroup, double>();
            // Per group with an attached second marriage: the extra width each attachment
            // adds left and right of the core (couple + children). MeasureGroup returns the
            // total; PlaceGroupBody shifts the core off-centre by the difference.
            var sideExt = new Dictionary<NuclearGroup, (double Left, double Right)>();

            var measurePath = new HashSet<GroupKey>(); // same cycle guard as markPath
            double ChildrenWidth(List<Guid> childIds) => childIds.Sum(cid =>
                primaryGroup.TryGetValue(cid, out var cg)
                    ? MeasureGroup(cg)
                    : (double)NodeSpacingX);

            // Where each parent of g sits relative to the core anchor, and which way an
            // attachment of theirs extends: a couple's ParentA is drawn left and ParentB
            // right; a single parent sits at the anchor and attaches on the right.
            (Guid? Parent, double Offset, int Dir)[] AttachSides(NuclearGroup g) => g.ParentBId.HasValue
                ? [(g.ParentAId, SpouseSpacingX / 2.0, -1), (g.ParentBId, SpouseSpacingX / 2.0, 1)]
                : [(g.ParentAId, 0, 1)];

            // Centre of an attached group's children, as an offset from the core anchor
            // (sign added by the caller): under the second couple's midpoint (or under the
            // shared person, for a single-parent group), pushed outward only if that would
            // overlap the core group's own children. sharedOffset is how far the shared
            // person sits from the anchor.
            double AttachedChildrenOffset(double coreChildrenW, double attachedW, double sharedOffset, bool hasPartner) =>
                Math.Max(sharedOffset + (hasPartner ? SpouseSpacingX / 2.0 : 0), coreChildrenW / 2.0 + attachedW / 2.0);

            double MeasureGroup(NuclearGroup g)
            {
                if (widthCache.TryGetValue(g, out var cached)) return cached;
                // A cycle: this group is already being measured further up. Count the link
                // that closed the loop as a plain leaf, the same width PlaceGroup gives a
                // child with no group of its own.
                if (!measurePath.Add(GroupKey.Of(g))) return NodeSpacingX;

                double selfW = g.ParentBId.HasValue
                    ? NodeSpacingX + SpouseSpacingX
                    : NodeSpacingX;

                double childrenW = g.ChildIds.Count == 0 ? 0 : ChildrenWidth(g.ChildIds);

                var w = g.ChildIds.Count == 0 ? selfW : Math.Max(selfW, childrenW);

                // How far each attachment reaches past the core's own edge: the partner's
                // node (SpouseSpacingX beyond the shared person, plus half a slot) or the
                // outer edge of its children, whichever is further.
                double left = 0, right = 0;
                foreach (var (parent, sharedOffset, dir) in AttachSides(g))
                {
                    if (AttachmentOf(g, parent) is not { } att) continue;
                    var hasPartner = att.PartnerId.HasValue;
                    var attW = att.Group.ChildIds.Count == 0 ? 0 : ChildrenWidth(att.Group.ChildIds);
                    var reach = hasPartner ? sharedOffset + SpouseSpacingX + NodeSpacingX / 2.0 : 0;
                    if (attW > 0) reach = Math.Max(reach, AttachedChildrenOffset(childrenW, attW, sharedOffset, hasPartner) + attW / 2.0);
                    var ext = Math.Max(0, reach - w / 2.0);
                    if (dir < 0) left = ext; else right = ext;
                }
                if (left > 0 || right > 0)
                {
                    sideExt[g] = (left, right);
                    w += left + right;
                }

                measurePath.Remove(GroupKey.Of(g));
                widthCache[g] = w;
                return w;
            }

            // ── Node creation ─────────────────────────────────────────────────
            int YOf(Guid id) => PaddingY + (int)((birthYears.GetValueOrDefault(id, minYear) - minYear) * PxPerYear);
            int SizeOf(Guid id)
            {
                var relDepth = depths.GetValueOrDefault(id, 0) - focusDepth;
                return id == focusPersonId ? FocusSize
                     : Math.Abs(relDepth) <= 1 ? Gen1Size
                     : DefaultSize;
            }

            void SetNode(Guid id, double x)
            {
                if (nodeMap.ContainsKey(id)) return; // cross-root couples: don't overwrite
                nodeMap[id] = new LayoutNode(personById[id], (int)Math.Round(x), YOf(id),
                    depths.GetValueOrDefault(id, 0), id == focusPersonId, SizeOf(id));
            }

            // Reserved [left, right] interval between every couple's own two spouse-nodes,
            // recorded as each couple is placed (any depth, not just roots) — this space
            // belongs to their connector/children-midpoint, not to anyone else. Read by
            // FindOpenXAdjacentTo below: an orphan-sibling search anchored off a person
            // outside a couple can otherwise walk (via collision-driven stepping) straight
            // into the visually "safe" territory between that couple's own two nodes,
            // since nothing previously distinguished "empty because unclaimed" from "empty
            // because it's the marriage gap." Found live 2026-09-18 in production: Max,
            // sibling-linked to both Rose (of the Ray+Rose couple) and Fannie (herself
            // anchored just outside that couple's measured bounds, off her own sibling
            // Ray), ends up anchored off Fannie — his rightmost already-placed sibling —
            // and walks leftward through Ray's position and Rose's position, landing
            // squarely between the two spouses since that gap had no other occupant.
            var coupleGaps = new List<(double Left, double Right)>();

            // ── Top-down placement ────────────────────────────────────────────
            var placePath = new HashSet<GroupKey>(); // same cycle guard as markPath
            void PlaceGroup(NuclearGroup g, double anchorX)
            {
                // A cycle: this group is already being placed further up, so its parents have
                // a node already. Don't descend again.
                if (!placePath.Add(GroupKey.Of(g))) return;
                try
                {
                    PlaceGroupBody(g, anchorX);
                }
                finally
                {
                    placePath.Remove(GroupKey.Of(g));
                }
            }

            // anchorX is the centre of the whole slot MeasureGroup(g) reserved. With an
            // attached second marriage on one side the core couple sits off-centre in it.
            void PlaceGroupBody(NuclearGroup g, double anchorX)
            {
                MeasureGroup(g);
                if (sideExt.TryGetValue(g, out var ext))
                    anchorX += (ext.Left - ext.Right) / 2.0;

                if (g.ParentAId.HasValue && g.ParentBId.HasValue)
                {
                    var leftX  = anchorX - SpouseSpacingX / 2.0;
                    var rightX = anchorX + SpouseSpacingX / 2.0;
                    SetNode(g.ParentAId.Value, leftX);
                    SetNode(g.ParentBId.Value, rightX);
                    coupleGaps.Add((leftX, rightX));
                }
                else if (g.ParentAId.HasValue)
                {
                    SetNode(g.ParentAId.Value, anchorX);
                }

                var childrenW = g.ChildIds.Count == 0 ? 0 : ChildrenWidth(g.ChildIds);
                foreach (var (parent, sharedOffset, dir) in AttachSides(g))
                {
                    if (AttachmentOf(g, parent) is not { } att) continue;
                    var sharedX = anchorX + dir * sharedOffset;
                    if (att.PartnerId is { } partnerId)
                    {
                        var partnerX = sharedX + dir * SpouseSpacingX;
                        SetNode(partnerId, partnerX);
                        coupleGaps.Add((Math.Min(sharedX, partnerX), Math.Max(sharedX, partnerX)));
                    }
                    if (att.Group.ChildIds.Count == 0) continue;
                    var attW = ChildrenWidth(att.Group.ChildIds);
                    PlaceChildren(att.Group.ChildIds,
                        anchorX + dir * AttachedChildrenOffset(childrenW, attW, sharedOffset, att.PartnerId.HasValue));
                }

                if (g.ChildIds.Count == 0) return;
                PlaceChildren(g.ChildIds, anchorX);
            }

            void PlaceChildren(List<Guid> childIds, double centerX)
            {
                var cWidths = childIds
                    .Select(cid => primaryGroup.TryGetValue(cid, out var cg)
                        ? MeasureGroup(cg)
                        : (double)NodeSpacingX)
                    .ToList();

                double totalW = cWidths.Sum();
                double startX = centerX - totalW / 2.0;

                for (int i = 0; i < childIds.Count; i++)
                {
                    var childId    = childIds[i];
                    double childAnchor = startX + cWidths[i] / 2.0;

                    if (primaryGroup.TryGetValue(childId, out var childGroup))
                        PlaceGroup(childGroup, childAnchor);
                    else
                        SetNode(childId, childAnchor);

                    startX += cWidths[i];
                }
            }

            // ── Place roots left to right ─────────────────────────────────────
            // Deliberately NOT reordered by sibling relationships. An earlier version
            // tried to reorder/cluster root groups so a newly-added sibling (e.g. "Bill"
            // or "Morton") would render immediately adjacent to their sibling — but that
            // meant every new addition could reshuffle X positions of people who were
            // already placed, and it broke down as soon as one of the siblings was
            // married (their spouse is an atomic part of the same couple unit, so a
            // sibling connector routed to them visually passes through/near the spouse).
            // Simpler and more stable: roots are placed in their natural order and never
            // move once placed; a new sibling just lands wherever it naturally falls.
            // The dashed sibling connector below still shows the relationship even when
            // the two ends aren't adjacent.
            double curX = xOffset;


            // Recorded so the orphan-individual anchor logic below (sibling/spouse
            // fallback placement) can tell when it's about to wander into a DIFFERENT
            // root group's span — see FindOpenXAdjacentTo.
            var rootGroupExtents = new List<(double Left, double Right)>();

            var prevInRun = false;
            for (int rgi = 0; rgi < rootGroups.Count; rgi++)
            {
                var rg = rootGroups[rgi];
                // The orphan slack goes AFTER a forced-adjacent run of families, not
                // between the two families the cross-root couple joins (R6, 2026-10-12).
                // Between them it kept every cross-root couple 240px further apart than
                // their slots allowed (~360px or more, past SpouseSpacingX). Making it
                // conditional on an orphan sibling existing was tried and rejected: an
                // orphan sibling marrying (no longer a root individual) then removed the
                // slack and shifted the right-hand family 240px. After the run, the
                // orphan still has room beside the family they anchor off, and nothing
                // moves when they marry.
                // A sibling-linked follower (R13) of the family just placed, or of the same
                // family as the follower just placed, stays in that run.
                var inRun = forcedAdjacencyAfterIndex.Contains(rgi) ||
                    (rgi > 0 && prevInRun && siblingFollowers.TryGetValue(PairKey(rg), out var follows) &&
                     (follows == PairKey(rootGroups[rgi - 1]) ||
                      (siblingFollowers.TryGetValue(PairKey(rootGroups[rgi - 1]), out var prevFollows) &&
                       prevFollows == follows)));
                if (prevInRun && !inRun)
                    curX += OrphanBufferX;
                prevInRun = inRun;

                var w = MeasureGroup(rg);
                var left = curX;
                PlaceGroup(rg, curX + w / 2.0);
                curX += w;
                rootGroupExtents.Add((left, curX));
            }

            // Finds an open X slot adjacent to `anchor` for an orphan individual (a
            // sibling or spouse with no group of their own). "Safe" territory is the
            // UNION of every root-group extent that contains ANY of this person's
            // already-placed relatives — not just the one nearest `anchor` — because an
            // orphan can have relatives split across two different root groups (e.g.
            // Morton's siblings Ray and Bill anchor to two separate, adjacent couples).
            // Walks outward from `anchor` in both directions, refusing to enter any
            // OTHER root group's span or to wander past the safe union's own outer edge
            // (which would otherwise walk clean off the canvas looking for empty space).
            // Falls back to the original unbounded walk only if a person has no
            // group-affiliated relatives at all to anchor safe territory to.
            //
            // Found 2026-07-07 in production: Morton (an orphan sibling of Ray and Bill,
            // no group of his own) anchored off Ray and walked rightward straight into
            // Bud+Florence's root group — forced zero-gap-adjacent to Ray+Rose's group by
            // the cross-root couple step (Marc married Ellen, Ray+Rose's daughter). A
            // first attempt that treated "any different root group" as foreign wrongly
            // blocked the leftward retreat too, since Bill's own group (Gish+Bill) is
            // ALSO a different root group — even though Bill is Morton's actual sibling —
            // so it fell through to the same unbounded walk and landed in the same wrong
            // spot. Fixed by unioning safe territory across ALL of a person's relatives,
            // and (see OrphanBufferX below) reserving actual slack at forced-adjacency
            // boundaries so there's genuinely somewhere to land.
            //
            // Occupancy is judged at the placed person's OWN row, against every node whose
            // circle would come near theirs (R4, 2026-10-15). It used to look only at nodes
            // on exactly the anchor's Y, so a third spouse born a few years after their
            // partner landed on top of a second spouse a row or two below (Casey on Blair,
            // ThirdMarriage_NewSpouseLandsBesidePartnerAndNobodyElseMoves).
            //
            // Both directions are searched and the nearer open slot wins, the right one on
            // a tie (R7, 2026-10-15). Trying the right first and taking whatever it found
            // put a partner past someone already on that side even with the left side free.
            double FindOpenXAdjacentTo(Guid placedId, LayoutNode anchor, IReadOnlyList<LayoutNode> relatives)
            {
                var (myY, mySize) = (YOf(placedId), SizeOf(placedId));
                bool Occupied(double x) => nodeMap.Values.Any(n =>
                    Math.Abs(n.X - x) < NodeSpacingX && Math.Abs(n.Y - myY) < (n.Size + mySize) / 2.0 + 10);

                var safeExtents = rootGroupExtents
                    .Where(e => relatives.Any(r => r.X >= e.Left && r.X <= e.Right))
                    .ToList();

                if (safeExtents.Count == 0)
                    return Nearer(UnboundedExtend(NodeSpacingX), UnboundedExtend(-NodeSpacingX));

                double UnboundedExtend(double step)
                {
                    var x = anchor.X + step;
                    while (Occupied(x)) x += step;
                    return x;
                }

                double Nearer(double right, double left) =>
                    Math.Abs(left - anchor.X) < Math.Abs(right - anchor.X) ? left : right;

                var safeMin = safeExtents.Min(e => e.Left);
                var safeMax = safeExtents.Max(e => e.Right);

                bool IsOutOfBounds(double x) =>
                    rootGroupExtents.Any(e => !safeExtents.Contains(e) && x >= e.Left && x <= e.Right) ||
                    x < safeMin - NodeSpacingX || x > safeMax + NodeSpacingX;

                // Strictly between a couple's own two spouse-nodes — reserved for their
                // connector/children-midpoint, never a legitimate landing spot for someone
                // else. Treated the same as a node collision below (skip past it, don't
                // stop there) rather than as out-of-bounds (don't give up the whole
                // direction just because the gap is in the way).
                bool InCoupleGap(double x) => coupleGaps.Any(cg => x > cg.Left && x < cg.Right);

                double? TryDirection(double step)
                {
                    var x = anchor.X + step;
                    while (!IsOutOfBounds(x) && (InCoupleGap(x) || Occupied(x)))
                        x += step;
                    return IsOutOfBounds(x) ? null : x;
                }

                var (right, left) = (TryDirection(NodeSpacingX), TryDirection(-NodeSpacingX));
                if (right is { } r && left is { } l) return Nearer(r, l);
                if ((right ?? left) is { } found) return found;

                // Both directions ran out of safe room — scan the whole safe union for
                // any open slot at all, rather than tunnelling into foreign territory or
                // wandering off-canvas. Extremely rare in practice.
                for (var x = safeMin; x <= safeMax; x += NodeSpacingX)
                    if (!InCoupleGap(x) && !Occupied(x))
                        return x;

                return (safeMin + safeMax) / 2.0; // last resort: accept overlap over foreign placement
            }

            // ── Pull cross-root partners together (R6/R12) ─────────────────────
            // Each partner of a cross-root couple was placed in their own family's slot,
            // so even with the two families adjacent and the partners on the inner edges
            // they can sit 320px+ apart (a lone child is centred under parents who are
            // 320px wide). A partner who heads no placed group of their own (a leaf, with
            // nobody hanging below them yet; the couple's own children are placed after
            // this) can slide toward the other, as long as they don't land on anyone,
            // between anyone else's partners, or with anyone between the two of them.
            // A partner never leaves the span between their own parents (a child must
            // still read as theirs; CrossRootCouple_StillDetectedWhenOneSideIsNested...
            // pins Marc there), so a gap wider than both spans allow is only narrowed.
            // Tried in order: both move, only the right one, only the left one. If none
            // fits, nobody moves.
            bool IsLeaf(Guid id) => !primaryGroup.ContainsKey(id) && !attachedFor.ContainsKey(id);
            bool Fits(LayoutNode l, LayoutNode r, double lx, double rx)
            {
                var top = Math.Min(l.Y, r.Y) - l.Size / 2.0;
                var bottom = Math.Max(l.Y, r.Y) + l.Size / 2.0;
                foreach (var n in nodeMap.Values)
                {
                    if (n.Person.Id == l.Person.Id || n.Person.Id == r.Person.Id) continue;
                    if (n.X > lx && n.X < rx && n.Y >= top && n.Y <= bottom) return false;
                    foreach (var (m, mx) in new[] { (l, lx), (r, rx) })
                        if (Math.Sqrt(Math.Pow(n.X - mx, 2) + Math.Pow(n.Y - m.Y, 2)) < (n.Size + m.Size) / 2.0 + 10)
                            return false;
                }
                foreach (var c in couples)
                {
                    if (c.PersonAId == l.Person.Id || c.PersonBId == l.Person.Id ||
                        c.PersonAId == r.Person.Id || c.PersonBId == r.Person.Id) continue;
                    if (!nodeMap.TryGetValue(c.PersonAId, out var ca) || !nodeMap.TryGetValue(c.PersonBId, out var cb)) continue;
                    var (cl, cr) = ca.X < cb.X ? (ca, cb) : (cb, ca);
                    var cTop = Math.Min(ca.Y, cb.Y) - ca.Size / 2.0;
                    var cBottom = Math.Max(ca.Y, cb.Y) + ca.Size / 2.0;
                    foreach (var (m, mx) in new[] { (l, lx), (r, rx) })
                        if (mx > cl.X && mx < cr.X && m.Y >= cTop && m.Y <= cBottom) return false;
                }
                return true;
            }
            foreach (var (cg, _, _, _, _) in crossRootInfo)
            {
                if (!cg.ParentAId.HasValue || !cg.ParentBId.HasValue) continue;
                if (!nodeMap.TryGetValue(cg.ParentAId.Value, out var pa) ||
                    !nodeMap.TryGetValue(cg.ParentBId.Value, out var pb)) continue;
                var (l, r) = pa.X <= pb.X ? (pa, pb) : (pb, pa);
                if (r.X - l.X <= SpouseSpacingX) continue;
                // How far each may travel toward the other: to the far edge of their
                // parents' span, or unbounded with no placed parents; not at all if not a leaf.
                double Reach(LayoutNode n, bool rightward)
                {
                    if (!IsLeaf(n.Person.Id)) return n.X;
                    var parentXs = (personById[n.Person.Id].ParentIds ?? [])
                        .Where(nodeMap.ContainsKey).Select(pid => (double)nodeMap[pid].X).ToList();
                    if (parentXs.Count == 0) return rightward ? double.MaxValue : double.MinValue;
                    return rightward ? Math.Max(n.X, parentXs.Max()) : Math.Min(n.X, parentXs.Min());
                }
                var (lHi, rLo) = (Reach(l, rightward: true), Reach(r, rightward: false));
                (double, double) Plan(double lLimit, double rLimit)
                {
                    var excess = (r.X - l.X - SpouseSpacingX) / 2.0;
                    var lx = Math.Min(l.X + excess, lLimit);
                    var rx = Math.Max(r.X - excess, rLimit);
                    // Whatever one side couldn't cover, the other takes up within its limit.
                    rx = Math.Max(Math.Min(rx, lx + SpouseSpacingX), rLimit);
                    lx = Math.Min(Math.Max(lx, rx - SpouseSpacingX), lLimit);
                    return (lx, rx);
                }
                var options = new List<(double Lx, double Rx)> { Plan(lHi, rLo), Plan(l.X, rLo), Plan(lHi, r.X) };
                foreach (var (lx, rx) in options)
                {
                    if ((lx, rx) == (l.X, r.X) || !Fits(l, r, lx, rx)) continue;
                    nodeMap[l.Person.Id] = l with { X = (int)Math.Round(lx) };
                    nodeMap[r.Person.Id] = r with { X = (int)Math.Round(rx) };
                    break;
                }
            }

            // ── Place children of cross-root couples ──────────────────────────
            // Both partners are already placed as leaves; SetNode's guard skips
            // them and only the children get positioned at the couple's midpoint.
            foreach (var (cg, _, _, _, _) in crossRootInfo)
            {
                if (!cg.ParentAId.HasValue || !cg.ParentBId.HasValue) continue;
                if (!nodeMap.TryGetValue(cg.ParentAId.Value, out var nA) ||
                    !nodeMap.TryGetValue(cg.ParentBId.Value, out var nB)) continue;
                PlaceGroup(cg, (nA.X + nB.X) / 2.0);
            }

            foreach (var person in rootIndividuals)
            {
                // A "root individual" has no Parent/child group of their own, but may
                // still have an explicit Sibling link to someone deep inside an existing
                // nuclear family (e.g. self-service "we're siblings" linking with no
                // shared parent on the tree). Anchoring next to that sibling — instead of
                // always appending after the ENTIRE component's subtree — is what already
                // makes the simpler orphan-sibling cases in FamilyTreeLayoutEngineTests
                // "just work": in those tests the sibling's own group happens to be the
                // last thing placed, so appending at curX coincidentally lands adjacent.
                // Once the sibling is nested several generations deep (not a root itself),
                // that coincidence breaks down and curX has already moved far to the
                // right — found 2026-07-06 via a real "test rosenberg" sibling of Douglas
                // and Lauren (both children of Marc+Ellen) landing past Lauren's spouse.
                // Pick the RIGHTMOST already-placed sibling, not just the first one in the
                // list — a person can have multiple already-placed siblings at very
                // different X positions (e.g. one root-level sibling and one whose subtree
                // has grown wide), and anchoring off whichever happens to be first could
                // land back among earlier, narrower siblings instead of past all of them.
                var placedSiblings = (person.SiblingIds ?? [])
                    .Where(sid => nodeMap.ContainsKey(sid))
                    .Select(sid => nodeMap[sid])
                    .ToList();
                var siblingNode = placedSiblings.OrderByDescending(n => n.X).FirstOrDefault();

                if (siblingNode != null)
                {
                    var anchoredX = FindOpenXAdjacentTo(person.Id, siblingNode, placedSiblings);
                    SetNode(person.Id, anchoredX);
                    // An anchored person can land past curX (in the orphan slack after a
                    // cross-root run, or by walking off the end of the tree). Without this,
                    // the next person appended at curX was drawn on top of them.
                    curX = Math.Max(curX, anchoredX + NodeSpacingX / 2.0);
                }
                else
                {
                    SetNode(person.Id, curX + NodeSpacingX / 2.0);
                    curX += NodeSpacingX;
                }
            }

            // Safety net: place any person still unpositioned. This is where a person
            // whose own group was excluded from rootGroups above (because it isn't their
            // PRIMARY group — e.g. Harvey Fleishman, Florence's second, childless
            // husband) lands. Anchor next to an already-placed spouse when there is one,
            // same reasoning as the sibling-anchor case: appending at the far right edge
            // of the whole component is a jarring, arbitrary position for someone who
            // actually has a real, known connection on the tree.
            foreach (var person in component)
            {
                if (nodeMap.ContainsKey(person.Id)) continue;

                var placedSpouses = (person.SpouseIds ?? [])
                    .Concat(person.FormerSpouseIds ?? [])
                    .Where(sid => nodeMap.ContainsKey(sid))
                    .Select(sid => nodeMap[sid])
                    .ToList();
                var spouseNode = placedSpouses.OrderByDescending(n => n.X).FirstOrDefault();

                if (spouseNode != null)
                {
                    var anchoredX = FindOpenXAdjacentTo(person.Id, spouseNode, placedSpouses);
                    SetNode(person.Id, anchoredX);
                    curX = Math.Max(curX, anchoredX + NodeSpacingX / 2.0); // as in the sibling loop
                }
                else
                {
                    SetNode(person.Id, curX + NodeSpacingX / 2.0);
                    curX += NodeSpacingX;
                }
            }

            // A sibling relationship with no shared parent on the tree has no
            // couple/parent connector to hang off of — draw a simple line between
            // wherever the two ended up, even if they're not adjacent. PersonDto.SiblingIds
            // merges explicit Sibling relationships with siblings inferred from a shared
            // parent (see PersonMapper) — skip pairs that DO share a parent, since those
            // already render via the normal stem/T-bar/drop connector and would otherwise
            // get a redundant second line drawn directly between them.
            var drawnSiblingPairs = new HashSet<(Guid, Guid)>();
            foreach (var person in component)
            {
                if (!nodeMap.TryGetValue(person.Id, out var nodeA)) continue;
                foreach (var sibId in person.SiblingIds ?? [])
                {
                    var key = person.Id.CompareTo(sibId) < 0 ? (person.Id, sibId) : (sibId, person.Id);
                    if (!drawnSiblingPairs.Add(key)) continue;
                    if (!nodeMap.TryGetValue(sibId, out var nodeB)) continue;

                    var sibling = personById.GetValueOrDefault(sibId);
                    var sharesParent = sibling is not null &&
                        (person.ParentIds ?? []).Intersect(sibling.ParentIds ?? []).Any();
                    if (sharesParent) continue;

                    siblingLinks.Add(new SiblingLink(nodeA.X, nodeA.Y, nodeB.X, nodeB.Y));
                }
            }

            previousComponentEndX = curX;
            xOffset = (int)Math.Ceiling(curX) + ComponentGapX;
        }

        // Right-edge canvas margin is PaddingX (canvas-edge padding), not ComponentGapX
        // (inter-component gap) — xOffset above was last advanced by ComponentGapX in
        // anticipation of a component that never came.
        var canvasWidth = previousComponentEndX is { } lastEndX
            ? (int)Math.Ceiling(lastEndX) + PaddingX
            : xOffset;

        // ─────────────────────────────────────────────────────────────────────
        // PHASE 5: Create visual bands (decade markers with time gradient)
        // ─────────────────────────────────────────────────────────────────────
        //
        // Bands are decade‑sized horizontal strips that help the eye read the
        // temporal distribution of the tree at a glance.
        var bandHeight = (int)(PxPerYear * 10);  // one band per decade
        if (bandHeight < 80) bandHeight = 80;

        var bands = new List<GenerationBand>();
        for (int year = minYear; year <= maxYear; year += 10)
        {
            var bandTop = PaddingY + (int)((year - minYear) * PxPerYear);
            bands.Add(new GenerationBand(year, bandTop, bandHeight));
        }

        // ─────────────────────────────────────────────────────────────────────
        // PHASE 6: Build connectors (SVG paths between relationships)
        // ─────────────────────────────────────────────────────────────────────
        //
        // Connectors encode family structure visually:
        //   • Couple U‑arcs between partners
        //   • Vertical stems from couple/parent to a sibling span
        //   • Horizontal spans across siblings
        //   • Drops from span to each child node
        //
        // The connector builder is modular so we can evolve relationship
        // semantics without rewriting the layout engine.
        var connectors = BuildConnectors(people, couples, nodeMap);

        return new FamilyTreeLayout(
            nodeMap.Values.ToList(),
            bands,
            connectors,
            siblingLinks,
            componentDividers,
            canvasWidth,
            canvasHeight,
            focusDepth);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DEPTH CALCULATION
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes relative generational depth for each person.
    ///
    /// Strategy:
    ///   1. Pick an arbitrary focus person (first in list)
    ///   2. BFS upward through parents (ancestors get positive depth)
    ///   3. Push depths downward through children (descendants get negative depth)
    ///   4. Propagate through spouses and siblings (same depth), re-checking children
    ///      as new depths arrive so a sibling-derived depth can cascade to their kids
    ///   5. Normalize so the oldest ancestor has depth 0
    ///
    /// This produces a stable relative depth map that works even when birth
    /// years are missing or inconsistent.
    /// </summary>
    private Dictionary<Guid, int> ComputeDepths(List<PersonDto> people)
    {
        var depths = new Dictionary<Guid, int>();
        var focus = people.FirstOrDefault();
        if (focus == null) return depths;

        depths[focus.Id] = 0;

        // Pass 1: Walk upward through parents (BFS).
        var queue = new Queue<PersonDto>();
        queue.Enqueue(focus);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            foreach (var parentId in current.ParentIds ?? [])
            {
                var parent = people.FirstOrDefault(p => p.Id == parentId);
                if (parent != null && !depths.ContainsKey(parent.Id))
                {
                    depths[parent.Id] = depths[current.Id] + 1;
                    queue.Enqueue(parent);
                }
            }
        }

        // Pass 2/3 combined: push depths downward through children, and propagate
        // through spouses and siblings — iterated together until stable, since a
        // sibling picking up a depth (e.g. "Bill" from "Ray") can unlock a further
        // child-depth inference (e.g. "Willa" from "Bill") in a later round.
        bool changed = true;
        while (changed)
        {
            changed = false;

            foreach (var person in people)
            {
                foreach (var parentId in person.ParentIds ?? [])
                {
                    if (depths.TryGetValue(parentId, out var pd) &&
                        !depths.ContainsKey(person.Id))
                    {
                        depths[person.Id] = pd - 1;
                        changed = true;
                    }
                }
            }

            foreach (var person in people)
            {
                if (!depths.TryGetValue(person.Id, out var personDepth))
                    continue;

                foreach (var spouseId in person.SpouseIds ?? [])
                {
                    if (!depths.ContainsKey(spouseId))
                    {
                        depths[spouseId] = personDepth;
                        changed = true;
                    }
                }

                // Siblings share a generation even when linked only by an explicit
                // Sibling relationship with no shared parent on the tree.
                foreach (var siblingId in person.SiblingIds ?? [])
                {
                    if (!depths.ContainsKey(siblingId))
                    {
                        depths[siblingId] = personDepth;
                        changed = true;
                    }
                }
            }
        }

        // Normalize: shift so min depth = 0.
        if (depths.Any())
        {
            var minDepth = depths.Values.Min();
            foreach (var key in depths.Keys.ToList())
                depths[key] -= minDepth;
        }

        return depths;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BIRTH YEAR INFERENCE
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Assigns a birth year to each person, inferring missing values from
    /// relationships where possible.
    ///
    /// Strategy:
    ///   1. Use PersonDto.BirthDate if available
    ///   2. If an entire connected component has no known birth date at all,
    ///      seed one representative person in it so relative-offset inference
    ///      below has something to propagate from (see the component-seeding
    ///      step)
    ///   3. Infer from children: parent_year ≈ avg(child_years) - 25
    ///   4. Infer from siblings: sibling_year ≈ avg(known sibling years)
    ///   5. Infer from spouses: spouse_year ≈ avg(known spouse/former-spouse years)
    ///   6. Infer from parents: child_year ≈ avg(parent_years) + 25
    ///   7. Default remaining unknowns to the median year
    ///
    /// This multi‑pass approach converges on a consistent temporal model even
    /// when data is incomplete.
    /// </summary>
    private Dictionary<Guid, int> ComputeBirthYears(List<PersonDto> people, List<List<PersonDto>> components)
    {
        var years = new Dictionary<Guid, int>();

        // Pass 0: Assign from known birth dates.
        foreach (var person in people.Where(p => p.BirthDate.HasValue))
            years[person.Id] = person.BirthDate!.Value.Year;

        // Pass 0.5: A connected component with zero known birth dates of its own
        // has nothing for Pass 1 below to propagate from, so every one of its
        // members would otherwise fall straight through to the Pass 3 "default to
        // median" fallback -- landing every related person (parent, child,
        // sibling, spouse) on the exact same year with no regard for their
        // relationships. This is the normal state of a brand-new tree on day
        // one: the first-invited family member's whole component has no birth
        // dates yet. Seed one representative person per such component so Pass 1
        // can still infer relative spacing (parent ≈ child - 25, siblings same
        // year, etc.) within that component, even though no real anchor date
        // exists anywhere in it. Scoped per component (not "the whole tree has no
        // dates") so a brand-new, dateless branch grafted onto an already-dated
        // family doesn't also collapse onto that family's unrelated median year.
        var currentYear = DateTime.Now.Year;
        foreach (var component in components)
        {
            if (component.Count == 0) continue;
            if (component.Any(p => years.ContainsKey(p.Id))) continue;

            years[component[0].Id] = currentYear;
        }

        // Pass 1: Infer parents from children, children from parents, and siblings
        // from each other — iterated together until stable. Combining these lets a
        // sibling picked up from another sibling (e.g. "Bill" from "Ray", who have
        // no shared parent on the tree) unlock a further child-year inference
        // (e.g. "Willa" from "Bill") in a later round. Without this, two people
        // with no birth date and no inferable link (like a newly-added sibling and
        // their child) would both fall through to the same default median year and
        // render on top of each other.
        bool changed = true;
        while (changed)
        {
            changed = false;

            foreach (var person in people)
            {
                if (years.ContainsKey(person.Id)) continue;

                var childYears = (person.ChildIds ?? [])
                    .Where(cid => years.ContainsKey(cid))
                    .Select(cid => years[cid])
                    .ToList();

                if (childYears.Any())
                {
                    years[person.Id] = (int)childYears.Average() - 25;
                    changed = true;
                    continue;
                }

                var siblingYears = (person.SiblingIds ?? [])
                    .Where(sid => years.ContainsKey(sid))
                    .Select(sid => years[sid])
                    .ToList();

                if (siblingYears.Any())
                {
                    years[person.Id] = (int)siblingYears.Average();
                    changed = true;
                    continue;
                }

                // Spouses are treated as roughly the same generation. Without this, a
                // spouse with no parent/child/sibling link of their own (e.g. someone
                // who married into the family with no other recorded relatives) never
                // gets an inferred year at all and falls straight to the "default to
                // median" fallback below — landing decades away from their actual
                // partner and making the couple connector look broken.
                var spouseYears = (person.SpouseIds ?? [])
                    .Concat(person.FormerSpouseIds ?? [])
                    .Where(sid => years.ContainsKey(sid))
                    .Select(sid => years[sid])
                    .ToList();

                if (spouseYears.Any())
                {
                    years[person.Id] = (int)spouseYears.Average();
                    changed = true;
                }
            }

            foreach (var person in people)
            {
                if (years.ContainsKey(person.Id)) continue;

                var parentYears = (person.ParentIds ?? [])
                    .Where(pid => years.ContainsKey(pid))
                    .Select(pid => years[pid])
                    .ToList();

                if (parentYears.Any())
                {
                    years[person.Id] = (int)parentYears.Average() + 25;
                    changed = true;
                }
            }
        }

        // Pass 3: Default unknown people to the median year (more stable than current year).
        if (years.Any())
        {
            var sortedYears = years.Values.OrderBy(y => y).ToList();
            var defaultYear = sortedYears[sortedYears.Count / 2];

            foreach (var person in people.Where(p => !years.ContainsKey(p.Id)))
                years[person.Id] = defaultYear;
        }
        else
        {
            // Unreachable in practice -- Pass 0.5 above seeds one representative per
            // non-empty component, so every component contributes at least one year
            // before we get here. Kept as a defensive fallback only.
            var fallbackYear = DateTime.Now.Year;
            foreach (var person in people)
                years[person.Id] = fallbackYear;
        }

        EnforceGenerationConstraints(people, years);

        return years;
    }

    /// <summary>Smallest birth-year gap the layout accepts between a parent and a child
    /// when at least one of them has no recorded date.</summary>
    private const int MinParentChildGap = 20;

    /// <summary>
    /// R3 (2026-10-15): the inference passes above are greedy and take the first evidence
    /// they find, so two dateless people can land on the same year as their parent, or a
    /// child a few years below one, or partners on different rows. This pass reconciles
    /// afterwards. It never moves anyone with a recorded birth date. For each parent/child
    /// link whose gap is under <see cref="MinParentChildGap"/>, an inferred child moves
    /// down; failing that, an inferred parent moves up. Then partners with no recorded date
    /// are brought to the same year, no higher than their dated children allow. Contradictory
    /// recorded dates are left alone, and the iteration cap keeps a conflicting set from
    /// looping.
    /// </summary>
    private static void EnforceGenerationConstraints(List<PersonDto> people, Dictionary<Guid, int> years)
    {
        var dated = people.Where(p => p.BirthDate.HasValue).Select(p => p.Id).ToHashSet();
        if (dated.Count == people.Count) return;

        var byId = people.ToDictionary(p => p.Id);

        // Pairs the layout draws as a couple: current and former spouses, plus two people
        // recorded as parents of the same child. Only pairs with no recorded date on either
        // side are equalised; a dated partner keeps their own row.
        var couples = new List<(PersonDto A, PersonDto B)>();
        var seen = new HashSet<(Guid, Guid)>();
        void AddCouple(Guid x, Guid y)
        {
            if (x == y || !byId.TryGetValue(x, out var px) || !byId.TryGetValue(y, out var py)) return;
            if (dated.Contains(x) || dated.Contains(y)) return;
            if (seen.Add(x.CompareTo(y) < 0 ? (x, y) : (y, x))) couples.Add((px, py));
        }
        foreach (var person in people)
        {
            foreach (var sid in (person.SpouseIds ?? []).Concat(person.FormerSpouseIds ?? []))
                AddCouple(person.Id, sid);
            var parents = person.ParentIds ?? [];
            for (var i = 0; i < parents.Count; i++)
                for (var j = i + 1; j < parents.Count; j++)
                    AddCouple(parents[i], parents[j]);
        }

        // Latest year each undated person can have without pushing a descendant with a
        // recorded date too close (a transitive "child year - gap", shared across a couple).
        // Resolving a clash between two undated people uses it to pick the side that can move.
        var ceiling = new Dictionary<Guid, int>();
        for (var round = 0; round <= people.Count; round++)
        {
            var moved = false;
            void Lower(Guid id, int value)
            {
                if (dated.Contains(id) || (ceiling.TryGetValue(id, out var cur) && cur <= value)) return;
                ceiling[id] = value;
                moved = true;
            }
            foreach (var person in people)
                foreach (var cid in person.ChildIds ?? [])
                {
                    if (dated.Contains(cid)) Lower(person.Id, years[cid] - MinParentChildGap);
                    else if (ceiling.TryGetValue(cid, out var c)) Lower(person.Id, c - MinParentChildGap);
                }
            foreach (var (a, b) in couples)
            {
                if (ceiling.TryGetValue(a.Id, out var ca)) Lower(b.Id, ca);
                if (ceiling.TryGetValue(b.Id, out var cb)) Lower(a.Id, cb);
            }
            if (!moved) break;
        }

        bool EnforceParentGaps()
        {
            var changed = false;

            foreach (var child in people)
            {
                foreach (var pid in child.ParentIds ?? [])
                {
                    if (!years.ContainsKey(pid) || !years.ContainsKey(child.Id)) continue;
                    if (years[child.Id] - years[pid] >= MinParentChildGap) continue;

                    var raised = years[pid] + MinParentChildGap;
                    var childCanMove = !dated.Contains(child.Id) &&
                                       (!ceiling.TryGetValue(child.Id, out var cap) || raised <= cap);
                    if (childCanMove)
                        years[child.Id] = raised;
                    else if (!dated.Contains(pid))
                        years[pid] = years[child.Id] - MinParentChildGap;
                    else if (!dated.Contains(child.Id))
                        years[child.Id] = raised;
                    else
                        continue;
                    changed = true;
                }
            }

            return changed;
        }

        for (var iteration = 0; iteration < 50; iteration++)
        {
            var changed = EnforceParentGaps();

            foreach (var (a, b) in couples)
            {
                if (years[a.Id] == years[b.Id]) continue;

                var target = Math.Max(years[a.Id], years[b.Id]);
                if (ceiling.TryGetValue(a.Id, out var capA)) target = Math.Min(target, capA);
                if (ceiling.TryGetValue(b.Id, out var capB)) target = Math.Min(target, capB);

                if (years[a.Id] == target && years[b.Id] == target) continue;
                years[a.Id] = target;
                years[b.Id] = target;
                changed = true;
            }

            if (!changed) break;
        }

        // Parent/child order outranks partner equality: dateless people married across a
        // generation (a woman married to both a father and his son) can't satisfy both, so
        // finish on the parent pass and a child never renders level with or above a parent.
        for (var i = 0; i < 50 && EnforceParentGaps(); i++) { }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CONNECTED COMPONENTS
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Groups people into separate connected components using BFS.
    ///
    /// A connected component is a maximal set of people connected through
    /// parent/child/spouse (current or former)/sibling relationships. Multiple components represent
    /// orphaned trees that are rendered side‑by‑side.
    /// </summary>
    private List<List<PersonDto>> IdentifyConnectedComponents(List<PersonDto> people)
    {
        var visited = new HashSet<Guid>();
        var components = new List<List<PersonDto>>();

        foreach (var person in people)
        {
            if (visited.Contains(person.Id)) continue;

            var component = new List<PersonDto>();
            var queue = new Queue<PersonDto>();
            queue.Enqueue(person);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (visited.Contains(current.Id)) continue;

                visited.Add(current.Id);
                component.Add(current);

                // Enqueue all related people.
                var relatedIds = new HashSet<Guid>();
                relatedIds.UnionWith(current.ParentIds ?? []);
                relatedIds.UnionWith(current.ChildIds ?? []);
                relatedIds.UnionWith(current.SpouseIds ?? []);
                // An ended marriage still connects two people (R1, 2026-10-11): without it,
                // someone whose only link is a former spouse became their own component,
                // drawn 320px+ off to the side and, if dateless, seeded at the current year.
                relatedIds.UnionWith(current.FormerSpouseIds ?? []);
                relatedIds.UnionWith(current.SiblingIds ?? []);

                foreach (var relatedId in relatedIds.Where(id => !visited.Contains(id)))
                {
                    var related = people.FirstOrDefault(p => p.Id == relatedId);
                    if (related != null)
                        queue.Enqueue(related);
                }
            }

            components.Add(component);
        }

        return components;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // LINEAGE-SIDE ORDERING
    // ─────────────────────────────────────────────────────────────────────────
    //
    // Dedicates a stable side of the canvas to each of an anchor person's two
    // parents' families (e.g. Doug's father's relatives always render on one
    // side, his mother's on the other), computed fresh on every ComputeLayout
    // call rather than incrementally reordering already-placed nodes — the same
    // shape as the barycenter/median heuristic used for crossing-minimization in
    // the Sugiyama layered-graph framework. A generic sibling-clustering REORDER
    // was already tried in this codebase and reverted (see the "place roots left
    // to right" comment above) because it moved already-placed nodes; this is a
    // pure sort key instead, so it carries none of that risk. Confirmed to be
    // anchored to a fixed, stable person (e.g. the logged-in user's own linked
    // Person) — NOT the transient focusPersonId, which changes as different
    // people are viewed. Requested 2026-07-07.

    /// <summary>
    /// BFS hop-distance from each of the anchor's two recorded parents, over an
    /// undirected graph built from Parent/Child/Spouse/FormerSpouse/Sibling links.
    /// Returns null if the anchor isn't found or has fewer than two resolvable
    /// parents — callers must treat that as "side ordering doesn't apply here."
    /// </summary>
    private static (Dictionary<Guid, int> DistFromP1, Dictionary<Guid, int> DistFromP2)? ComputeLineageSideDistances(
        List<PersonDto> people, Guid anchorPersonId)
    {
        var personById = people.ToDictionary(p => p.Id);
        if (!personById.TryGetValue(anchorPersonId, out var anchor))
            return null;

        var personIndex = new Dictionary<Guid, int>();
        for (int i = 0; i < people.Count; i++) personIndex.TryAdd(people[i].Id, i);

        // Deterministic pick of "first two" parents by list position — not by Gender,
        // since PersonMapper/ParentIds carries no father-first/mother-first convention
        // to rely on, and inventing one here would be an assumption the rest of the
        // codebase doesn't make.
        var parents = (anchor.ParentIds ?? [])
            .Where(personById.ContainsKey)
            .Distinct()
            .OrderBy(id => personIndex.TryGetValue(id, out var idx) ? idx : int.MaxValue)
            .ToList();

        if (parents.Count < 2) return null;

        var p1 = parents[0];
        var p2 = parents[1];

        // Never hop through the anchor themselves, and exclude the direct p1<->p2
        // marriage edge in both directions — otherwise one parent's BFS leaks into
        // the other's side via their own spousal link, defeating the whole point.
        IEnumerable<Guid> Neighbors(Guid id)
        {
            if (!personById.TryGetValue(id, out var person)) yield break;

            // FormerSpouseIds deliberately included — a parent's children from an earlier marriage still belong on that
            // parent's side of the canvas.
            var raw = (person.ParentIds ?? [])
                .Concat(person.ChildIds ?? [])
                .Concat(person.SpouseIds ?? [])
                .Concat(person.FormerSpouseIds ?? [])
                .Concat(person.SiblingIds ?? []);

            foreach (var n in raw)
            {
                if (n == anchorPersonId) continue;
                if (id == p1 && n == p2) continue;
                if (id == p2 && n == p1) continue;
                yield return n;
            }
        }

        Dictionary<Guid, int> Bfs(Guid start)
        {
            var dist = new Dictionary<Guid, int> { [start] = 0 };
            var queue = new Queue<Guid>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                foreach (var n in Neighbors(id))
                    if (!dist.ContainsKey(n)) { dist[n] = dist[id] + 1; queue.Enqueue(n); }
            }
            return dist;
        }

        return (Bfs(p1), Bfs(p2));
    }

    /// <summary>
    /// Sorts root groups by which of the anchor's two parents' sides they're
    /// closer to (stable sort — ties/unreachable groups land in a neutral centre
    /// bucket, preserving their existing relative order). No-ops entirely when
    /// anchorPersonId is null or the anchor doesn't have two recorded parents.
    /// </summary>
    private static List<NuclearGroup> ApplyLineageSideOrder(
        List<PersonDto> people, List<NuclearGroup> rootGroups, Guid? anchorPersonId)
    {
        if (!anchorPersonId.HasValue) return rootGroups;

        var distances = ComputeLineageSideDistances(people, anchorPersonId.Value);
        if (distances is not { } d) return rootGroups;

        int SideRank(NuclearGroup rg)
        {
            var members = new[] { rg.ParentAId, rg.ParentBId }
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();

            var d1 = members.Select(m => d.DistFromP1.GetValueOrDefault(m, int.MaxValue)).DefaultIfEmpty(int.MaxValue).Min();
            var d2 = members.Select(m => d.DistFromP2.GetValueOrDefault(m, int.MaxValue)).DefaultIfEmpty(int.MaxValue).Min();

            if (d1 == int.MaxValue && d2 == int.MaxValue) return 1; // unreachable from either side → neutral centre
            if (d1 == d2) return 1;                                 // tie (incl. the anchor's own parents, 0/0) → neutral centre
            return d1 < d2 ? 0 : 2;                                 // side 1 (left) vs side 2 (right)
        }

        return [.. rootGroups.OrderBy(SideRank)];
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PARENT CYCLES (R0)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A Parent cycle (someone recorded as their own ancestor) is invalid data that the write
    /// paths now refuse, but rows saved before that check can still hold one. The recursions in
    /// <see cref="ComputeLayout"/> are guarded so the tree still renders; this leaves a trace
    /// in the logs naming who is caught in it so the data can be repaired.
    /// </summary>
    private void WarnAboutParentCycles(List<PersonDto> people)
    {
        var graph = new ParentGraph(people.SelectMany(p => (p.ParentIds ?? []).Select(parent => (parent, p.Id))));
        var cycles = graph.FindCycles();
        if (cycles.Count == 0) return;

        var names = new Dictionary<Guid, string>();
        foreach (var p in people) names.TryAdd(p.Id, p.FullName);
        foreach (var cycle in cycles.Where(c => _warnedCycles.Add(string.Join(",", c))))
            _logger.LogWarning(
                "Parent cycle in family tree data involving {Count} people: {People}. The tree still " +
                "renders, but the loop is ignored where the layout would otherwise recurse forever. " +
                "Remove one of the Parent links to repair it.",
                cycle.Count,
                string.Join(", ", cycle.Select(id => $"{names.GetValueOrDefault(id, "(unknown)")} ({id})")));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // NUCLEAR GROUP BUILDING
    // ─────────────────────────────────────────────────────────────────────────

    private List<NuclearGroup> BuildNuclearGroups(List<PersonDto> people, List<CoupleDto> couples)
    {
        var groups    = new List<NuclearGroup>();
        var personIds = people.Select(p => p.Id).ToHashSet();
        var claimed   = new HashSet<Guid>();

        // Sort couples by their OWN identity (position in the deterministically-ordered
        // people list) — NOT by CoupleHelper.Derive's incidental dictionary-insertion
        // order, which is driven by whichever CHILD's shared-parent check happens to run
        // first. Found 2026-07-06: marrying "Bill" himself sent his entire nuclear group
        // to the far left of the tree, because his child Willa's last name ("Kuh") sorted
        // alphabetically before "Rosenberg"/"Small" — nothing to do with genealogy, purely
        // an artifact of dictionary iteration order. Sorting couples by the couple's own
        // position keeps root-group order stable and predictable regardless of which
        // child happens to trigger the couple's discovery.
        var personIndex = new Dictionary<Guid, int>();
        for (int i = 0; i < people.Count; i++) personIndex.TryAdd(people[i].Id, i);

        int CoupleSortKey(CoupleDto c) => Math.Min(
            personIndex.TryGetValue(c.PersonAId, out var a) ? a : int.MaxValue,
            personIndex.TryGetValue(c.PersonBId, out var b) ? b : int.MaxValue);

        // Couple families: children claimed here are not assigned to single parents.
        foreach (var couple in couples
            .Where(c => personIds.Contains(c.PersonAId) && personIds.Contains(c.PersonBId))
            .OrderBy(CoupleSortKey))
        {
            var children = couple.ChildIds.Where(personIds.Contains).ToList();
            foreach (var c in children) claimed.Add(c);
            groups.Add(new NuclearGroup(couple.PersonAId, couple.PersonBId, children));
        }

        // Single-parent families for children not already claimed by a couple.
        var singleChildren = new Dictionary<Guid, List<Guid>>();
        foreach (var person in people)
        {
            if (claimed.Contains(person.Id)) continue;
            foreach (var parentId in person.ParentIds ?? [])
            {
                if (!personIds.Contains(parentId)) continue;
                if (!singleChildren.TryGetValue(parentId, out var list))
                    singleChildren[parentId] = list = [];
                list.Add(person.Id);
            }
        }
        foreach (var (parentId, children) in singleChildren)
            groups.Add(new NuclearGroup(parentId, null, children));

        return groups;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CONNECTOR BUILDING
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds SVG connectors (family relationship paths).
    ///
    /// Creates:
    ///   • Couple U‑arcs (curved bracket between partners)
    ///   • Stem lines (vertical parent→child flow)
    ///   • Sibling spans (horizontal bow across children)
    ///   • Child drops (bezier curves from span to child nodes)
    ///
    /// The logic is structured to avoid duplication between coupled and
    /// single‑parent families while keeping the visual grammar consistent.
    /// </summary>
    private List<FamilyUnit> BuildConnectors(
        List<PersonDto> people,
        List<CoupleDto> couples,
        Dictionary<Guid, LayoutNode> nodeMap)
    {
        var families = new List<FamilyUnit>();
        var routedChildren = new HashSet<Guid>();

        // 1. Process coupled families.
        foreach (var couple in couples)
        {
            if (!nodeMap.TryGetValue(couple.PersonAId, out var a) ||
                !nodeMap.TryGetValue(couple.PersonBId, out var b))
                continue;

            var children = couple.ChildIds
                .Where(nodeMap.ContainsKey)
                .Select(id => nodeMap[id])
                .ToList();

            foreach (var c in children)
                routedChildren.Add(c.Person.Id);

            var coupleUnits = BuildCoupleFamilyUnits(a, b, children, couple.IsFormer);
            families.AddRange(coupleUnits);
        }

        // 2. Process single‑parent families — group all children by parent so the
        //    stem→span→drops pattern matches coupled families and avoids S‑curve gaps.
        var singleParentGroups = new Dictionary<Guid, List<Guid>>();

        foreach (var person in people)
        {
            if (routedChildren.Contains(person.Id)) continue;

            foreach (var parentId in person.ParentIds ?? [])
            {
                if (!nodeMap.ContainsKey(parentId) || !nodeMap.ContainsKey(person.Id))
                    continue;

                if (!singleParentGroups.ContainsKey(parentId))
                    singleParentGroups[parentId] = [];

                singleParentGroups[parentId].Add(person.Id);
            }
        }

        foreach (var (parentId, childIds) in singleParentGroups)
        {
            var parent = nodeMap[parentId];
            var children = childIds
                .Where(nodeMap.ContainsKey)
                .Select(id => nodeMap[id])
                .ToList();

            if (!children.Any()) continue;

            var singleUnits = BuildSingleParentFamilyUnits(parent, children);
            families.AddRange(singleUnits);
        }

        return families;
    }

    /// <summary>
    /// Builds one or more FamilyUnit connectors for a couple and their children.
    ///
    /// Handles:
    ///   • No children → horizontal couple line, no stem
    ///   • One child   → horizontal line + vertical stem + T‑bar + drop
    ///   • Many        → horizontal line + vertical stem + T‑bar + drops
    /// </summary>
    private IEnumerable<FamilyUnit> BuildCoupleFamilyUnits(
        LayoutNode partnerA,
        LayoutNode partnerB,
        List<LayoutNode> children,
        bool isFormer = false)
    {
        var families = new List<FamilyUnit>();

        var sharedY = Math.Max(partnerA.Y, partnerB.Y);
        var aBottomY = sharedY + partnerA.Size / 2.0;
        var bBottomY = sharedY + partnerB.Size / 2.0;
        var midX = (partnerA.X + partnerB.X) / 2.0;
        // Horizontal connector at the bottom of the lower node
        var lineY = Math.Max(aBottomY, bBottomY);

        var arc = new CoupleArc(
            partnerA.X, lineY,
            partnerB.X, lineY,
            lineY,  // PeakY kept for record compat — equals lineY (no curve)
            midX,
            lineY,  // HeartY at the connector level
            isFormer);

        if (!children.Any())
        {
            // Couple with no children: just the horizontal line.
            var stem = new StemLine(midX, lineY, lineY);
            families.Add(new FamilyUnit(arc, stem, null, Array.Empty<ChildDrop>()));
            return families;
        }

        // Compute the Y position of the T‑bar (just above the topmost child).
        var spanY = children.Min(c => c.Y - c.Size / 2.0) - 18;
        var stemLine = new StemLine(midX, lineY, spanY);

        // Always include a T‑bar so drops connect cleanly even for single children.
        var span = BuildSiblingSpanIncludingParentCenter(
            parentCenterX: midX,
            children: children,
            spanY: spanY);

        var childDrops = children
            .Select(c => new ChildDrop(c.X, c.Y - c.Size / 2.0))
            .ToList();

        families.Add(new FamilyUnit(arc, stemLine, span, childDrops));
        return families;
    }

    /// <summary>
    /// Builds one or more FamilyUnit connectors for a single parent and their children.
    ///
    /// Mirrors the visual grammar of couples:
    ///   • Vertical stem from parent
    ///   • Optional horizontal span across children
    ///   • Drops from span (or stem) to each child
    /// </summary>
    private IEnumerable<FamilyUnit> BuildSingleParentFamilyUnits(
        LayoutNode parent,
        List<LayoutNode> children)
    {
        var families = new List<FamilyUnit>();

        var parentBottomY = parent.Y + parent.Size / 2.0;
        var spanY = children.Min(c => c.Y - c.Size / 2.0) - 18;

        var stem = new StemLine(parent.X, parentBottomY, spanY);

        // Always include a T‑bar so drops connect cleanly even for single children.
        var span = BuildSiblingSpanIncludingParentCenter(
            parentCenterX: parent.X,
            children: children,
            spanY: spanY);

        var childDrops = children
            .Select(c => new ChildDrop(c.X, c.Y - c.Size / 2.0))
            .ToList();

        families.Add(new FamilyUnit(null, stem, span, childDrops));
        return families;
    }

    /// <summary>
    /// Builds a sibling span that always includes the parent's center X.
    ///
    /// The span covers exactly the range [childMinX, childMaxX] extended to also
    /// include parentCenterX. This guarantees the vertical stem connects to the span
    /// without artificially widening it beyond the actual children positions.
    /// </summary>
    private SiblingSpan? BuildSiblingSpanIncludingParentCenter(
        double parentCenterX,
        List<LayoutNode> children,
        double spanY)
    {
        var childMinX = children.Min(c => (double)c.X);
        var childMaxX = children.Max(c => (double)c.X);

        var spanLeft  = Math.Min(childMinX, parentCenterX);
        var spanRight = Math.Max(childMaxX, parentCenterX);

        if (Math.Abs(spanRight - spanLeft) <= 1)
            return null;

        return new SiblingSpan(spanLeft, spanRight, spanY);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RECORDS (data structures for layout output)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A positioned person node ready for rendering.
    /// </summary>
    public record LayoutNode(
        PersonDto Person,
        int X,
        int Y,
        int Depth,
        bool IsFocus,
        int Size);

    /// <summary>
    /// A horizontal band representing a decade on the timeline.
    /// </summary>
    public record GenerationBand(
        int Depth,
        int Top,
        int Height);

    /// <summary>
    /// A U‑shaped arc connecting two partners, with a heart at the peak.
    /// </summary>
    public record CoupleArc(
        double PartnerAX,
        double PartnerAY,
        double PartnerBX,
        double PartnerBY,
        double PeakY,
        double MidX,
        double HeartY,
        bool IsFormer = false);

    /// <summary>
    /// A vertical line from a couple/parent down toward children.
    /// </summary>
    public record StemLine(double X, double TopY, double BotY);

    /// <summary>
    /// A horizontal line across siblings.
    /// </summary>
    public record SiblingSpan(double LeftX, double RightX, double Y);

    /// <summary>
    /// A vertical drop from a sibling span to a child node.
    /// </summary>
    public record ChildDrop(double CenterX, double BotY);

    /// <summary>
    /// A direct line between two siblings who have no shared parent on the tree
    /// (e.g. a newly-added sibling like "Bill" linked only to "Ray") — there is no
    /// parent/couple group to hang a normal stem+span+drop connector off of.
    /// </summary>
    public record SiblingLink(double AX, double AY, double BX, double BY);

    /// <summary>
    /// A subtle vertical boundary marking the gap between two genuinely disconnected
    /// components (no relationship path between them at all) sharing one canvas.
    /// </summary>
    public record ComponentDivider(double X, int TopY, int BotY);

    /// <summary>
    /// A nuclear family group used internally during X-axis layout.
    /// Distinct from <see cref="FamilyUnit"/>, which is the SVG connector output.
    /// </summary>
    private record NuclearGroup(Guid? ParentAId, Guid? ParentBId, List<Guid> ChildIds);

    /// <summary>
    /// Identity of a <see cref="NuclearGroup"/> by its parents alone. The record's own equality
    /// compares the ChildIds list by reference, and the cross-root reorder swaps in copies
    /// with a new list, so the cycle guards (see MarkDescendants) key on this instead.
    /// BuildNuclearGroups makes at most one group per parent pair or single parent.
    /// </summary>
    private readonly record struct GroupKey(Guid? ParentAId, Guid? ParentBId)
    {
        public static GroupKey Of(NuclearGroup g) => new(g.ParentAId, g.ParentBId);
    }

    /// <summary>
    /// A complete visual family unit: couple (optional), stem, span (optional), and drops.
    /// </summary>
    public record FamilyUnit(
        CoupleArc? Couple,
        StemLine Stem,
        SiblingSpan? Span,
        IReadOnlyList<ChildDrop> Drops);
}

/// <summary>
/// Complete layout output containing all positioning, bands, and connectors.
/// Ready to render in FamilyTreeCanvas.
/// </summary>
public record FamilyTreeLayout(
    List<FamilyTreeLayoutEngine.LayoutNode> Nodes,
    List<FamilyTreeLayoutEngine.GenerationBand> Bands,
    List<FamilyTreeLayoutEngine.FamilyUnit> Connectors,
    List<FamilyTreeLayoutEngine.SiblingLink> SiblingLinks,
    List<FamilyTreeLayoutEngine.ComponentDivider> ComponentDividers,
    int CanvasWidth,
    int CanvasHeight,
    int FocusDepth)
{
    public static FamilyTreeLayout Empty => new([], [], [], [], [], 900, 600, 0);
}