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
        // PHASE 4: Position all nodes (X, Y coordinates)
        // ─────────────────────────────────────────────────────────────────────
        //
        // For each component:
        //   • Group people by depth
        //   • Center each depth row horizontally within the component
        //   • Apply dynamic spacing (wider for couples)
        //   • Map birth years → Y positions
        //
        // The result is a stable, balanced layout that respects both time and
        // generational structure.
        // Couple lookup from CoupleDto for reliable spouse detection (SpouseIds may be
        // incomplete while incrementally building the tree).
        var couplePairs = new HashSet<(Guid, Guid)>(
            couples.Select(c => c.PersonAId < c.PersonBId
                ? (c.PersonAId, c.PersonBId)
                : (c.PersonBId, c.PersonAId)));

        var nodeMap = new Dictionary<Guid, LayoutNode>();
        var xOffset = PaddingX;

        foreach (var component in components)
        {
            // Group by depth within this component and sort oldest (highest depth) at the top.
            var componentByDepth = component
                .GroupBy(p => depths.GetValueOrDefault(p.Id, 0))
                .OrderByDescending(g => g.Key)
                .ToList();

            // Pre-compute couple-aware row data so we can size the component correctly.
            var rows = componentByDepth
                .Select(g => SortSpousesAdjacent(g.ToList(), couplePairs))
                .Select(sorted => (sorted, offsets: ComputeRowOffsets(sorted, couplePairs)))
                .ToList();

            var maxRowWidth = rows.Max(r => r.offsets.Length > 0 ? r.offsets[^1] : 0.0);
            var componentXWidth = Math.Max((int)maxRowWidth + PaddingX * 2, NodeSpacingX + PaddingX * 2);

            foreach (var (sortedMembers, xOffsets) in rows)
            {
                var rowWidth = xOffsets.Length > 1 ? xOffsets[^1] : 0.0;
                var startX = -rowWidth / 2.0;

                for (int i = 0; i < sortedMembers.Count; i++)
                {
                    var person = sortedMembers[i];
                    var isFocus = person.Id == focusPersonId;
                    var relDepth = depths.GetValueOrDefault(person.Id, 0);

                    // Size encodes semantic importance:
                    //   • Focus person: largest
                    //   • Immediate relatives (±1 depth): medium
                    //   • Others: default
                    var size = isFocus
                        ? FocusSize
                        : Math.Abs(relDepth) <= 1
                            ? Gen1Size
                            : DefaultSize;

                    var yearOffset = birthYears.GetValueOrDefault(person.Id, minYear);
                    var y = PaddingY + (int)((yearOffset - minYear) * PxPerYear);

                    nodeMap[person.Id] = new LayoutNode(
                        person,
                        (int)(xOffset + componentXWidth / 2.0 + startX + xOffsets[i]),
                        y,
                        relDepth,
                        isFocus,
                        size);
                }
            }

            // Move the X offset to the right for the next component.
            xOffset += componentXWidth;
        }

        var canvasWidth = xOffset + PaddingX;

        // ─────────────────────────────────────────────────────────────────────
        // PHASE 4.5: Center lone ancestors over their children
        // ─────────────────────────────────────────────────────────────────────
        //
        // When a depth row contains exactly one person (e.g. a single known
        // grandfather), the generic loop places them at the component's horizontal
        // midpoint — which looks correct for a balanced tree but feels wrong when
        // the component's widest row is a couple (so the midpoint falls between
        // the two partners, not above the actual child).
        //
        // Fix: after all positions are computed, slide any single-person depth row
        // so it sits above the average X of its direct children. Process bottom-up
        // (low depth → high depth) so that parent adjustments cascade correctly.
        {
            var byDepth = nodeMap.Values
                .GroupBy(n => n.Depth)
                .OrderBy(g => g.Key)   // ascending → descendants processed before ancestors
                .ToList();

            foreach (var depthGroup in byDepth)
            {
                var nodes = depthGroup.ToList();
                if (nodes.Count != 1) continue;

                var node = nodes[0];
                var childNodes = (node.Person.ChildIds ?? [])
                    .Where(nodeMap.ContainsKey)
                    .Select(id => nodeMap[id])
                    .ToList();

                if (!childNodes.Any()) continue;

                var targetX = (int)childNodes.Average(c => (double)c.X);
                nodeMap[node.Person.Id] = node with { X = targetX };
            }
        }

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
            years[person.Id] = person.BirthDate.Value.Year;

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

            var coupleUnits = BuildCoupleFamilyUnits(a, b, children);
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
    ///   • No children → just a U‑arc with a short stem
    ///   • One child   → U‑arc + stem + single drop
    ///   • Many        → U‑arc + stem + sibling span + drops
    /// </summary>
    private IEnumerable<FamilyUnit> BuildCoupleFamilyUnits(
        LayoutNode partnerA,
        LayoutNode partnerB,
        List<LayoutNode> children)
    {
        var families = new List<FamilyUnit>();

        var sharedY = Math.Max(partnerA.Y, partnerB.Y);
        var aBottomY = sharedY + partnerA.Size / 2.0;
        var bBottomY = sharedY + partnerB.Size / 2.0;
        var midX = (partnerA.X + partnerB.X) / 2.0;
        var peakY = Math.Min(aBottomY, bBottomY) - 22;
        var heartY = peakY + 3;

        var arc = new CoupleArc(
            partnerA.X, aBottomY,
            partnerB.X, bBottomY,
            peakY,
            midX,
            heartY);

        if (!children.Any())
        {
            // Couple with no children: simple U‑arc + short stem.
            var stem = new StemLine(midX, heartY, heartY);
            families.Add(new FamilyUnit(arc, stem, null, Array.Empty<ChildDrop>()));
            return families;
        }

        // Compute the Y position of the sibling span (just above the topmost child).
        var spanY = children.Min(c => c.Y - c.Size / 2.0) - 18;
        var stemLine = new StemLine(midX, heartY, spanY);

        if (children.Count == 1)
        {
            // Single child: no horizontal span needed, just a drop.
            var c = children[0];
            var drops = new List<ChildDrop>
            {
                new ChildDrop(c.X, c.Y - c.Size / 2.0)
            };

            families.Add(new FamilyUnit(arc, stemLine, null, drops));
            return families;
        }

        // Multiple children: build a horizontal span and drops.
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

        if (children.Count == 1)
        {
            // Single child: no span needed, just a drop.
            var c = children[0];
            var drops = new List<ChildDrop>
            {
                new ChildDrop(c.X, c.Y - c.Size / 2.0)
            };

            families.Add(new FamilyUnit(null, stem, null, drops));
            return families;
        }

        // Multiple children: build a span that includes the parent center.
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
    // ROW LAYOUT HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    private static List<PersonDto> SortSpousesAdjacent(List<PersonDto> members, HashSet<(Guid, Guid)> couplePairs)
    {
        var sorted = new List<PersonDto>(members.Count);
        var remaining = new List<PersonDto>(members);

        while (remaining.Count > 0)
        {
            var person = remaining[0];
            remaining.RemoveAt(0);
            sorted.Add(person);

            var spouse = remaining.FirstOrDefault(p => IsCouple(person, p, couplePairs));

            if (spouse != null)
            {
                remaining.Remove(spouse);
                sorted.Add(spouse);
            }
        }

        return sorted;
    }

    private static double[] ComputeRowOffsets(List<PersonDto> members, HashSet<(Guid, Guid)> couplePairs)
    {
        var offsets = new double[members.Count];
        for (int i = 1; i < members.Count; i++)
        {
            var areSpouses = IsCouple(members[i - 1], members[i], couplePairs);
            offsets[i] = offsets[i - 1] + (areSpouses ? NodeSpacingX + SpouseSpacingX : NodeSpacingX);
        }
        return offsets;
    }

    private static bool IsCouple(PersonDto a, PersonDto b, HashSet<(Guid, Guid)> couplePairs)
    {
        var lo = a.Id < b.Id ? a.Id : b.Id;
        var hi = a.Id < b.Id ? b.Id : a.Id;
        if (couplePairs.Contains((lo, hi))) return true;
        return (a.SpouseIds?.Contains(b.Id) ?? false) || (b.SpouseIds?.Contains(a.Id) ?? false);
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
        double HeartY);

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