using EFCore.AutoSeed.Exceptions;
using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Providers;

/// <summary>
/// Inserts a generation plan into a <see cref="DbContext"/> in fast mode: rows are generated and
/// FK-resolved exactly like <see cref="Persistence"/>, but written through an
/// <see cref="IBulkInsertProvider"/> instead of <see cref="DbContext.SaveChangesAsync(System.Threading.CancellationToken)"/>,
/// bypassing the change tracker entirely. Only entity types simple enough to make that safe are
/// supported; anything else is rejected up front rather than risking silently wrong data.
/// </summary>
public sealed class BulkPersistence
{
    private readonly IBulkInsertProvider _provider;
    private readonly UniquenessEnforcer _uniquenessEnforcer;

    /// <summary>
    /// Initializes a new instance of the <see cref="BulkPersistence"/> class.
    /// </summary>
    /// <param name="provider">The bulk insert provider to write through.</param>
    /// <param name="uniquenessEnforcer">
    /// The enforcer used to fix up duplicate values for unique properties before each entity type
    /// is inserted. Defaults to a new <see cref="Pipeline.UniquenessEnforcer"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="provider"/> is <see langword="null"/>.</exception>
    public BulkPersistence(IBulkInsertProvider provider, UniquenessEnforcer? uniquenessEnforcer = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
        _uniquenessEnforcer = uniquenessEnforcer ?? new UniquenessEnforcer();
    }

    /// <summary>
    /// Generates and bulk-inserts every row in <paramref name="plan"/>.
    /// </summary>
    /// <param name="context">The context to insert into.</param>
    /// <param name="plan">How many rows to generate for each entity type, in dependency order.</param>
    /// <param name="edges">Every dependency between two entity types in <paramref name="plan"/>, required or deferred.</param>
    /// <param name="deferredEdges">Foreign keys that would need a cycle-breaking second pass; not supported in fast mode.</param>
    /// <param name="generateRow">Produces the property values for one row of one entity type.</param>
    /// <param name="rootRandom">The random source every row and every uniqueness fix-up derives from.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number of rows inserted, keyed by entity type name.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <exception cref="UnsupportedEntityTypeException">
    /// The model needs a cycle-breaking second pass, declares an inherited or owned entity type,
    /// or a single-column identity primary key of a type fast mode does not assign itself.
    /// </exception>
    public async Task<IReadOnlyDictionary<string, int>> InsertAsync(
        DbContext context,
        IReadOnlyList<EntityGenerationPlan> plan,
        IReadOnlyList<GraphEdge> edges,
        IReadOnlyList<GraphEdge> deferredEdges,
        Func<IEntityType, SeededRandom, Dictionary<string, object>> generateRow,
        SeededRandom rootRandom,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(deferredEdges);
        ArgumentNullException.ThrowIfNull(generateRow);
        ArgumentNullException.ThrowIfNull(rootRandom);

        if (deferredEdges.Count > 0)
        {
            throw new UnsupportedEntityTypeException(
                deferredEdges[0].Dependent.Name,
                "fast mode does not support foreign key cycles yet; use AutoSeedAsync instead");
        }

        foreach (EntityGenerationPlan entityPlan in plan)
        {
            RejectIfUnsupported(entityPlan.EntityType);
        }

        IReadOnlyList<GraphEdge> requiredEdges = [.. edges.Where(edge => edge.ForeignKey.IsRequired)];
        Dictionary<IEntityType, List<Dictionary<string, object>>> rowsByEntityType = [];
        Dictionary<IEntityType, int> nextIdentityValue = [];

        foreach (EntityGenerationPlan entityPlan in plan)
        {
            List<Dictionary<string, object>> rows = await InsertEntityTypeAsync(
                    context, entityPlan, requiredEdges, rowsByEntityType, nextIdentityValue, generateRow, rootRandom, cancellationToken)
                .ConfigureAwait(false);
            rowsByEntityType[entityPlan.EntityType] = rows;
        }

        return plan.ToDictionary(entry => entry.EntityType.Name, entry => entry.RowCount);
    }

    private async Task<List<Dictionary<string, object>>> InsertEntityTypeAsync(
        DbContext context,
        EntityGenerationPlan entityPlan,
        IReadOnlyList<GraphEdge> requiredEdges,
        Dictionary<IEntityType, List<Dictionary<string, object>>> rowsByEntityType,
        Dictionary<IEntityType, int> nextIdentityValue,
        Func<IEntityType, SeededRandom, Dictionary<string, object>> generateRow,
        SeededRandom rootRandom,
        CancellationToken cancellationToken)
    {
        IEntityType entityType = entityPlan.EntityType;
        SeededRandom entityRandom = rootRandom.Derive(entityType.Name);

        List<Dictionary<string, object>> rows = new(entityPlan.RowCount);
        for (int rowIndex = 0; rowIndex < entityPlan.RowCount; rowIndex++)
        {
            rows.Add(generateRow(entityType, entityRandom.Derive(rowIndex)));
        }

        _uniquenessEnforcer.EnsureUnique(entityType, rows, entityRandom);
        AssignIdentityPrimaryKey(entityType, rows, nextIdentityValue);

        int[]? driverRowIndices = entityPlan.ChildCountsByDriverRow is null
            ? null
            : ExpandDriverRowIndices(entityPlan.ChildCountsByDriverRow);

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            AssignRequiredForeignKeys(entityType, requiredEdges, entityPlan, rows[rowIndex], rowIndex, driverRowIndices, rowsByEntityType);
        }

