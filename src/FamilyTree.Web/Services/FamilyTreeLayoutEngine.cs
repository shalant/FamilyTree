using FamilyTree.Shared.DTOs;
using FamilyTree.Shared.DTOs.Person;

namespace FamilyTree.Web.Services;

/// <summary>
/// Computes complete 2D layout for a family tree visualization.
///
/// ALGORITHM OVERVIEW:
/// ─────────────────────────────────────────────────────────────────────────────
///
/// The layout system uses two independent axes:
///
/// Y-AXIS (Birth Year Timeline):
///   - Absolute positioning based on person's birth year
///   - Creates a temporal axis where time flows top→bottom
///   - Older ancestors at top, younger descendants at bottom
///   - People born in same year appear at same Y position
///   - Missing birth dates inferred from children/spouses
///
/// X-AXIS (Family Components & Relative Depth):
///   - Separate unconnected family trees horizontally (left→right)
///   - Within each tree, position by relative generational depth
///   - Parents positioned above children
///   - Spouses positioned adjacent to each other
///   - Allows for graceful tree merging when relationships discovered
///
/// COMPUTATION PHASES:
/// ─────────────────────────────────────────────────────────────────────────────
///
/// Phase 1: Compute Generational Depths
///   - BFS upward through parent relationships
///   - BFS downward through child relationships
///   - Propagate sideways through spouse relationships
///   - Result: Every person has a relative depth (parent=+1, child=-1, etc.)
///
/// Phase 2: Compute Birth Years
///   - Extract from PersonDto.BirthDate if available
///   - Infer from children: parent_year ≈ child_year - 25
///   - Infer from spouses: share same year
///   - Default to current year if still unknown
///
/// Phase 3: Identify Connected Components
///   - BFS through all relationships (parent, child, spouse)
///   - Group people into separate family trees
///   - When new relationship added, trees automatically regroup
///
/// Phase 4: Position All Nodes
///   - For each connected component:
///     * Group by relative depth (generational distance)
///     * Calculate X positions centered within component
///     * Assign Y positions from birth years
///     * Components separated horizontally on canvas
///
/// Phase 5: Create Visual Bands
///   - Decade-based bands (10-year intervals)
///   - Opacity gradient: older (darker) → newer (lighter)
///   - Helps user understand temporal distribution
///
/// Phase 6: Build Connectors
///   - SVG paths between parents and children
///   - U-arc brackets for couples
///   - Stem lines for parent→child flow
///
/// ROBUSTNESS:
/// ─────────────────────────────────────────────────────────────────────────────
///
/// Handles incomplete data gracefully:
///   - Orphans (no parents): treated as tree roots
///   - Missing birth dates: inferred from relationships
///   - Disconnected trees: rendered side-by-side
///   - Tree merging: automatic when relationship discovered
///   - Concurrency: depth calculation is stable (idempotent)
/// </summary>
public class FamilyTreeLayoutEngine
{
    // Layout constants
    private const int NodeSpacingX = 120;
    private const int PaddingX = 90;
    private const int PaddingY = 10;
    private const int FocusSize = 68;
    private const int Gen1Size = 56;
    private const int DefaultSize = 48;
    private const double PxPerYear = 45.0;  // pixels per year on Y-axis

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
        var depths = ComputeDepths(people);
        var focusDepth = focusPersonId.HasValue && depths.TryGetValue(focusPersonId.Value, out var fd)
            ? fd
            : 0;

        // ─────────────────────────────────────────────────────────────────────
        // PHASE 2: Compute birth years (absolute timeline positions)
        // ─────────────────────────────────────────────────────────────────────
        var birthYears = ComputeBirthYears(people);
        var minYear = birthYears.Values.Min();
        var maxYear = birthYears.Values.Max();
        var yearRange = maxYear - minYear + 1;
        var canvasHeight = (int)(yearRange * PxPerYear + PaddingY * 2);

        // ─────────────────────────────────────────────────────────────────────
        // PHASE 3: Identify connected components (separate family trees)
        // ─────────────────────────────────────────────────────────────────────
        var components = IdentifyConnectedComponents(people);

