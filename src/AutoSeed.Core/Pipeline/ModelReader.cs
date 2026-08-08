using EFCore.AutoSeed.Exceptions;
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
    /// Reads <paramref name="model"/>, keeping every entity type that is not owned, not an implicit
    /// many-to-many join table, not abstract, and has a primary key, and building a dependency edge
    /// for every foreign key between two of them that is not itself an inheritance table-splitting
    /// link.
    /// </summary>
    /// <param name="model">The finalized model of the <see cref="DbContext"/> to seed.</param>
    /// <returns>The seedable entity types, their dependencies, and what was excluded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is <see langword="null"/>.</exception>
    /// <exception cref="UnsupportedEntityTypeException">
    /// An entity type has a required foreign key whose principal entity type was excluded from
    /// seeding (for example an abstract type or one with no primary key), so the foreign key could
    /// never be satisfied.
    /// </exception>
    public ModelReadResult Read(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        List<IEntityType> seedable = [];
        List<SkippedEntityType> skipped = [];

        HashSet<IEntityType> manyToManyJoinEntityTypes = [.. model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetSkipNavigations())
            .Select(skipNavigation => skipNavigation.JoinEntityType)];

        foreach (IEntityType entityType in model.GetEntityTypes())
        {
            if (entityType.IsOwned())
            {
                continue;
            }

            if (manyToManyJoinEntityTypes.Contains(entityType))
            {
                skipped.Add(new SkippedEntityType(entityType.Name, "implicit many-to-many join table, not populated yet"));
                continue;
            }

            if (entityType.ClrType.IsAbstract)
            {
                skipped.Add(new SkippedEntityType(entityType.Name, "abstract type, cannot be instantiated"));
                continue;
            }

            if (entityType.FindPrimaryKey() is null)
            {
                skipped.Add(new SkippedEntityType(entityType.Name, "no primary key"));
                continue;
            }

            seedable.Add(entityType);
        }

        foreach (INavigation navigation in CollectOwnedCollectionNavigations(seedable))
        {
            skipped.Add(new SkippedEntityType(
                navigation.TargetEntityType.Name,
                $"owned collection navigation '{navigation.DeclaringEntityType.ShortName()}.{navigation.Name}', not populated yet"));
        }

        HashSet<IEntityType> seedableSet = [.. seedable];

        List<GraphEdge> edges = [];
        foreach (IEntityType entityType in seedable)
        {
            foreach (IForeignKey foreignKey in entityType.GetForeignKeys())
            {
                if (IsInheritanceLinkingForeignKey(entityType, foreignKey))
                {
                    continue;
                }

                if (seedableSet.Contains(foreignKey.PrincipalEntityType))
                {
                    edges.Add(new GraphEdge(foreignKey.PrincipalEntityType, entityType, foreignKey));
                    continue;
                }

                if (foreignKey.IsRequired)
                {
                    string principalReason = skipped
                        .FirstOrDefault(entry => entry.EntityTypeName == foreignKey.PrincipalEntityType.Name)
                        ?.Reason ?? "owned type, cannot be seeded independently";
                    string propertyNames = string.Join(", ", foreignKey.Properties.Select(property => property.Name));

                    throw new UnsupportedEntityTypeException(
                        entityType.Name,
                        $"its required foreign key '{propertyNames}' references '{foreignKey.PrincipalEntityType.Name}', which cannot be seeded ({principalReason})");
                }
            }
        }

        return new ModelReadResult(
            [.. seedable.OrderBy(entityType => entityType.Name, StringComparer.Ordinal)],
            edges,
            [.. skipped.OrderBy(entry => entry.EntityTypeName, StringComparer.Ordinal)]);
    }

    /// <summary>
    /// Walks every owned navigation reachable from <paramref name="entityTypes"/>, recursing through
    /// owned reference navigations (a nested owned type can itself own a collection), and yields
    /// every owned collection navigation found: value generation never populates one, so callers
    /// record it as skipped instead of leaving it silently empty.
    /// </summary>
    private static IEnumerable<INavigation> CollectOwnedCollectionNavigations(IEnumerable<IEntityType> entityTypes)
    {
        foreach (IEntityType entityType in entityTypes)
        {
            foreach (INavigation navigation in CollectOwnedCollectionNavigations(entityType))
            {
                yield return navigation;
            }
        }
    }

    private static IEnumerable<INavigation> CollectOwnedCollectionNavigations(IEntityType entityType)
    {
        foreach (INavigation navigation in entityType.GetNavigations().Where(navigation => navigation.ForeignKey.IsOwnership))
        {
            if (navigation.IsCollection)
            {
                yield return navigation;
                continue;
            }

            foreach (INavigation nested in CollectOwnedCollectionNavigations(navigation.TargetEntityType))
            {
                yield return nested;
            }
        }
    }

    /// <summary>
    /// A table-per-type entity type declares a foreign key to its own base type: SQL Server and
    /// PostgreSQL need it to join the tables back together, but it is not a dependency between two
    /// independent rows the way an ordinary required foreign key is. EF Core's own
    /// <see cref="Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(System.Threading.CancellationToken)"/>
    /// already assigns the same generated key to both tables' rows for a single inserted instance;
    /// treating this as a real edge would make the base type's row a second, independently
    /// generated one, which then collides with the shared key EF Core assigns.
    /// </summary>
    private static bool IsInheritanceLinkingForeignKey(IEntityType entityType, IForeignKey foreignKey)
    {
        for (IEntityType? baseType = entityType.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (foreignKey.PrincipalEntityType == baseType)
            {
                return true;
            }
        }

        return false;
    }
}
