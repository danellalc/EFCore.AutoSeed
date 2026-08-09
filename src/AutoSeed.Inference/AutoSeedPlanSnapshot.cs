using EFCore.AutoSeed.Pipeline;

namespace EFCore.AutoSeed;

/// <summary>
/// A serializable snapshot of what <see cref="DbContextAutoSeedExtensions.AutoSeedExplainAsync"/>
/// would do, saved as a baseline to diff a later model against with <see cref="AutoSeedDiff"/>.
/// Unlike <see cref="AutoSeedExplainResult"/>, every entity type is a plain string name instead of
/// a live <see cref="Microsoft.EntityFrameworkCore.Metadata.IEntityType"/>, so it survives being
/// written to disk and read back by a later, separate process.
/// </summary>
/// <param name="Version">The schema version of this snapshot, for forward compatibility as more fields are added.</param>
/// <param name="Order">The insertion order every seedable entity type would be written in, by full name.</param>
/// <param name="RowCounts">How many rows each entity type in <paramref name="Order"/> would get, keyed by entity type name.</param>
/// <param name="SkippedEntityTypes">Entity types found in the model but excluded from seeding, and why.</param>
/// <param name="Cycles">Every dependency cycle that would be resolved with a second, two-pass insert.</param>
public sealed record AutoSeedPlanSnapshot(
    int Version,
    IReadOnlyList<string> Order,
    IReadOnlyDictionary<string, int> RowCounts,
    IReadOnlyList<SkippedEntityType> SkippedEntityTypes,
    IReadOnlyList<AutoSeedPlanCycle> Cycles)
{
    /// <summary>
    /// The current snapshot schema version, written by <see cref="FromExplainResult"/>.
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// Builds a snapshot from a live <see cref="AutoSeedExplainResult"/>.
    /// </summary>
    /// <param name="result">The result to snapshot.</param>
    /// <returns>A snapshot with the same information, safe to serialize.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    public static AutoSeedPlanSnapshot FromExplainResult(AutoSeedExplainResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new AutoSeedPlanSnapshot(
            CurrentVersion,
            [.. result.Order.Select(entityType => entityType.Name)],
            result.RowCounts,
            result.SkippedEntityTypes,
            [.. result.DeferredEdges.Select(edge => new AutoSeedPlanCycle(
                edge.Dependent.Name,
                edge.Principal.Name,
                string.Join(", ", edge.ForeignKey.Properties.Select(property => property.Name))))]);
    }
}

/// <summary>
/// One dependency cycle a snapshot's plan would resolve with a second, two-pass insert.
/// </summary>
/// <param name="DependentEntityTypeName">The full name of the entity type whose foreign key was deferred.</param>
/// <param name="PrincipalEntityTypeName">The full name of the entity type the deferred foreign key points at.</param>
/// <param name="ForeignKeyProperties">The deferred foreign key's property names, comma-separated.</param>
public sealed record AutoSeedPlanCycle(string DependentEntityTypeName, string PrincipalEntityTypeName, string ForeignKeyProperties);
