using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed;

/// <summary>
/// What <see cref="DbContextAutoSeedExtensions.AutoSeedExplainAsync"/> would do, without writing
/// anything to the database.
/// </summary>
/// <param name="Order">The insertion order every seedable entity type would be written in.</param>
/// <param name="RowCounts">How many rows each entity type in <paramref name="Order"/> would get, keyed by entity type name.</param>
/// <param name="ChildCountsByEntityType">
/// For entity types whose row count was derived from a principal (see <see cref="EntityGenerationPlan.Driver"/>),
/// the per-principal-row child counts that produced <paramref name="RowCounts"/>. Absent for root entity types.
/// </param>
/// <param name="SkippedEntityTypes">Entity types found in the model but excluded from seeding, and why.</param>
/// <param name="DeferredEdges">
/// Foreign keys that form a nullable cycle and would be resolved with a second, two-pass insert.
/// </param>
public sealed record AutoSeedExplainResult(
    IReadOnlyList<IEntityType> Order,
    IReadOnlyDictionary<string, int> RowCounts,
    IReadOnlyDictionary<string, IReadOnlyList<int>> ChildCountsByEntityType,
    IReadOnlyList<SkippedEntityType> SkippedEntityTypes,
    IReadOnlyList<GraphEdge> DeferredEdges)
{
    /// <summary>
    /// Renders this plan as a short, human-readable report.
    /// </summary>
    /// <returns>The rendered report.</returns>
    public string ToReport()
    {
        List<string> lines = [];

        if (Order.Count > 0)
        {
            lines.Add(string.Join(" -> ", Order.Select(ShortName)));
        }

        int nameWidth = Order.Count == 0 ? 0 : Order.Max(entityType => ShortName(entityType).Length);
        foreach (IEntityType entityType in Order)
        {
            string name = ShortName(entityType);
            int rowCount = RowCounts[entityType.Name];
            string line = $"{name.PadRight(nameWidth)}: {rowCount,7:N0} rows";

            if (ChildCountsByEntityType.TryGetValue(entityType.Name, out IReadOnlyList<int>? childCounts) && childCounts.Count > 0)
            {
                double mean = childCounts.Average();
                int max = childCounts.Max();
                line += $" (long tail, mean {mean:0.0}, max {max:N0})";
            }

            lines.Add(line);
        }

        foreach (GraphEdge edge in DeferredEdges)
        {
            string dependent = ShortName(edge.Dependent);
            string property = string.Join(", ", edge.ForeignKey.Properties.Select(property => property.Name));
            lines.Add($"Cycle detected: {dependent}.{property} (nullable, resolved in 2 passes)");
        }

        foreach (SkippedEntityType skipped in SkippedEntityTypes)
        {
            lines.Add($"Skipped: {skipped.EntityTypeName.Split('.')[^1]} ({skipped.Reason})");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string ShortName(IEntityType entityType) => entityType.Name.Split('.')[^1];
}
