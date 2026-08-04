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
    private const string IdentityRandomScope = "__identity";

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
    /// The model needs a cycle-breaking second pass, declares an inherited entity type, or a
    /// single-column identity primary key of a type other than <see cref="int"/>, <see cref="long"/> or <see cref="Guid"/>.
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
        Dictionary<IEntityType, long> nextIdentityValue = [];

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
        Dictionary<IEntityType, long> nextIdentityValue,
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
        await AssignIdentityPrimaryKeyAsync(context, entityType, rows, entityRandom, nextIdentityValue, cancellationToken).ConfigureAwait(false);

        int[]? driverRowIndices = entityPlan.ChildCountsByDriverRow is null
            ? null
            : ExpandDriverRowIndices(entityPlan.ChildCountsByDriverRow);

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            AssignRequiredForeignKeys(entityType, requiredEdges, entityPlan, rows[rowIndex], rowIndex, driverRowIndices, rowsByEntityType);
        }

        List<Dictionary<string, object>> columnKeyedRows = new(rows.Count);
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            columnKeyedRows.Add(BuildColumnKeyedRow(entityType, rows[rowIndex], entityRandom.Derive(rowIndex), generateRow));
        }

        await _provider.InsertAsync(context, entityType, columnKeyedRows, cancellationToken).ConfigureAwait(false);
        return rows;
    }

    private static Dictionary<string, object> BuildColumnKeyedRow(
        IEntityType entityType,
        Dictionary<string, object> row,
        SeededRandom rowRandom,
        Func<IEntityType, SeededRandom, Dictionary<string, object>> generateRow)
    {
        Dictionary<string, object> columnKeyedRow = [];
        foreach (KeyValuePair<string, object> entry in row)
        {
            if (entityType.FindProperty(entry.Key) is IProperty property)
            {
                columnKeyedRow[property.GetColumnName()] = entry.Value;
            }
        }

        AddOwnedValues(entityType, rowRandom, generateRow, columnKeyedRow);
        return columnKeyedRow;
    }

    private static void AddOwnedValues(
        IEntityType entityType,
        SeededRandom rowRandom,
        Func<IEntityType, SeededRandom, Dictionary<string, object>> generateRow,
        Dictionary<string, object> columnKeyedRow)
    {
        foreach (INavigation navigation in GetOwnedReferenceNavigations(entityType))
        {
            IEntityType ownedEntityType = navigation.TargetEntityType;
            SeededRandom ownedRandom = rowRandom.Derive(navigation.Name);
            Dictionary<string, object> ownedValues = generateRow(ownedEntityType, ownedRandom);

            foreach (KeyValuePair<string, object> entry in ownedValues)
            {
                if (ownedEntityType.FindProperty(entry.Key) is IProperty property)
                {
                    columnKeyedRow[property.GetColumnName()] = entry.Value;
                }
            }

            AddOwnedValues(ownedEntityType, ownedRandom, generateRow, columnKeyedRow);
        }
    }

    private static void RejectIfUnsupported(IEntityType entityType)
    {
        if (entityType.BaseType is not null || entityType.GetDirectlyDerivedTypes().Any())
        {
            throw new UnsupportedEntityTypeException(
                entityType.Name, "fast mode does not support inherited entity types yet; use AutoSeedAsync instead");
        }

        IKey? primaryKey = entityType.FindPrimaryKey();
        if (primaryKey is { Properties.Count: 1 } && primaryKey.Properties[0].ValueGenerated == ValueGenerated.OnAdd
            && primaryKey.Properties[0].ClrType != typeof(int)
            && primaryKey.Properties[0].ClrType != typeof(long)
            && primaryKey.Properties[0].ClrType != typeof(Guid))
        {
            throw new UnsupportedEntityTypeException(
                entityType.Name,
                $"fast mode only assigns int, long or Guid identity primary keys itself, not '{primaryKey.Properties[0].ClrType.Name}'; use AutoSeedAsync instead");
        }
    }

    private static IEnumerable<INavigation> GetOwnedReferenceNavigations(IEntityType entityType) =>
        entityType.GetNavigations()
            .Where(navigation => !navigation.IsCollection && navigation.ForeignKey.IsOwnership && navigation.ForeignKey.PrincipalEntityType == entityType);

    /// <summary>
    /// Walks <paramref name="entityType"/>'s own properties, then, recursively, every owned
    /// reference navigation's target entity type's properties, in the stable order EF Core's own
    /// <see cref="IEntityType.GetProperties"/> and <see cref="IEntityType.GetNavigations"/> already
    /// return them in. Used by both bulk insert providers to know every column a row must carry,
    /// owner columns and flattened owned columns alike.
    /// </summary>
    /// <param name="entityType">The entity type to flatten.</param>
    /// <returns>Every property to write, paired with its column name, in a stable order.</returns>
    internal static IReadOnlyList<(IProperty Property, string ColumnName)> GetFlattenedColumns(IEntityType entityType)
    {
        List<(IProperty Property, string ColumnName)> columns = [];
        CollectFlattenedColumns(entityType, columns);
        return columns;
    }

    private static void CollectFlattenedColumns(IEntityType entityType, List<(IProperty Property, string ColumnName)> columns)
    {
        foreach (IProperty property in entityType.GetProperties())
        {
            columns.Add((property, property.GetColumnName()));
        }

        foreach (INavigation navigation in GetOwnedReferenceNavigations(entityType))
        {
            CollectFlattenedColumns(navigation.TargetEntityType, columns);
        }
    }

    /// <summary>
    /// Assigns a single-column identity primary key's value for every row of
    /// <paramref name="entityType"/>. An <see cref="int"/> or <see cref="long"/> key continues from
    /// whatever the destination table's own maximum value already is, queried once per entity type
    /// per <see cref="InsertAsync"/> call and cached in <paramref name="nextIdentityValue"/>, so
    /// seeding into a table that already has rows (a second seeding run, say) does not collide with
    /// what is already there. A <see cref="Guid"/> key is derived deterministically from
    /// <paramref name="entityRandom"/> instead, matching an empty-table identity column's continuity
    /// concern not applying to a 128-bit random value.
    /// </summary>
    private async Task AssignIdentityPrimaryKeyAsync(
        DbContext context,
        IEntityType entityType,
        List<Dictionary<string, object>> rows,
        SeededRandom entityRandom,
        Dictionary<IEntityType, long> nextIdentityValue,
        CancellationToken cancellationToken)
    {
        IKey? primaryKey = entityType.FindPrimaryKey();
        if (primaryKey is not { Properties.Count: 1 } || primaryKey.Properties[0].ValueGenerated != ValueGenerated.OnAdd)
        {
            return;
        }

        IProperty keyProperty = primaryKey.Properties[0];
        string keyPropertyName = keyProperty.Name;

        if (keyProperty.ClrType == typeof(Guid))
        {
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                SeededRandom identityRandom = entityRandom.Derive(rowIndex).Derive(IdentityRandomScope);
                rows[rowIndex][keyPropertyName] = GenerateGuid(identityRandom);
            }

            return;
        }

        if (!nextIdentityValue.TryGetValue(entityType, out long next))
        {
            long maxExisting = await _provider
                .GetMaxIdentityValueAsync(context, entityType, keyProperty.GetColumnName(), cancellationToken)
                .ConfigureAwait(false);
            next = maxExisting + 1;
        }

        foreach (Dictionary<string, object> row in rows)
        {
            row[keyPropertyName] = keyProperty.ClrType == typeof(long) ? next : (object)(int)next;
            next++;
        }

        nextIdentityValue[entityType] = next;
    }

    private static Guid GenerateGuid(SeededRandom random)
    {
        byte[] bytes = new byte[16];
        for (int index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte)random.Next(0, 256);
        }

        return new Guid(bytes);
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
