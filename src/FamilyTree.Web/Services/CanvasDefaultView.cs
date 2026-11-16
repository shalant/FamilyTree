namespace FamilyTree.Web.Services;

/// <summary>
/// What the tree canvas frames when it first opens (and on "Reset view"): the focus person, the row of
/// parents above and the row of children below, and the extent of the focus person's own immediate family.
/// The generation rows are what the canvas has always framed; the immediate family is added to them
/// (2026-11-15, P7) because the rows alone missed a parent in a tree where the layout does not put the
/// parent exactly one generation up, and said nothing about width, so a phone-width screen could not
/// fit the family across.
/// </summary>
public readonly record struct DefaultView(
    double FocusX, double FocusY,
    double? TopY, double? BotY,
    double? LeftX, double? RightX);

public static class CanvasDefaultView
{
    public static DefaultView Compute(FamilyTreeLayout layout)
    {
        var focusNode = layout.Nodes.FirstOrDefault(n => n.IsFocus);
        var fx = focusNode?.X ?? layout.CanvasWidth / 2.0;
        var fy = focusNode?.Y ?? layout.CanvasHeight / 2.0;

        var fd = layout.FocusDepth;
        var parentNodes = layout.Nodes.Where(n => n.Depth == fd + 1).ToList();
        var childNodes  = layout.Nodes.Where(n => n.Depth == fd - 1).ToList();

        double? genTopY = null;
        double? genBotY = null;
        if (parentNodes.Count > 0)
            genTopY = parentNodes.Min(n => (double)n.Y - n.Size / 2.0) - 30;
        else if (focusNode != null)
            genTopY = focusNode.Y - focusNode.Size / 2.0 - 30;

        if (childNodes.Count > 0)
            genBotY = childNodes.Max(n => (double)n.Y + n.Size / 2.0) + 30;
        else if (focusNode != null)
            genBotY = focusNode.Y + focusNode.Size / 2.0 + 30;

        // The focus person's own parents, partners and children, by relationship rather than by row.
        // The generation rows above can hold the whole tree's width (aunts, uncles, cousins), so only
        // these set the horizontal fit; they also widen the vertical band so none of them is cut off.
        double? left = null;
        double? right = null;
        if (focusNode != null)
        {
            var person = focusNode.Person;
            var family = new HashSet<Guid> { person.Id };
            foreach (var id in person.ParentIds ?? []) family.Add(id);
            foreach (var id in person.ChildIds ?? []) family.Add(id);
            foreach (var id in person.SpouseIds ?? []) family.Add(id);

            var nodes = layout.Nodes.Where(n => family.Contains(n.Person.Id)).ToList();
            if (nodes.Count > 0)
            {
                left = nodes.Min(n => n.X - n.Size / 2.0);
                right = nodes.Max(n => n.X + n.Size / 2.0);
                genTopY = Math.Min(genTopY ?? double.MaxValue, nodes.Min(n => n.Y - n.Size / 2.0) - 30);
                genBotY = Math.Max(genBotY ?? double.MinValue, nodes.Max(n => n.Y + n.Size / 2.0) + 30);
            }
        }

        return new DefaultView(fx, fy, genTopY, genBotY, left, right);
    }
}
