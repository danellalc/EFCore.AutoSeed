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
    /// <returns>The number of rows inserted, keyed by entity type name.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
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
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(deferredEdges);
        ArgumentNullException.ThrowIfNull(generateRow);
        ArgumentNullException.ThrowIfNull(rootRandom);

        IReadOnlyList<GraphEdge> requiredEdges = [.. edges.Where(edge => edge.ForeignKey.IsRequired)];
        Dictionary<IEntityType, List<object>> insertedByEntityType = [];

        foreach (EntityGenerationPlan entityPlan in plan)
        {
            List<object> instances = await InsertEntityTypeAsync(
                context, entityPlan, requiredEdges, insertedByEntityType, generateRow, rootRandom, cancellationToken).ConfigureAwait(false);
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
        Dictionary<IEntityType, List<object>> insertedByEntityType,
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

        int[]? driverRowIndices = entityPlan.ChildCountsByDriverRow is null
            ? null
            : ExpandDriverRowIndices(entityPlan.ChildCountsByDriverRow);

        List<object> instances = new(rows.Count);
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            object instance = CreateInstance(entityType);
            context.Add(instance);
            ApplyValues(context, instance, rows[rowIndex]);
            AssignOwnedTypes(context, entityType, instance, entityRandom.Derive(rowIndex), generateRow);
            AssignRequiredForeignKeys(context, requiredEdges, entityType, entityPlan, instance, rowIndex, driverRowIndices, insertedByEntityType);
            instances.Add(instance);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return instances;
    }

    private static void AssignRequiredForeignKeys(
        DbContext context,
        IReadOnlyList<GraphEdge> requiredEdges,
        IEntityType entityType,
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

            AssignForeignKey(context, edge, instance, principalInstance);
        }
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
            .Where(navigation => !navigation.IsCollection && navigation.ForeignKey.IsOwnership && navigation.ForeignKey.PrincipalEntityType == entityType);

    private static void ApplyValues(DbContext context, object instance, Dictionary<string, object> values)
    {
        foreach (KeyValuePair<string, object> entry in values)
        {
            context.Entry(instance).Property(entry.Key).CurrentValue = entry.Value;
        }
    }

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
