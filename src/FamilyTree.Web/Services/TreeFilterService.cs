using FamilyTree.Shared.DTOs;
using FamilyTree.Shared.DTOs.Person;

namespace FamilyTree.Web.Services;

/// <summary>
/// Pure BFS filter that restricts the canvas to people and couples within
/// N relationship hops of a focus person. Used by the live canvas (Home.razor)
/// and the export dialog. MaxDegrees = 0 means "show all".
/// </summary>
public static class TreeFilterService
{
    public static List<PersonDto> WithinDegrees(
        IReadOnlyList<PersonDto> pool, Guid focusId, int maxDegrees)
    {
        if (maxDegrees <= 0) return [.. pool];

        var byId  = pool.ToDictionary(p => p.Id);
        var dist  = new Dictionary<Guid, int> { [focusId] = 0 };
        var queue = new Queue<Guid>();
        queue.Enqueue(focusId);

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!byId.TryGetValue(id, out var p)) continue;
            var d = dist[id];
            if (d >= maxDegrees) continue;

            var neighbors = (p.ParentIds       ?? [])
                .Concat(p.ChildIds        ?? [])
                .Concat(p.SpouseIds       ?? [])
                .Concat(p.FormerSpouseIds ?? [])
                .Concat(p.SiblingIds      ?? []);

            foreach (var nid in neighbors)
            {
                if (!dist.ContainsKey(nid))
                {
                    dist[nid] = d + 1;
                    queue.Enqueue(nid);
                }
            }
        }

        return pool.Where(p => dist.ContainsKey(p.Id)).ToList();
    }

    public static List<CoupleDto> FilterCouples(
        IReadOnlyList<CoupleDto> couples, IReadOnlyCollection<Guid> visibleIds)
    {
        return couples
            .Where(c => visibleIds.Contains(c.PersonAId) && visibleIds.Contains(c.PersonBId))
            .ToList();
    }
}