        await _provider.InsertAsync(context, entityType, rows, cancellationToken).ConfigureAwait(false);
        return rows;
    }

    private static void RejectIfUnsupported(IEntityType entityType)
    {
        if (entityType.BaseType is not null || entityType.GetDirectlyDerivedTypes().Any())
        {
            throw new UnsupportedEntityTypeException(
                entityType.Name, "fast mode does not support inherited entity types yet; use AutoSeedAsync instead");
        }

        if (GetOwnedReferenceNavigations(entityType).Any())
        {
            throw new UnsupportedEntityTypeException(
                entityType.Name, "fast mode does not support owned types yet; use AutoSeedAsync instead");
        }

        IKey? primaryKey = entityType.FindPrimaryKey();
        if (primaryKey is { Properties.Count: 1 } && primaryKey.Properties[0].ValueGenerated == ValueGenerated.OnAdd
            && primaryKey.Properties[0].ClrType != typeof(int))
        {
            throw new UnsupportedEntityTypeException(
                entityType.Name,
                $"fast mode only assigns int identity primary keys itself, not '{primaryKey.Properties[0].ClrType.Name}'; use AutoSeedAsync instead");
        }
    }

    private static IEnumerable<INavigation> GetOwnedReferenceNavigations(IEntityType entityType) =>
        entityType.GetNavigations()
            .Where(navigation => !navigation.IsCollection && navigation.ForeignKey.IsOwnership && navigation.ForeignKey.PrincipalEntityType == entityType);

    private static void AssignIdentityPrimaryKey(
        IEntityType entityType, List<Dictionary<string, object>> rows, Dictionary<IEntityType, int> nextIdentityValue)
    {
        IKey? primaryKey = entityType.FindPrimaryKey();
        if (primaryKey is not { Properties.Count: 1 } || primaryKey.Properties[0].ValueGenerated != ValueGenerated.OnAdd)
        {
            return;
        }

        string keyPropertyName = primaryKey.Properties[0].Name;
        int next = nextIdentityValue.TryGetValue(entityType, out int value) ? value : 1;

        foreach (Dictionary<string, object> row in rows)
        {
            row[keyPropertyName] = next;
            next++;
        }

        nextIdentityValue[entityType] = next;
    }

    private static void AssignRequiredForeignKeys(
        IEntityType entityType,
        IReadOnlyList<GraphEdge> requiredEdges,
        EntityGenerationPlan entityPlan,
        Dictionary<string, object> row,
        int rowIndex,
        int[]? driverRowIndices,
        Dictionary<IEntityType, List<Dictionary<string, object>>> rowsByEntityType)
    {
        foreach (GraphEdge edge in requiredEdges)
        {
            if (edge.Dependent != entityType)
            {
                continue;
            }

            List<Dictionary<string, object>> principalRows = rowsByEntityType[edge.Principal];
            if (principalRows.Count == 0)
            {
                throw new UnsupportedEntityTypeException(
                    entityType.Name, $"required principal '{edge.Principal.Name}' has zero generated rows");
            }

            Dictionary<string, object> principalRow = edge.Principal == entityPlan.Driver && driverRowIndices is not null
                ? principalRows[driverRowIndices[rowIndex]]
                : principalRows[rowIndex % principalRows.Count];

            IReadOnlyList<IProperty> dependentProperties = edge.ForeignKey.Properties;
            IReadOnlyList<IProperty> principalProperties = edge.ForeignKey.PrincipalKey.Properties;

            for (int index = 0; index < dependentProperties.Count; index++)
            {
                row[dependentProperties[index].Name] = principalRow[principalProperties[index].Name];
            }
        }
    }

    private static int[] ExpandDriverRowIndices(IReadOnlyList<int> childCountsByDriverRow)
    {
        int[] driverRowIndices = new int[childCountsByDriverRow.Sum()];
        int cursor = 0;

        for (int driverRowIndex = 0; driverRowIndex < childCountsByDriverRow.Count; driverRowIndex++)
        {
            for (int repeat = 0; repeat < childCountsByDriverRow[driverRowIndex]; repeat++)
            {
                driverRowIndices[cursor] = driverRowIndex;
                cursor++;
            }
        }

        return driverRowIndices;
    }
}
