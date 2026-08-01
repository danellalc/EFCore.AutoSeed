using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Pipeline;

/// <summary>
/// A directed graph of entity types connected by <see cref="GraphEdge"/> dependencies, with a
/// stable topological sort.
/// </summary>
public sealed class DependencyGraph
{
    private readonly IReadOnlyList<IEntityType> _entityTypes;
    private readonly IReadOnlyList<GraphEdge> _edges;

    /// <summary>
    /// Initializes a new instance of the <see cref="DependencyGraph"/> class.
    /// </summary>
    /// <param name="entityTypes">Every node in the graph.</param>
    /// <param name="edges">Every dependency between two of <paramref name="entityTypes"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="entityTypes"/> or <paramref name="edges"/> is <see langword="null"/>.</exception>
    public DependencyGraph(IReadOnlyList<IEntityType> entityTypes, IReadOnlyList<GraphEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(entityTypes);
        ArgumentNullException.ThrowIfNull(edges);
        _entityTypes = entityTypes;
        _edges = edges;
    }

    /// <summary>
    /// Computes a topological order over the graph using Kahn's algorithm. When more than one
    /// entity type is ready to be ordered at the same step, the one with the smallest name
    /// (ordinal comparison) is chosen, so the result is stable across runs regardless of
    /// discovery order.
    /// </summary>
    /// <returns>
    /// The order that could be computed, plus any entity types left out because they participate
    /// in a dependency cycle.
    /// </returns>
    public TopologicalSortResult StableTopologicalSort()
    {
        Dictionary<IEntityType, int> inDegree = _entityTypes.ToDictionary(entityType => entityType, _ => 0);
        Dictionary<IEntityType, List<IEntityType>> dependents = _entityTypes.ToDictionary(entityType => entityType, _ => new List<IEntityType>());

        foreach (GraphEdge edge in _edges)
        {
            inDegree[edge.Dependent]++;
            dependents[edge.Principal].Add(edge.Dependent);
        }

        SortedSet<IEntityType> ready = new(Comparer<IEntityType>.Create((left, right) => string.CompareOrdinal(left.Name, right.Name)));
        foreach (IEntityType entityType in _entityTypes)
        {
            if (inDegree[entityType] == 0)
            {
                ready.Add(entityType);
            }
        }

        List<IEntityType> order = new(_entityTypes.Count);

        while (ready.Count > 0)
        {
            IEntityType next = ready.First();
            ready.Remove(next);
            order.Add(next);

            foreach (IEntityType dependent in dependents[next])
            {
                inDegree[dependent]--;
                if (inDegree[dependent] == 0)
                {
                    ready.Add(dependent);
                }
            }
        }

        if (order.Count == _entityTypes.Count)
        {
            return new TopologicalSortResult(order, []);
        }

        IReadOnlyList<IEntityType> unordered = _entityTypes
            .Except(order)
            .OrderBy(entityType => entityType.Name, StringComparer.Ordinal)
            .ToArray();

        return new TopologicalSortResult(order, unordered);
    }
}
