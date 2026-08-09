using System.Reflection;
using EFCore.AutoSeed.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Pipeline;

/// <summary>
/// Inserts a generation plan into a <see cref="DbContext"/> in fidelity mode: one
/// <see cref="DbContext.SaveChangesAsync(CancellationToken)"/> per entity type, in dependency
/// order, followed by a second pass that fills in every foreign key deferred to break a cycle.
/// </summary>
public sealed class Persistence
{
    private const string IdentityRandomScope = "__identity";

    private readonly UniquenessEnforcer _uniquenessEnforcer;

    /// <summary>
    /// Initializes a new instance of the <see cref="Persistence"/> class.
    /// </summary>
    /// <param name="uniquenessEnforcer">
    /// The enforcer used to fix up duplicate values for unique properties before each entity
    /// type is inserted. Defaults to a new <see cref="Pipeline.UniquenessEnforcer"/>.
    /// </param>
    public Persistence(UniquenessEnforcer? uniquenessEnforcer = null)
    {
        _uniquenessEnforcer = uniquenessEnforcer ?? new UniquenessEnforcer();
    }

    /// <summary>
    /// Generates and inserts every row in <paramref name="plan"/>.
    /// </summary>
    /// <param name="context">The context to insert into.</param>
    /// <param name="plan">How many rows to generate for each entity type, in dependency order.</param>
    /// <param name="edges">Every dependency between two entity types in <paramref name="plan"/>, required or deferred.</param>
    /// <param name="deferredEdges">The foreign keys left null on first insert and filled in on a second pass.</param>
    /// <param name="generateRow">Produces the property values for one row of one entity type.</param>
    /// <param name="rootRandom">The random source every row and every uniqueness fix-up derives from.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <param name="existingRowsByEntityType">
    /// Already-existing rows for an entity type excluded from generation, used as valid foreign key
    /// targets in place of freshly generated ones. An entity type present here is never generated or
    /// inserted, regardless of its row count in <paramref name="plan"/>. Defaults to none excluded.
    /// </param>
    /// <param name="nullRate">
    /// For an optional foreign key that is not part of <paramref name="deferredEdges"/>, the chance
    /// each row leaves it null instead of pointing at a generated principal row. Defaults to
    /// <c>1</c>, so an optional foreign key stays null, as it always has, unless a caller opts in
    /// to a lower rate.
    /// </param>
    /// <param name="exactRowsByEntityType">
    /// Caller-supplied, already fully-formed rows for an entity type configured with
    /// <c>EntityConfigurationBuilder&lt;TEntity&gt;.SeedWith(rows)</c>: inserted exactly as given, in
    /// the same dependency-ordered pass as every other entity type, but never generated. Defaults to
    /// none.
    /// </param>
    /// <returns>The number of rows inserted, keyed by entity type name.</returns>
    /// <exception cref="ArgumentNullException">Any required argument is <see langword="null"/>.</exception>
    /// <exception cref="UnsupportedEntityTypeException">
    /// An entity type has no public parameterless constructor, or a required principal has no generated rows.
    /// </exception>
    public async Task<IReadOnlyDictionary<string, int>> InsertAsync(
        DbContext context,
        IReadOnlyList<EntityGenerationPlan> plan,
        IReadOnlyList<GraphEdge> edges,
        IReadOnlyList<GraphEdge> deferredEdges,
        Func<IEntityType, SeededRandom, Dictionary<string, object>> generateRow,
        SeededRandom rootRandom,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<IEntityType, IReadOnlyList<object>>? existingRowsByEntityType = null,
        double nullRate = 1,
        IReadOnlyDictionary<IEntityType, IReadOnlyList<object>>? exactRowsByEntityType = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(deferredEdges);
        ArgumentNullException.ThrowIfNull(generateRow);
        ArgumentNullException.ThrowIfNull(rootRandom);

        IReadOnlyList<GraphEdge> requiredEdges = [.. edges.Where(edge => edge.ForeignKey.IsRequired)];
        HashSet<GraphEdge> deferredEdgeSet = [.. deferredEdges];
        IReadOnlyList<GraphEdge> optionalEdges = [.. edges.Where(edge => !edge.ForeignKey.IsRequired && !deferredEdgeSet.Contains(edge))];
        Dictionary<IEntityType, List<object>> insertedByEntityType = [];

        foreach (EntityGenerationPlan entityPlan in plan)
        {
            List<object> instances;
            if (existingRowsByEntityType?.TryGetValue(entityPlan.EntityType, out IReadOnlyList<object>? existingRows) is true)
            {
                instances = [.. existingRows];
            }
            else if (exactRowsByEntityType?.TryGetValue(entityPlan.EntityType, out IReadOnlyList<object>? exactRows) is true)
            {
                instances = await InsertExactRowsAsync(context, exactRows, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                instances = await InsertEntityTypeAsync(
                    context, entityPlan, requiredEdges, optionalEdges, insertedByEntityType, generateRow, rootRandom, nullRate, cancellationToken)
                    .ConfigureAwait(false);
            }

            insertedByEntityType[entityPlan.EntityType] = instances;
        }

        if (deferredEdges.Count > 0)
        {
            AssignDeferredForeignKeys(context, deferredEdges, insertedByEntityType);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return plan.ToDictionary(entry => entry.EntityType.Name, entry => entry.RowCount);
    }

    private async Task<List<object>> InsertEntityTypeAsync(
        DbContext context,
        EntityGenerationPlan entityPlan,
        IReadOnlyList<GraphEdge> requiredEdges,
        IReadOnlyList<GraphEdge> optionalEdges,
        Dictionary<IEntityType, List<object>> insertedByEntityType,
        Func<IEntityType, SeededRandom, Dictionary<string, object>> generateRow,
        SeededRandom rootRandom,
        double nullRate,
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
        AssignGuidIdentityPrimaryKey(entityType, rows, entityRandom);

        int[]? driverRowIndices = entityPlan.ChildCountsByDriverRow is null
            ? null
            : ExpandDriverRowIndices(entityPlan.ChildCountsByDriverRow);

        List<object> instances = new(rows.Count);
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            SeededRandom rowRandom = entityRandom.Derive(rowIndex);
            object instance = CreateInstance(entityType);
            Dictionary<string, object> deferredValues = ApplyValuesBeforeTracking(entityType, instance, rows[rowIndex]);
            AssignRequiredForeignKeysBeforeTracking(entityType, requiredEdges, entityPlan, instance, rowIndex, driverRowIndices, insertedByEntityType);
            AssignOptionalForeignKeysBeforeTracking(
                entityType, optionalEdges, entityPlan, instance, rowIndex, driverRowIndices, insertedByEntityType, rowRandom, nullRate);

            context.Add(instance);
            ApplyDeferredValues(context, instance, deferredValues);
            AssignOwnedTypes(context, entityType, instance, rowRandom, generateRow);
            instances.Add(instance);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return instances;
    }

    /// <summary>
    /// Inserts a <c>SeedWith</c> entity type's caller-supplied rows exactly as given: no generation,
    /// no foreign key assignment, no owned-type population. A database-generated primary key still
    /// gets whatever value the database assigns, the same as <see cref="DbContext.Add(object)"/>
    /// called directly would produce.
    /// </summary>
    private static async Task<List<object>> InsertExactRowsAsync(DbContext context, IReadOnlyList<object> exactRows, CancellationToken cancellationToken)
    {
        List<object> instances = new(exactRows.Count);
        foreach (object instance in exactRows)
        {
            context.Add(instance);
            instances.Add(instance);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return instances;
    }

    /// <summary>
    /// Fills in a single-column <see cref="Guid"/> primary key with <see cref="ValueGenerated.OnAdd"/>
    /// before it reaches <see cref="DbContext.Add(object)"/>. No inference rule ever produces a
    /// value for such a property, so left alone it would reach <see cref="DbContext.Add(object)"/>
    /// still at its CLR default and EF Core's own non-seeded client-side generator would assign it,
    /// breaking determinism. Uses the same <paramref name="entityRandom"/> derivation path and byte
    /// generation as fast mode's identity assignment, so both modes produce the same key for the
    /// same seed and row index. Composite keys and non-<see cref="Guid"/> identity primary keys
    /// (<see cref="int"/>, <see cref="long"/>) are left untouched, the latter to the database's own
    /// real IDENTITY column.
    /// </summary>
    private static void AssignGuidIdentityPrimaryKey(IEntityType entityType, List<Dictionary<string, object>> rows, SeededRandom entityRandom)
    {
        IKey? primaryKey = entityType.FindPrimaryKey();
        if (primaryKey is not { Properties.Count: 1 }
            || primaryKey.Properties[0].ValueGenerated != ValueGenerated.OnAdd
            || primaryKey.Properties[0].ClrType != typeof(Guid))
        {
            return;
        }

        string keyPropertyName = primaryKey.Properties[0].Name;
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            SeededRandom identityRandom = entityRandom.Derive(rowIndex).Derive(IdentityRandomScope);
            rows[rowIndex][keyPropertyName] = GenerateGuid(identityRandom);
        }
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

    /// <summary>
    /// A property with two columns as a composite primary key, one of them also a foreign key, is
    /// tracked with a half-formed key (the foreign key column still at its CLR default) the moment
    /// <see cref="DbContext.Add(object)"/> runs. Setting the foreign key afterward, through
    /// <see cref="EntityEntry"/>, is a key change EF Core's identity map validates immediately, and
    /// two rows sharing that same half-formed key collide before either ever gets its real value.
    /// Everything that can be set through reflection happens on the plain, untracked instance
    /// first, so <see cref="DbContext.Add(object)"/> only ever sees a fully-formed key. Only shadow
    /// properties (no backing CLR member) fall back to being set after tracking begins.
    /// </summary>
    private static Dictionary<string, object> ApplyValuesBeforeTracking(IEntityType entityType, object instance, Dictionary<string, object> values)
    {
        Dictionary<string, object> deferredValues = [];

        foreach (KeyValuePair<string, object> entry in values)
        {
            IProperty? property = entityType.FindProperty(entry.Key);
            if (property?.PropertyInfo is PropertyInfo propertyInfo && propertyInfo.CanWrite)
            {
                SetPropertyValue(property, instance, entry.Value);
            }
            else
            {
                deferredValues[entry.Key] = entry.Value;
            }
        }

        return deferredValues;
    }

    private static void ApplyDeferredValues(DbContext context, object instance, Dictionary<string, object> deferredValues)
    {
        foreach (KeyValuePair<string, object> entry in deferredValues)
        {
            context.Entry(instance).Property(entry.Key).CurrentValue = entry.Value;
        }
    }

    private static void AssignRequiredForeignKeysBeforeTracking(
        IEntityType entityType,
        IReadOnlyList<GraphEdge> requiredEdges,
        EntityGenerationPlan entityPlan,
        object instance,
        int rowIndex,
        int[]? driverRowIndices,
        Dictionary<IEntityType, List<object>> insertedByEntityType)
    {
        foreach (GraphEdge edge in requiredEdges)
        {
            if (edge.Dependent != entityType)
            {
                continue;
            }

            List<object> principalInstances = insertedByEntityType[edge.Principal];
            if (principalInstances.Count == 0)
            {
                throw new UnsupportedEntityTypeException(
                    entityType.Name, $"required principal '{edge.Principal.Name}' has zero generated rows");
            }

            object principalInstance = edge.Principal == entityPlan.Driver && driverRowIndices is not null
                ? principalInstances[driverRowIndices[rowIndex]]
                : principalInstances[rowIndex % principalInstances.Count];

            AssignForeignKeyBeforeTracking(edge, instance, principalInstance);
        }
    }

    /// <summary>
    /// An optional foreign key that is not part of a resolved cycle is never assigned a value by
    /// any other step in the pipeline, so left alone it would stay at its CLR default (null) on
    /// every row, regardless of how many valid principal rows exist. Rolls, per row and per edge,
    /// whether to leave it null instead, so an optional relationship is exercised as often as
    /// <paramref name="nullRate"/> allows. Unlike a required foreign key, a principal with zero
    /// generated rows is not an error here: the relationship is skipped for every row instead.
    /// </summary>
    private static void AssignOptionalForeignKeysBeforeTracking(
        IEntityType entityType,
        IReadOnlyList<GraphEdge> optionalEdges,
        EntityGenerationPlan entityPlan,
        object instance,
        int rowIndex,
        int[]? driverRowIndices,
        Dictionary<IEntityType, List<object>> insertedByEntityType,
        SeededRandom rowRandom,
        double nullRate)
    {
        foreach (GraphEdge edge in optionalEdges)
        {
            if (edge.Dependent != entityType)
            {
                continue;
            }

            if (!insertedByEntityType.TryGetValue(edge.Principal, out List<object>? principalInstances) || principalInstances.Count == 0)
            {
                continue;
            }

            string edgeKey = string.Join(",", edge.ForeignKey.Properties.Select(property => property.Name));
            bool leaveNull = rowRandom.Derive(edgeKey).Derive("NullRate").NextDouble() < nullRate;
            if (leaveNull)
            {
                continue;
            }

            object principalInstance = edge.Principal == entityPlan.Driver && driverRowIndices is not null
                ? principalInstances[driverRowIndices[rowIndex]]
                : principalInstances[rowIndex % principalInstances.Count];

            AssignForeignKeyBeforeTracking(edge, instance, principalInstance);
        }
    }

    private static void AssignForeignKeyBeforeTracking(GraphEdge edge, object dependentInstance, object principalInstance)
    {
        if (edge.ForeignKey.DependentToPrincipal is INavigation navigation
            && navigation.PropertyInfo is PropertyInfo navigationProperty && navigationProperty.CanWrite)
        {
            SetPropertyValue(navigation, dependentInstance, principalInstance);
            return;
        }

        IReadOnlyList<IProperty> dependentProperties = edge.ForeignKey.Properties;
        IReadOnlyList<IProperty> principalProperties = edge.ForeignKey.PrincipalKey.Properties;

        for (int index = 0; index < dependentProperties.Count; index++)
        {
            IProperty principalProperty = principalProperties[index];
            IProperty dependentProperty = dependentProperties[index];

            if (principalProperty.PropertyInfo is null
                || dependentProperty.PropertyInfo is not PropertyInfo dependentPropertyInfo
                || !dependentPropertyInfo.CanWrite)
            {
                continue;
            }

            SetPropertyValue(dependentProperty, dependentInstance, GetPropertyValue(principalProperty, principalInstance));
        }
    }

    /// <summary>
    /// An implicit many-to-many join entity type is a shared-type entity backed by
    /// <see cref="Dictionary{TKey, TValue}"/>: its properties are real columns, but reflection sees
    /// them through <see cref="Dictionary{TKey, TValue}"/>'s indexer instead of a named member, and
    /// <see cref="PropertyInfo.SetValue(object, object)"/> on an indexer throws
    /// <see cref="TargetParameterCountException"/> because it never passes the index argument the
    /// indexer requires.
    /// </summary>
    private static void SetPropertyValue(IPropertyBase property, object instance, object? value)
    {
        PropertyInfo propertyInfo = property.PropertyInfo!;
        if (property.IsIndexerProperty())
        {
            propertyInfo.SetValue(instance, value, [property.Name]);
        }
        else
        {
            propertyInfo.SetValue(instance, value);
        }
    }

    private static object? GetPropertyValue(IPropertyBase property, object instance)
    {
        PropertyInfo propertyInfo = property.PropertyInfo!;
        return property.IsIndexerProperty()
            ? propertyInfo.GetValue(instance, [property.Name])
            : propertyInfo.GetValue(instance);
    }

    private static void AssignDeferredForeignKeys(
        DbContext context, IReadOnlyList<GraphEdge> deferredEdges, Dictionary<IEntityType, List<object>> insertedByEntityType)
    {
        foreach (GraphEdge edge in deferredEdges)
        {
            List<object> dependents = insertedByEntityType[edge.Dependent];
            List<object> principals = insertedByEntityType[edge.Principal];
            bool isSelfReference = edge.Principal == edge.Dependent;

            if (principals.Count == 0 || (isSelfReference && principals.Count == 1))
            {
                continue;
            }

            for (int index = 0; index < dependents.Count; index++)
            {
                int principalIndex = index % principals.Count;
                if (isSelfReference && principalIndex == index)
                {
                    principalIndex = (principalIndex + 1) % principals.Count;
                }

                AssignForeignKey(context, edge, dependents[index], principals[principalIndex]);
            }
        }
    }

    private static void AssignForeignKey(DbContext context, GraphEdge edge, object dependentInstance, object principalInstance)
    {
        INavigation? navigation = edge.ForeignKey.DependentToPrincipal;
        if (navigation is not null)
        {
            context.Entry(dependentInstance).Reference(navigation.Name).CurrentValue = principalInstance;
            return;
        }

        IReadOnlyList<IProperty> dependentProperties = edge.ForeignKey.Properties;
        IReadOnlyList<IProperty> principalProperties = edge.ForeignKey.PrincipalKey.Properties;

        for (int index = 0; index < dependentProperties.Count; index++)
        {
            object? principalValue = context.Entry(principalInstance).Property(principalProperties[index].Name).CurrentValue;
            context.Entry(dependentInstance).Property(dependentProperties[index].Name).CurrentValue = principalValue;
        }
    }

    private static void AssignOwnedTypes(
        DbContext context,
        IEntityType entityType,
        object instance,
        SeededRandom rowRandom,
        Func<IEntityType, SeededRandom, Dictionary<string, object>> generateRow)
    {
        foreach (INavigation navigation in GetOwnedReferenceNavigations(entityType))
        {
            IEntityType ownedEntityType = navigation.TargetEntityType;
            object ownedInstance = CreateInstance(ownedEntityType);

            ReferenceEntry ownerReference = context.Entry(instance).Reference(navigation.Name);
            ownerReference.CurrentValue = ownedInstance;

            EntityEntry? ownedEntry = ownerReference.TargetEntry;
            if (ownedEntry is null)
            {
                continue;
            }

            SeededRandom ownedRandom = rowRandom.Derive(navigation.Name);
            Dictionary<string, object> ownedValues = generateRow(ownedEntityType, ownedRandom);
            foreach (KeyValuePair<string, object> entry in ownedValues)
            {
                ownedEntry.Property(entry.Key).CurrentValue = entry.Value;
            }

            AssignOwnedTypes(context, ownedEntityType, ownedInstance, ownedRandom, generateRow);
        }
    }

    private static IEnumerable<INavigation> GetOwnedReferenceNavigations(IEntityType entityType) =>
        entityType.GetNavigations()
            .Where(navigation => !navigation.IsCollection && navigation.ForeignKey.IsOwnership
                && navigation.ForeignKey.PrincipalEntityType.GetDerivedTypesInclusive().Contains(entityType));

    private static object CreateInstance(IEntityType entityType)
    {
        try
        {
            return Activator.CreateInstance(entityType.ClrType)
                ?? throw new UnsupportedEntityTypeException(entityType.Name, "its constructor returned null");
        }
        catch (MissingMethodException)
        {
            throw new UnsupportedEntityTypeException(entityType.Name, "it has no public parameterless constructor");
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw new UnsupportedEntityTypeException(
                entityType.Name, "its parameterless constructor threw an exception", exception.InnerException);
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
