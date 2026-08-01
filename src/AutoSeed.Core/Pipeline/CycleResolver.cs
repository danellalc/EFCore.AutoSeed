using EFCore.AutoSeed.Exceptions;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Pipeline;

/// <summary>
/// Resolves dependency cycles left over from <see cref="DependencyGraph.StableTopologicalSort"/>.
/// A cycle broken by at least one nullable foreign key can be ordered by deferring that foreign
/// key to a second pass; a cycle made entirely of required foreign keys cannot be ordered at all.
/// </summary>
public sealed class CycleResolver
{
    /// <summary>
    /// Computes an insertion order for <paramref name="entityTypes"/>, deferring one nullable
    /// foreign key at a time out of every cycle it finds until the graph is acyclic.
    /// </summary>
    /// <param name="entityTypes">Every entity type to order.</param>
    /// <param name="edges">Every dependency between two of <paramref name="entityTypes"/>.</param>
    /// <returns>The full insertion order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entityTypes"/> or <paramref name="edges"/> is <see langword="null"/>.</exception>
    /// <exception cref="UnresolvableCycleException">
    /// The graph contains one or more cycles made entirely of required foreign keys.
    /// </exception>
    public IReadOnlyList<IEntityType> Resolve(IReadOnlyList<IEntityType> entityTypes, IReadOnlyList<GraphEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(entityTypes);
        ArgumentNullException.ThrowIfNull(edges);

        List<GraphEdge> activeEdges = [.. edges];

        while (true)
        {
            DependencyGraph graph = new(entityTypes, activeEdges);
            TopologicalSortResult sortResult = graph.StableTopologicalSort();

            if (sortResult.Unordered.Count == 0)
            {
                return sortResult.Order;
            }

            HashSet<IEntityType> stuck = [.. sortResult.Unordered];

            GraphEdge? breakable = activeEdges
                .Where(edge => !edge.ForeignKey.IsRequired && stuck.Contains(edge.Principal) && stuck.Contains(edge.Dependent))
                .OrderBy(edge => edge.Dependent.Name, StringComparer.Ordinal)
                .ThenBy(edge => edge.Principal.Name, StringComparer.Ordinal)
                .ThenBy(edge => string.Join(",", edge.ForeignKey.Properties.Select(property => property.Name)), StringComparer.Ordinal)
                .FirstOrDefault();

            if (breakable is not null)
            {
                activeEdges.Remove(breakable);
                continue;
            }

            throw BuildUnresolvableCycleException(stuck, activeEdges);
        }
    }

    private static UnresolvableCycleException BuildUnresolvableCycleException(
        HashSet<IEntityType> stuck, IReadOnlyList<GraphEdge> activeEdges)
    {
        IReadOnlyList<GraphEdge> edgesWithinStuck = [.. activeEdges
            .Where(edge => stuck.Contains(edge.Principal) && stuck.Contains(edge.Dependent))];

        HashSet<IEntityType> cyclicEntityTypes = FindCyclicEntityTypes(stuck, edgesWithinStuck);

        List<IReadOnlyList<string>> cycles = [.. FindConnectedComponents(cyclicEntityTypes, edgesWithinStuck)
            .Select(component => FindCyclePath(component, edgesWithinStuck))
            .OrderBy(cycle => cycle[0], StringComparer.Ordinal)];

        return new UnresolvableCycleException(cycles);
    }

    private static HashSet<IEntityType> FindCyclicEntityTypes(HashSet<IEntityType> stuck, IReadOnlyList<GraphEdge> edges)
    {
        HashSet<IEntityType> remaining = [.. stuck];

        bool removedAny = true;
        while (removedAny)
        {
            removedAny = false;

            Dictionary<IEntityType, int> outDegree = remaining.ToDictionary(entityType => entityType, _ => 0);
            Dictionary<IEntityType, int> inDegree = remaining.ToDictionary(entityType => entityType, _ => 0);

            foreach (GraphEdge edge in edges)
            {
                if (remaining.Contains(edge.Principal) && remaining.Contains(edge.Dependent))
                {
                    outDegree[edge.Principal]++;
                    inDegree[edge.Dependent]++;
                }
            }

            foreach (IEntityType entityType in remaining.ToArray())
            {
                if (outDegree[entityType] == 0 || inDegree[entityType] == 0)
                {
                    remaining.Remove(entityType);
                    removedAny = true;
                }
            }
        }

        return remaining;
    }

    private static IReadOnlyList<HashSet<IEntityType>> FindConnectedComponents(
        HashSet<IEntityType> cyclicEntityTypes, IReadOnlyList<GraphEdge> edges)
    {
        Dictionary<IEntityType, List<IEntityType>> neighbors = cyclicEntityTypes.ToDictionary(entityType => entityType, _ => new List<IEntityType>());
        foreach (GraphEdge edge in edges)
        {
            if (cyclicEntityTypes.Contains(edge.Principal) && cyclicEntityTypes.Contains(edge.Dependent))
            {
                neighbors[edge.Principal].Add(edge.Dependent);
                neighbors[edge.Dependent].Add(edge.Principal);
            }
        }

        HashSet<IEntityType> visited = [];
        List<HashSet<IEntityType>> components = [];

        foreach (IEntityType start in cyclicEntityTypes.OrderBy(entityType => entityType.Name, StringComparer.Ordinal))
        {
            if (!visited.Add(start))
            {
                continue;
            }

            HashSet<IEntityType> component = [start];
            Queue<IEntityType> queue = new();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                IEntityType current = queue.Dequeue();
                foreach (IEntityType neighbor in neighbors[current])
                {
                    if (visited.Add(neighbor))
                    {
                        component.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }

            components.Add(component);
        }

        return components;
    }

    private static IReadOnlyList<string> FindCyclePath(HashSet<IEntityType> component, IReadOnlyList<GraphEdge> edges)
    {
        ILookup<IEntityType, IEntityType> next = edges
            .Where(edge => component.Contains(edge.Principal) && component.Contains(edge.Dependent))
            .ToLookup(edge => edge.Principal, edge => edge.Dependent);

        IEntityType current = component.OrderBy(entityType => entityType.Name, StringComparer.Ordinal).First();
        List<IEntityType> path = [current];
        Dictionary<IEntityType, int> firstSeenAt = new() { [current] = 0 };

        while (true)
        {
            current = next[current].OrderBy(entityType => entityType.Name, StringComparer.Ordinal).First();
            if (firstSeenAt.TryGetValue(current, out int index))
            {
                return [.. path.Skip(index).Select(entityType => entityType.Name), current.Name];
            }

            firstSeenAt[current] = path.Count;
            path.Add(current);
        }
    }
}
