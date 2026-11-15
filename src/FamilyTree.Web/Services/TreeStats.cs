using FamilyTree.Shared.DTOs.Person;

namespace FamilyTree.Web.Services;

/// <summary>Numbers about a whole tree that the Dashboard shows.</summary>
public static class TreeStats
{
    /// <summary>
    /// The length of the longest chain of parent to child links, counting the first person as 1.
    /// Each person's depth is one more than their deepest parent's, not the shortest route to them:
    /// a breadth-first walk from every person with no parents reached someone through a married-in
    /// parent first and counted the longer line down from the great-grandparents short.
    /// </summary>
    public static int ComputeGenerations(List<PersonDto> people)
    {
        if (people.Count == 0) return 0;

        var byId = people.ToDictionary(p => p.Id);
        var depth = new Dictionary<Guid, int>();
        var inProgress = new HashSet<Guid>();

        // A parent loop can't be saved (the write paths refuse it), but old data might hold one:
        // a person met again while still being worked out adds nothing, so the walk always ends.
        int DepthOf(Guid id)
        {
            if (depth.TryGetValue(id, out var known)) return known;
            if (!inProgress.Add(id)) return 0;

            var deepestParent = 0;
            foreach (var parentId in byId[id].ParentIds ?? [])
                if (byId.ContainsKey(parentId))
                    deepestParent = Math.Max(deepestParent, DepthOf(parentId));

            inProgress.Remove(id);
            return depth[id] = deepestParent + 1;
        }

        return people.Max(p => DepthOf(p.Id));
    }
}
