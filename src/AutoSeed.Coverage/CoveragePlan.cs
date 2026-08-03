using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Coverage;

/// <summary>
/// Decides how many rows to generate for each entity type so that, together with
/// <see cref="CoverageValueGenerator"/>, the resulting dataset touches every enum value, every
/// nullable property in both states, every string at its length boundaries, and every relationship
/// at zero, one and many, in as few rows as possible.
/// </summary>
public sealed class CoveragePlan
{
    private const int MinimumOwnRowCount = 3;

    /// <summary>
    /// Computes a row count for every entity type in <paramref name="order"/>.
    /// </summary>
    /// <param name="order">Every entity type to plan for, topologically sorted so a principal always precedes its dependents.</param>
    /// <param name="edges">Every dependency between two of <paramref name="order"/>.</param>
    /// <returns>One <see cref="EntityGenerationPlan"/> per entity type in <paramref name="order"/>, in the same order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="order"/> or <paramref name="edges"/> is <see langword="null"/>.</exception>
    public IReadOnlyList<EntityGenerationPlan> Plan(IReadOnlyList<IEntityType> order, IReadOnlyList<GraphEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(edges);

        ILookup<IEntityType, GraphEdge> requiredEdgesByDependent = edges
            .Where(edge => edge.ForeignKey.IsRequired)
            .ToLookup(edge => edge.Dependent);

        Dictionary<IEntityType, int> rowCounts = [];
        List<EntityGenerationPlan> plan = new(order.Count);

        foreach (IEntityType entityType in order)
        {
            int ownCoverageRowCount = OwnCoverageRowCount(entityType);

            GraphEdge? driverEdge = requiredEdgesByDependent[entityType]
                .OrderBy(edge => edge.Principal.Name, StringComparer.Ordinal)
                .FirstOrDefault();

            if (driverEdge is null)
            {
                rowCounts[entityType] = ownCoverageRowCount;
                plan.Add(new EntityGenerationPlan(entityType, ownCoverageRowCount, null, null));
                continue;
            }

            IEntityType driver = driverEdge.Principal;
            int driverRowCount = rowCounts[driver];

            IReadOnlyList<int> childCounts = SharedPrimaryKey.IsDependent(entityType, driverEdge.ForeignKey)
                ? Enumerable.Repeat(1, driverRowCount).ToArray()
                : CardinalityChildCounts(driverRowCount, ownCoverageRowCount);

            int rowCount = childCounts.Sum();
            rowCounts[entityType] = rowCount;
            plan.Add(new EntityGenerationPlan(entityType, rowCount, driver, childCounts));
        }

        return plan;
    }

    private static int OwnCoverageRowCount(IEntityType entityType)
    {
        int largestEnumCardinality = entityType.GetProperties()
            .Select(property => Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType)
            .Where(clrType => clrType.IsEnum)
            .Select(clrType => Enum.GetValues(clrType).Length)
            .DefaultIfEmpty(0)
            .Max();

        return 1 + Math.Max(MinimumOwnRowCount, largestEnumCardinality);
    }

    /// <summary>
    /// Puts zero children on the driver's first row, exactly one on its second, and every
    /// remaining coverage row on its third (or its first, if there is no third), so the "zero, one
    /// and many" cardinalities all appear without spending extra rows purely to demonstrate shape.
    /// </summary>
    private static int[] CardinalityChildCounts(int driverRowCount, int ownCoverageRowCount)
    {
        int[] childCounts = new int[driverRowCount];
        if (driverRowCount == 0)
        {
            return childCounts;
        }

        if (driverRowCount == 1)
        {
            childCounts[0] = ownCoverageRowCount;
            return childCounts;
        }

        childCounts[1] = 1;
        int manyIndex = driverRowCount > 2 ? 2 : 1;
        childCounts[manyIndex] += Math.Max(ownCoverageRowCount - 1, 0);
        return childCounts;
    }
}
