using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Pipeline;

/// <summary>
/// The result of attempting a stable topological sort over a <see cref="DependencyGraph"/>.
/// </summary>
/// <param name="Order">
/// The entity types that could be ordered, from least to most dependent. Ties break on entity
/// type name, ordinal comparison, never on hash or discovery order.
/// </param>
/// <param name="Unordered">
/// The entity types that could not be ordered because they participate in a dependency cycle,
/// sorted by name. Empty when <paramref name="Order"/> contains every entity type.
/// </param>
public sealed record TopologicalSortResult(
    IReadOnlyList<IEntityType> Order,
    IReadOnlyList<IEntityType> Unordered);
