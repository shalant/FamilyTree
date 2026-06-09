using FamilyTree.Shared.DTOs;
using FamilyTree.Shared.DTOs.Person;

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
        Guid? focusPersonId)
    {
        if (!people.Any())
            return FamilyTreeLayout.Empty;

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
        // PHASE 2: Compute birth years (absolute timeline positions)
        // ─────────────────────────────────────────────────────────────────────
        //
        // We build a robust birth‑year map that:
        //   • Uses explicit BirthDate when present
        //   • Infers parents from children (parent ≈ child - 25)
        //   • Infers children from parents (child ≈ parent + 25)
        //   • Defaults remaining unknowns to the median year
        //
        // This produces a smooth temporal axis even with sparse data.
        var birthYears = ComputeBirthYears(people);
        // Snap to decade boundaries so band labels land on round numbers (1910, 1920 …)
        var minYear = (birthYears.Values.Min() / 10) * 10;
        var maxYear = ((birthYears.Values.Max() + 9) / 10) * 10;
        var yearRange = maxYear - minYear + 1;
        var canvasHeight = (int)(yearRange * PxPerYear + PaddingY * 2);

        // ─────────────────────────────────────────────────────────────────────
        // PHASE 3: Identify connected components (separate family trees)
        // ─────────────────────────────────────────────────────────────────────
        //
        // A connected component is a maximal set of people connected through
        // parent/child/spouse relationships. Each component is rendered as an
        // independent tree, laid out side‑by‑side horizontally.
        var components = IdentifyConnectedComponents(people);

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
        var xOffset = PaddingX;

        foreach (var component in components)
        {
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

            // Map each person to the one group they head as a parent.
            // Couple groups are preferred over single-parent groups.
            var primaryGroup = new Dictionary<Guid, NuclearGroup>();
            foreach (var g in compGroups.OrderByDescending(g => g.ParentBId.HasValue ? 1 : 0))
            {
                if (g.ParentAId.HasValue) primaryGroup.TryAdd(g.ParentAId.Value, g);
                if (g.ParentBId.HasValue) primaryGroup.TryAdd(g.ParentBId.Value, g);
            }

            // Root groups: parents that are not children of any group (tree tops).
            var rootGroups = compGroups.Where(g =>
                (!g.ParentAId.HasValue || !childrenInGroups.Contains(g.ParentAId.Value)) &&
                (!g.ParentBId.HasValue || !childrenInGroups.Contains(g.ParentBId.Value)))
                .ToList();

            // Root individuals: people in no group at all (unlinked singletons).
            var rootIndividuals = component.Where(p =>
                !childrenInGroups.Contains(p.Id) &&
                !parentsInGroups.Contains(p.Id))
                .ToList();

            // ── Cross-root couple detection ───────────────────────────────────
            // When a child of root group A marries a child of root group B,
            // PlaceGroup would recurse into their NuclearGroup from both sides,
            // writing conflicting X positions (tangle). Fix:
            //   1. Remove the couple from primaryGroup → each partner becomes
            //      a leaf placed under their own parent group.
            //   2. Sort root groups so the two bridged groups are adjacent and
            //      ParentA's group is to the left.
            //   3. Sort children so cross-root partners land at the inner edges,
            //      producing a natural couple connector that spans between families.
            // After all root groups are placed, PlaceGroup is called once more
            // for each cross-root couple to position their children at the midpoint.

            var directChildOfRoot = new Dictionary<Guid, NuclearGroup>();
            foreach (var rg in rootGroups)
                foreach (var cid in rg.ChildIds)
                    directChildOfRoot.TryAdd(cid, rg);

            var crossRootInfo = new List<(NuclearGroup couple, NuclearGroup rootA, NuclearGroup rootB)>();
            foreach (var g in compGroups)
            {
                if (!g.ParentAId.HasValue || !g.ParentBId.HasValue) continue;
                if (!directChildOfRoot.TryGetValue(g.ParentAId.Value, out var rgA)) continue;
                if (!directChildOfRoot.TryGetValue(g.ParentBId.Value, out var rgB)) continue;
                if (rgA == rgB) continue;
                crossRootInfo.Add((g, rgA, rgB));
            }

            if (crossRootInfo.Count > 0)
            {
                // 1. Remove from primaryGroup so partners are placed as simple leaves.
                foreach (var (cg, _, _) in crossRootInfo)
                {
                    if (cg.ParentAId.HasValue && primaryGroup.TryGetValue(cg.ParentAId.Value, out var pA) && pA == cg)
                        primaryGroup.Remove(cg.ParentAId.Value);
                    if (cg.ParentBId.HasValue && primaryGroup.TryGetValue(cg.ParentBId.Value, out var pB) && pB == cg)
                        primaryGroup.Remove(cg.ParentBId.Value);
                }

                // 2. Sort root groups: rootA (ParentA's group) left of rootB.
                var rootGroupScore = rootGroups.ToDictionary(rg => rg, _ => 0);
                foreach (var (_, rgA, rgB) in crossRootInfo)
                {
                    rootGroupScore[rgA] -= 1;
                    rootGroupScore[rgB] += 1;
                }
                rootGroups = [.. rootGroups.OrderBy(rg => rootGroupScore[rg])];

                // 3. Sort children: cross-root ParentA → rightmost, ParentB → leftmost.
                var crossEdge = new Dictionary<Guid, bool>(); // true = isParentA (right edge)
                foreach (var (cg, _, _) in crossRootInfo)
                {
                    if (cg.ParentAId.HasValue) crossEdge[cg.ParentAId.Value] = true;
                    if (cg.ParentBId.HasValue) crossEdge[cg.ParentBId.Value] = false;
                }
                rootGroups = rootGroups
                    .Select(rg => rg with
                    {
                        ChildIds = [.. rg.ChildIds.OrderBy(cid =>
                            crossEdge.TryGetValue(cid, out var isA) ? (isA ? 2 : 0) : 1)]
                    })
                    .ToList();
            }

            // ── Memoised bottom-up width measurement ─────────────────────────
            var widthCache = new Dictionary<NuclearGroup, double>();

            double MeasureGroup(NuclearGroup g)
            {
                if (widthCache.TryGetValue(g, out var cached)) return cached;

                double selfW = g.ParentBId.HasValue
                    ? NodeSpacingX + SpouseSpacingX
                    : NodeSpacingX;

                double childrenW = g.ChildIds.Count == 0
                    ? 0
                    : g.ChildIds.Sum(cid =>
                        primaryGroup.TryGetValue(cid, out var cg)
                            ? MeasureGroup(cg)
                            : (double)NodeSpacingX);

                var w = g.ChildIds.Count == 0 ? selfW : Math.Max(selfW, childrenW);
                widthCache[g] = w;
                return w;
            }

            // ── Node creation ─────────────────────────────────────────────────
            void SetNode(Guid id, double x)
            {
                if (nodeMap.ContainsKey(id)) return; // cross-root couples: don't overwrite
                var person   = personById[id];
                var isFocus  = id == focusPersonId;
                var depth    = depths.GetValueOrDefault(id, 0);
                var relDepth = depth - focusDepth;
                var size     = isFocus ? FocusSize
                             : Math.Abs(relDepth) <= 1 ? Gen1Size
                             : DefaultSize;
                var year = birthYears.GetValueOrDefault(id, minYear);
                var y    = PaddingY + (int)((year - minYear) * PxPerYear);
                nodeMap[id] = new LayoutNode(person, (int)Math.Round(x), y, depth, isFocus, size);
            }

            // ── Top-down placement ────────────────────────────────────────────
            void PlaceGroup(NuclearGroup g, double anchorX)
            {
                if (g.ParentAId.HasValue && g.ParentBId.HasValue)
                {
                    SetNode(g.ParentAId.Value, anchorX - SpouseSpacingX / 2.0);
                    SetNode(g.ParentBId.Value, anchorX + SpouseSpacingX / 2.0);
                }
                else if (g.ParentAId.HasValue)
                {
                    SetNode(g.ParentAId.Value, anchorX);
                }

                if (g.ChildIds.Count == 0) return;

                var cWidths = g.ChildIds
                    .Select(cid => primaryGroup.TryGetValue(cid, out var cg)
                        ? MeasureGroup(cg)
                        : (double)NodeSpacingX)
                    .ToList();

                double totalW = cWidths.Sum();
                double startX = anchorX - totalW / 2.0;

                for (int i = 0; i < g.ChildIds.Count; i++)
                {
                    var childId    = g.ChildIds[i];
                    double childAnchor = startX + cWidths[i] / 2.0;

                    if (primaryGroup.TryGetValue(childId, out var childGroup))
                        PlaceGroup(childGroup, childAnchor);
                    else
                        SetNode(childId, childAnchor);

                    startX += cWidths[i];
                }
            }

            // ── Place roots left to right ─────────────────────────────────────
            double curX = xOffset;

            foreach (var rg in rootGroups)
            {
                var w = MeasureGroup(rg);
                PlaceGroup(rg, curX + w / 2.0);
                curX += w;
            }

            // ── Place children of cross-root couples ──────────────────────────
            // Both partners are already placed as leaves; SetNode's guard skips
            // them and only the children get positioned at the couple's midpoint.
            foreach (var (cg, _, _) in crossRootInfo)
            {
                if (!cg.ParentAId.HasValue || !cg.ParentBId.HasValue) continue;
                if (!nodeMap.TryGetValue(cg.ParentAId.Value, out var nA) ||
                    !nodeMap.TryGetValue(cg.ParentBId.Value, out var nB)) continue;
                PlaceGroup(cg, (nA.X + nB.X) / 2.0);
            }

            foreach (var person in rootIndividuals)
            {
                SetNode(person.Id, curX + NodeSpacingX / 2.0);
                curX += NodeSpacingX;
            }

            // Safety net: place any person still unpositioned at the right edge.
            foreach (var person in component)
            {
                if (!nodeMap.ContainsKey(person.Id))
                {
                    SetNode(person.Id, curX + NodeSpacingX / 2.0);
                    curX += NodeSpacingX;
                }
            }

            xOffset = (int)Math.Ceiling(curX) + PaddingX;
        }

        var canvasWidth = xOffset;

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
    ///   4. Propagate through spouses (same depth as partner)
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

        // Pass 2: Push depths downward through children.
        foreach (var person in people)
        {
            foreach (var parentId in person.ParentIds ?? [])
            {
                if (depths.TryGetValue(parentId, out var pd) &&
                    !depths.ContainsKey(person.Id))
                {
                    depths[person.Id] = pd - 1;
                }
            }
        }

        // Pass 3: Propagate through spouses (iterate until stable).
        bool changed = true;
        while (changed)
        {
            changed = false;

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
    ///   2. Infer from children: parent_year ≈ avg(child_years) - 25
    ///   3. Infer from parents: child_year ≈ avg(parent_years) + 25
    ///   4. Default remaining unknowns to the median year
    ///
    /// This multi‑pass approach converges on a consistent temporal model even
    /// when data is incomplete.
    /// </summary>
    private Dictionary<Guid, int> ComputeBirthYears(List<PersonDto> people)
    {
        var years = new Dictionary<Guid, int>();

        // Pass 0: Assign from known birth dates.
        foreach (var person in people.Where(p => p.BirthDate.HasValue))
            years[person.Id] = person.BirthDate!.Value.Year;

        // Pass 1: Infer parents from children.
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
                }
            }
        }

        // Pass 2: Infer children from parents.
        changed = true;
        while (changed)
        {
            changed = false;

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
            var currentYear = DateTime.Now.Year;
            foreach (var person in people)
                years[person.Id] = currentYear;
        }

        return years;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CONNECTED COMPONENTS
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Groups people into separate connected components using BFS.
    ///
    /// A connected component is a maximal set of people connected through
    /// parent/child/spouse relationships. Multiple components represent
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
    // NUCLEAR GROUP BUILDING
    // ─────────────────────────────────────────────────────────────────────────

    private List<NuclearGroup> BuildNuclearGroups(List<PersonDto> people, List<CoupleDto> couples)
    {
        var groups    = new List<NuclearGroup>();
        var personIds = people.Select(p => p.Id).ToHashSet();
        var claimed   = new HashSet<Guid>();

        // Couple families: children claimed here are not assigned to single parents.
        foreach (var couple in couples)
        {
            if (!personIds.Contains(couple.PersonAId) || !personIds.Contains(couple.PersonBId))
                continue;

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
    /// A nuclear family group used internally during X-axis layout.
    /// Distinct from <see cref="FamilyUnit"/>, which is the SVG connector output.
    /// </summary>
    private record NuclearGroup(Guid? ParentAId, Guid? ParentBId, List<Guid> ChildIds);

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
    int CanvasWidth,
    int CanvasHeight,
    int FocusDepth)
{
    public static FamilyTreeLayout Empty => new([], [], [], 900, 600, 0);
}