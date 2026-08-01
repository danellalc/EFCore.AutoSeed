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
    /// <exception cref="UnresolvableCycleException">
    /// The graph contains a cycle made entirely of required foreign keys.
    /// </exception>
    public IReadOnlyList<IEntityType> Resolve(IReadOnlyList<IEntityType> entityTypes, IReadOnlyList<GraphEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(entityTypes);
        ArgumentNullException.ThrowIfNull(edges);

        List<GraphEdge> activeEdges = [.. edges];

        while (true)
        {
            DependencyGraph graph = new(entityTypes, activeEdges);
            TopologicalSortResult result = graph.StableTopologicalSort();

            if (result.Unordered.Count == 0)
            {
                return result.Order;
            }

            HashSet<IEntityType> stuck = result.Unordered.ToHashSet();

            GraphEdge? breakable = activeEdges
                .Where(edge => !edge.ForeignKey.IsRequired && stuck.Contains(edge.Principal) && stuck.Contains(edge.Dependent))
                .OrderBy(edge => edge.Dependent.Name, StringComparer.Ordinal)
                .ThenBy(edge => edge.Principal.Name, StringComparer.Ordinal)
                .ThenBy(edge => string.Join(",", edge.ForeignKey.Properties.Select(property => property.Name)), StringComparer.Ordinal)
                .FirstOrDefault();

            if (breakable is null)
            {
                IReadOnlyList<string> cycleEntityNames = [.. result.Unordered.Select(entityType => entityType.Name)];
                throw new UnresolvableCycleException(cycleEntityNames);
            }

            activeEdges.Remove(breakable);
        }
    }
}
