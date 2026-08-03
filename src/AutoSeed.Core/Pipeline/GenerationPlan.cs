using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Pipeline;

/// <summary>
/// Decides how many rows to generate for each entity type. An entity type with no required
/// foreign key dependency gets <c>scale</c> rows directly. An entity type that depends on another
/// gets a row count derived from its principal's row count: one draw per principal row from a
/// long-tail distribution, so most principal rows get few dependents and a few get many.
/// </summary>
public sealed class GenerationPlan
{
    private readonly double _meanChildrenPerParent;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenerationPlan"/> class.
    /// </summary>
    /// <param name="meanChildrenPerParent">The average number of dependent rows generated per principal row.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="meanChildrenPerParent"/> is not positive.</exception>
    public GenerationPlan(double meanChildrenPerParent = 3.0)
    {
        if (meanChildrenPerParent <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(meanChildrenPerParent), meanChildrenPerParent, "Must be positive.");
        }

        _meanChildrenPerParent = meanChildrenPerParent;
    }

    /// <summary>
    /// Computes a row count for every entity type in <paramref name="order"/>.
    /// </summary>
    /// <param name="order">Every entity type to plan for, topologically sorted so a principal always precedes its dependents.</param>
    /// <param name="edges">Every dependency between two of <paramref name="order"/>.</param>
    /// <param name="scale">The row count for entity types with no required principal.</param>
    /// <param name="random">The random source this plan's cardinality draws derive from.</param>
    /// <returns>One <see cref="EntityGenerationPlan"/> per entity type in <paramref name="order"/>, in the same order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="order"/>, <paramref name="edges"/> or <paramref name="random"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scale"/> is not positive.</exception>
    public IReadOnlyList<EntityGenerationPlan> Plan(IReadOnlyList<IEntityType> order, IReadOnlyList<GraphEdge> edges, int scale, SeededRandom random)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(random);
        if (scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "Must be positive.");
        }

        return PlanCore(order, edges, _ => scale, random);
    }

    /// <summary>
    /// Like the single-scale overload, but an entity type with no required principal uses its own
    /// scale from <paramref name="scaleByRootEntityType"/> instead of one value shared by every
    /// such entity type. Used to preserve a captured production shape's relative table sizes.
    /// </summary>
    /// <param name="order">Every entity type to plan for, topologically sorted so a principal always precedes its dependents.</param>
    /// <param name="edges">Every dependency between two of <paramref name="order"/>.</param>
    /// <param name="scaleByRootEntityType">The row count for an entity type with no required principal, keyed by entity type.</param>
    /// <param name="defaultScale">The row count for an entity type with no required principal that is absent from <paramref name="scaleByRootEntityType"/>, or whose value there is not positive.</param>
    /// <param name="random">The random source this plan's cardinality draws derive from.</param>
    /// <returns>One <see cref="EntityGenerationPlan"/> per entity type in <paramref name="order"/>, in the same order.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="order"/>, <paramref name="edges"/>, <paramref name="scaleByRootEntityType"/> or <paramref name="random"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="defaultScale"/> is not positive.</exception>
    public IReadOnlyList<EntityGenerationPlan> Plan(
        IReadOnlyList<IEntityType> order,
        IReadOnlyList<GraphEdge> edges,
        IReadOnlyDictionary<IEntityType, int> scaleByRootEntityType,
        int defaultScale,
        SeededRandom random)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(scaleByRootEntityType);
        ArgumentNullException.ThrowIfNull(random);
        if (defaultScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultScale), defaultScale, "Must be positive.");
        }

        return PlanCore(
            order,
            edges,
            entityType => scaleByRootEntityType.TryGetValue(entityType, out int scale) && scale > 0 ? scale : defaultScale,
            random);
    }

    private IReadOnlyList<EntityGenerationPlan> PlanCore(
        IReadOnlyList<IEntityType> order, IReadOnlyList<GraphEdge> edges, Func<IEntityType, int> scaleForRootEntityType, SeededRandom random)
    {
        ILookup<IEntityType, GraphEdge> requiredEdgesByDependent = edges
            .Where(edge => edge.ForeignKey.IsRequired)
            .ToLookup(edge => edge.Dependent);

        Dictionary<IEntityType, int> rowCounts = [];
        List<EntityGenerationPlan> plan = new(order.Count);

        foreach (IEntityType entityType in order)
        {
            GraphEdge? driverEdge = requiredEdgesByDependent[entityType]
                .OrderBy(edge => edge.Principal.Name, StringComparer.Ordinal)
                .FirstOrDefault();

            if (driverEdge is null)
            {
                int scale = scaleForRootEntityType(entityType);
                rowCounts[entityType] = scale;
                plan.Add(new EntityGenerationPlan(entityType, scale, null, null));
                continue;
            }

            IEntityType driver = driverEdge.Principal;
            IReadOnlyList<int> childCounts = SharedPrimaryKey.IsDependent(entityType, driverEdge.ForeignKey)
                ? Enumerable.Repeat(1, rowCounts[driver]).ToArray()
                : DrawChildCounts(entityType, rowCounts[driver], random);

            int rowCount = childCounts.Sum();
            rowCounts[entityType] = rowCount;
            plan.Add(new EntityGenerationPlan(entityType, rowCount, driver, childCounts));
        }

        return plan;
    }

    private IReadOnlyList<int> DrawChildCounts(IEntityType entityType, int driverRowCount, SeededRandom random)
    {
        SeededRandom entityRandom = random.Derive(entityType.Name);
        int[] childCounts = new int[driverRowCount];

        for (int driverRowIndex = 0; driverRowIndex < driverRowCount; driverRowIndex++)
        {
            childCounts[driverRowIndex] = DrawChildCount(entityRandom.Derive(driverRowIndex));
        }

        return childCounts;
    }

    private int DrawChildCount(SeededRandom random)
    {
        double uniform = random.NextDouble();
        double drawn = -_meanChildrenPerParent * Math.Log(1.0 - uniform);
        return (int)Math.Round(drawn, MidpointRounding.AwayFromZero);
    }
}
