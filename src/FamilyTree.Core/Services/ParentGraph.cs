namespace FamilyTree.Core.Services;

/// <summary>
/// The "is a parent of" graph of a family tree, used to keep it free of loops.
///
/// A loop (someone recorded as their own ancestor) is never valid genealogy, and the
/// layout engine recurses down the parent -> child chain, so one in the data once meant an
/// uncatchable StackOverflow that took the whole web process down (R0, docs/TodoList.md).
/// The write paths use <see cref="Ancestors"/> / <see cref="Descendants"/> to refuse a
/// link that would close a loop; <see cref="FindCycles"/> finds loops already in the data.
/// All walks are iterative and tolerate loops in the graph they are given.
/// </summary>
public sealed class ParentGraph
{
    private readonly Dictionary<Guid, List<Guid>> _childrenOf = new();
    private readonly Dictionary<Guid, List<Guid>> _parentsOf = new();
    private readonly HashSet<Guid> _nodes = [];

    public ParentGraph(IEnumerable<(Guid Parent, Guid Child)> edges)
    {
        foreach (var (parent, child) in edges) Add(parent, child);
    }

    public void Add(Guid parent, Guid child)
    {
        _nodes.Add(parent);
        _nodes.Add(child);
        Link(_childrenOf, parent, child);
        Link(_parentsOf, child, parent);
    }

    /// <summary>True if recording <paramref name="parent"/> as a parent of <paramref name="child"/>
    /// would close a loop: they are the same person, or <paramref name="parent"/> is already one
    /// of the child's descendants.</summary>
    public bool WouldCreateLoop(Guid parent, Guid child) =>
        parent == child || Descendants(child).Contains(parent);

    private static void Link(Dictionary<Guid, List<Guid>> map, Guid from, Guid to)
    {
        if (!map.TryGetValue(from, out var list)) map[from] = list = [];
        if (!list.Contains(to)) list.Add(to);
    }

    /// <summary>Everyone reachable by walking up from <paramref name="id"/>. Includes
    /// <paramref name="id"/> itself only if it sits on a loop.</summary>
    public HashSet<Guid> Ancestors(Guid id) => Reach(_parentsOf, id);

    /// <summary>Everyone reachable by walking down from <paramref name="id"/>. Includes
    /// <paramref name="id"/> itself only if it sits on a loop.</summary>
    public HashSet<Guid> Descendants(Guid id) => Reach(_childrenOf, id);

    /// <summary>Like <see cref="Ancestors"/>, with each one's fewest generations up
    /// (1 = parent), and <paramref name="id"/> itself at 0.</summary>
    public Dictionary<Guid, int> AncestorGenerations(Guid id) => Distances(_parentsOf, id);

    /// <summary>Like <see cref="Descendants"/>, with each one's fewest generations down
    /// (1 = child), and <paramref name="id"/> itself at 0.</summary>
    public Dictionary<Guid, int> DescendantGenerations(Guid id) => Distances(_childrenOf, id);

    private static Dictionary<Guid, int> Distances(Dictionary<Guid, List<Guid>> map, Guid start)
    {
        var dist = new Dictionary<Guid, int> { [start] = 0 };
        var queue = new Queue<Guid>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var at = queue.Dequeue();
            if (!map.TryGetValue(at, out var next)) continue;
            foreach (var n in next)
                if (dist.TryAdd(n, dist[at] + 1)) queue.Enqueue(n);
        }
        return dist;
    }

    private static HashSet<Guid> Reach(Dictionary<Guid, List<Guid>> map, Guid start)
    {
        var seen = new HashSet<Guid>();
        var stack = new Stack<Guid>();
        stack.Push(start);
        while (stack.Count > 0)
        {
            if (!map.TryGetValue(stack.Pop(), out var next)) continue;
            foreach (var n in next)
                if (seen.Add(n)) stack.Push(n);
        }
        return seen;
    }

    /// <summary>
    /// Every group of people caught in a loop: the strongly connected components with more
    /// than one member, plus anyone recorded as their own parent. Each group is sorted by id
    /// and the groups are sorted by their first id, so the output is deterministic.
    /// </summary>
    public List<List<Guid>> FindCycles()
    {
        // Iterative Tarjan: a pathological tree must not be able to overflow the stack here
        // either, the whole point being that this runs on data that may already be bad.
        var index = new Dictionary<Guid, int>();
        var low = new Dictionary<Guid, int>();
        var onStack = new HashSet<Guid>();
        var stack = new Stack<Guid>();
        var cycles = new List<List<Guid>>();
        var counter = 0;

        IEnumerator<Guid> ChildrenOf(Guid id) =>
            (_childrenOf.TryGetValue(id, out var list) ? list : (IEnumerable<Guid>)[]).GetEnumerator();

        foreach (var root in _nodes.OrderBy(n => n))
        {
            if (index.ContainsKey(root)) continue;

            var work = new Stack<(Guid Node, IEnumerator<Guid> Children)>();
            index[root] = low[root] = counter++;
            stack.Push(root);
            onStack.Add(root);
            work.Push((root, ChildrenOf(root)));

            while (work.Count > 0)
            {
                var (v, children) = work.Peek();
                if (children.MoveNext())
                {
                    var w = children.Current;
                    if (!index.ContainsKey(w))
                    {
                        index[w] = low[w] = counter++;
                        stack.Push(w);
                        onStack.Add(w);
                        work.Push((w, ChildrenOf(w)));
                    }
                    else if (onStack.Contains(w))
                    {
                        low[v] = Math.Min(low[v], index[w]);
                    }
                    continue;
                }

                work.Pop();
                if (work.Count > 0)
                {
                    var parent = work.Peek().Node;
                    low[parent] = Math.Min(low[parent], low[v]);
                }
                if (low[v] != index[v]) continue;

                var component = new List<Guid>();
                Guid member;
                do
                {
                    member = stack.Pop();
                    onStack.Remove(member);
                    component.Add(member);
                } while (member != v);

                var selfParent = component.Count == 1 && _childrenOf.TryGetValue(v, out var kids) && kids.Contains(v);
                if (component.Count > 1 || selfParent)
                {
                    component.Sort();
                    cycles.Add(component);
                }
            }
        }

        return [.. cycles.OrderBy(c => c[0])];
    }
}
