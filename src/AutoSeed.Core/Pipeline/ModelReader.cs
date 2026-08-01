using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Pipeline;

/// <summary>
/// Reads an EF Core <see cref="IModel"/> and determines which entity types can be seeded and how
/// they depend on each other.
/// </summary>
public sealed class ModelReader
{
    /// <summary>
    /// Reads <paramref name="model"/>, keeping every entity type that is not owned and has a
    /// primary key, and building a dependency edge for every foreign key between two of them.
    /// </summary>
    /// <param name="model">The finalized model of the <see cref="DbContext"/> to seed.</param>
    /// <returns>The seedable entity types, their dependencies, and what was excluded.</returns>
    public ModelReadResult Read(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        List<IEntityType> seedable = [];
        List<SkippedEntityType> skipped = [];

        foreach (IEntityType entityType in model.GetEntityTypes())
        {
            if (entityType.IsOwned())
            {
                continue;
            }

            if (entityType.FindPrimaryKey() is null)
            {
                skipped.Add(new SkippedEntityType(entityType.Name, "no primary key"));
                continue;
            }

            seedable.Add(entityType);
        }

        HashSet<IEntityType> seedableSet = [.. seedable];

        List<GraphEdge> edges = [];
        foreach (IEntityType entityType in seedable)
        {
            foreach (IForeignKey foreignKey in entityType.GetForeignKeys())
            {
                if (seedableSet.Contains(foreignKey.PrincipalEntityType))
                {
                    edges.Add(new GraphEdge(foreignKey.PrincipalEntityType, entityType, foreignKey));
                }
            }
        }

        return new ModelReadResult(
            [.. seedable.OrderBy(entityType => entityType.Name, StringComparer.Ordinal)],
            edges,
            [.. skipped.OrderBy(entry => entry.EntityTypeName, StringComparer.Ordinal)]);
    }
}