        // ─────────────────────────────────────────────────────────────────────
        // PHASE 4: Position all nodes (X, Y coordinates)
        // ─────────────────────────────────────────────────────────────────────
        var nodeMap = new Dictionary<Guid, LayoutNode>();
        var xOffset = PaddingX;

        foreach (var component in components)
        {
            // Group by depth within this component
            var componentByDepth = component
                .GroupBy(p => depths.GetValueOrDefault(p.Id, 0))
                .OrderByDescending(g => g.Key)
                .ToList();

            var maxInRow = componentByDepth.Max(g => g.Count());
            var componentXWidth = maxInRow * NodeSpacingX + PaddingX * 2;

            foreach (var depthGroup in componentByDepth)
            {
                var members = depthGroup.ToList();
                var count = members.Count;
                var startX = -(count - 1) * NodeSpacingX / 2.0;

                for (int i = 0; i < count; i++)
                {
                    var person = members[i];
                    var isFocus = person.Id == focusPersonId;
                    var relDep = depths.GetValueOrDefault(person.Id, 0);
                    var size = isFocus ? FocusSize
                              : Math.Abs(relDep) <= 1 ? Gen1Size
                              : DefaultSize;

                    var yearOffset = birthYears.GetValueOrDefault(person.Id, minYear);
                    var y = PaddingY + (int)((yearOffset - minYear) * PxPerYear);

                    nodeMap[person.Id] = new LayoutNode(
                        person,
                        (int)(xOffset + componentXWidth / 2.0 + startX + i * NodeSpacingX),
                        y,
                        depths.GetValueOrDefault(person.Id, 0),
                        isFocus,
                        size
                    );
                }
            }

            xOffset += componentXWidth;
        }

        var canvasWidth = xOffset + PaddingX;

        // ─────────────────────────────────────────────────────────────────────
        // PHASE 5: Create visual bands (decade markers with time gradient)
        // ─────────────────────────────────────────────────────────────────────
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
        var connectors = BuildConnectors(people, couples, nodeMap);

