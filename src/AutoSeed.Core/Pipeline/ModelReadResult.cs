using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Pipeline;

/// <summary>
/// The entity types <see cref="ModelReader"/> found in a model, split between what can be seeded
/// and what was excluded.
/// </summary>
/// <param name="EntityTypes">The entity types that can be seeded, sorted by name.</param>
/// <param name="Edges">Every foreign key dependency between two of <paramref name="EntityTypes"/>.</param>
/// <param name="SkippedEntityTypes">The entity types found in the model but excluded from seeding, sorted by name.</param>
public sealed record ModelReadResult(
    IReadOnlyList<IEntityType> EntityTypes,
    IReadOnlyList<GraphEdge> Edges,
    IReadOnlyList<SkippedEntityType> SkippedEntityTypes);