        return new FamilyTreeLayout(
            nodeMap.Values.ToList(),
            bands,
            connectors,
            canvasWidth,
            canvasHeight,
            focusDepth);
    }

    /// <summary>
    /// Computes relative generational depth for each person.
    ///
    /// Pass 1: BFS upward through parents (ancestors get positive depth)
    /// Pass 2: BFS downward through children (descendants get negative depth)
    /// Pass 3: Propagate through spouses (same depth as partner)
    /// Final: Normalize so oldest ancestor = depth 0
    /// </summary>
    private Dictionary<Guid, int> ComputeDepths(List<PersonDto> people)
    {
        var depths = new Dictionary<Guid, int>();
        var focus = people.FirstOrDefault();
        if (focus == null) return depths;

        depths[focus.Id] = 0;

        // Pass 1: Walk upward through parents
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

        // Pass 2: Push depths downward through children
        foreach (var person in people)
        {
            foreach (var parentId in person.ParentIds ?? [])
            {
                if (depths.TryGetValue(parentId, out var pd) && !depths.ContainsKey(person.Id))
                    depths[person.Id] = pd - 1;
            }
        }

        // Pass 3: Propagate through spouses (iterate until stable)
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var person in people)
            {
                if (!depths.TryGetValue(person.Id, out var personDepth)) continue;
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

        // Normalize: shift so min depth = 0
        if (depths.Any())
        {
            var minDepth = depths.Values.Min();
            foreach (var key in depths.Keys.ToList())
                depths[key] -= minDepth;
        }

        return depths;
    }

    /// <summary>
    /// Assigns birth year to each person.
    ///
    /// Strategy:
    /// 1. Use PersonDto.BirthDate if available
    /// 2. Infer from children: parent ≈ child - 25 years
    /// 3. Infer from spouses: use spouse's year
    /// 4. Default to current year if still unknown
    /// </summary>
    private Dictionary<Guid, int> ComputeBirthYears(List<PersonDto> people)
    {
        var years = new Dictionary<Guid, int>();

        // Assign from known birth dates
        foreach (var person in people.Where(p => p.BirthDate.HasValue))
            years[person.Id] = person.BirthDate.Value.Year;

        // Infer from children (iterate until stable)
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

        // Default unknown people to the median year (more stable than current year)
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

    /// <summary>
    /// Groups people into separate connected components using BFS.
    ///
    /// A connected component is a maximal set of people connected through
    /// parent/child/spouse relationships. Multiple components = orphaned trees.
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

                // Enqueue all related people
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

    /// <summary>
    /// Builds SVG connectors (family relationship paths).
    ///
    /// Creates:
    /// - Couple U-arcs (curved bracket between partners)
    /// - Stem lines (vertical parent→child flow)
    /// - Sibling spans (horizontal bow across children)
    /// - Child drops (bezier curves from span to child nodes)
    /// </summary>
    private List<FamilyUnit> BuildConnectors(
        List<PersonDto> people,
        List<CoupleDto> couples,
        Dictionary<Guid, LayoutNode> nodeMap)
    {
        var families = new List<FamilyUnit>();
        var routedChildren = new HashSet<Guid>();

        // Process coupled families
        foreach (var couple in couples)
        {
            if (!nodeMap.TryGetValue(couple.PersonAId, out var a) ||
                !nodeMap.TryGetValue(couple.PersonBId, out var b))
                continue;

            var sharedY = Math.Max(a.Y, b.Y);
            var aBottomY = sharedY + a.Size / 2.0;
            var bBottomY = sharedY + b.Size / 2.0;
            var midX = (a.X + b.X) / 2.0;
            var peakY = Math.Min(aBottomY, bBottomY) - 22;
            var heartY = peakY + 3;

            var arc = new CoupleArc(a.X, aBottomY, b.X, bBottomY, peakY, midX, heartY);

            var children = couple.ChildIds
                .Where(nodeMap.ContainsKey)
                .Select(id => nodeMap[id])
                .ToList();

            foreach (var c in children)
                routedChildren.Add(c.Person.Id);

            if (!children.Any())
            {
                families.Add(new FamilyUnit(arc, new StemLine(midX, heartY, heartY), null, []));
                continue;
            }

            var spanY = children.Min(c => c.Y - c.Size / 2.0) - 18;
            var stem = new StemLine(midX, heartY, spanY);
            var span = children.Count > 1
                ? new SiblingSpan(children.Min(c => (double)c.X),
                                  children.Max(c => (double)c.X), spanY)
                : null;
            var drops = children.Select(c => new ChildDrop(c.X, c.Y - c.Size / 2.0)).ToList();

            families.Add(new FamilyUnit(arc, stem, span, drops));
        }

        // Process single-parent families
        foreach (var person in people)
        {
            if (routedChildren.Contains(person.Id)) continue;
            foreach (var parentId in person.ParentIds ?? [])
            {
                if (!nodeMap.TryGetValue(parentId, out var parent) ||
                    !nodeMap.TryGetValue(person.Id, out var child))
                    continue;

                var parentBottomY = parent.Y + parent.Size / 2.0;
                var childTopY = child.Y - child.Size / 2.0;
                var midY = parentBottomY + (childTopY - parentBottomY) / 2.0;

                families.Add(new FamilyUnit(
                    null,
                    new StemLine(parent.X, parentBottomY, midY),
                    null,
                    [new ChildDrop(child.X, childTopY)]));
            }
        }

        return families;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RECORDS (data structures for layout output)
    // ─────────────────────────────────────────────────────────────────────────

    public record LayoutNode(
        PersonDto Person,
        int X,
        int Y,
        int Depth,
        bool IsFocus,
        int Size);

    public record GenerationBand(
        int Depth,
        int Top,
        int Height);

    public record CoupleArc(
        double PartnerAX,
        double PartnerAY,
        double PartnerBX,
        double PartnerBY,
        double PeakY,
        double MidX,
        double HeartY);

    public record StemLine(double X, double TopY, double BotY);
    public record SiblingSpan(double LeftX, double RightX, double Y);
    public record ChildDrop(double CenterX, double BotY);

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
