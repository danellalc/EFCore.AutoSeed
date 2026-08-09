namespace EFCore.AutoSeed;

/// <summary>
/// What changed between two <see cref="AutoSeedPlanSnapshot"/>s, from <see cref="AutoSeedDiff.Compare"/>.
/// </summary>
/// <param name="AddedEntityTypes">Entity types seedable now that were not seedable in the baseline, by full name.</param>
/// <param name="RemovedEntityTypes">Entity types seedable in the baseline that are not seedable now, by full name.</param>
/// <param name="RowCountChanges">Entity types seedable in both snapshots whose row count differs between them.</param>
/// <param name="SkipChanges">
/// Entity types whose skip status differs between the two snapshots: newly skipped, no longer
/// skipped, or skipped for a different reason.
/// </param>
/// <param name="AddedCycles">Dependency cycles present now that were not present in the baseline.</param>
/// <param name="RemovedCycles">Dependency cycles present in the baseline that are not present now.</param>
public sealed record AutoSeedDiffResult(
    IReadOnlyList<string> AddedEntityTypes,
    IReadOnlyList<string> RemovedEntityTypes,
    IReadOnlyList<AutoSeedRowCountChange> RowCountChanges,
    IReadOnlyList<AutoSeedSkipChange> SkipChanges,
    IReadOnlyList<AutoSeedPlanCycle> AddedCycles,
    IReadOnlyList<AutoSeedPlanCycle> RemovedCycles)
{
    /// <summary>
    /// Whether any difference was found at all.
    /// </summary>
    public bool HasChanges =>
        AddedEntityTypes.Count > 0 || RemovedEntityTypes.Count > 0 || RowCountChanges.Count > 0
        || SkipChanges.Count > 0 || AddedCycles.Count > 0 || RemovedCycles.Count > 0;

    /// <summary>
    /// Renders this diff as a short, human-readable report.
    /// </summary>
    /// <returns>The rendered report.</returns>
    public string ToReport()
    {
        if (!HasChanges)
        {
            return "No differences from the baseline.";
        }

        List<string> lines = [];

        foreach (string entityTypeName in AddedEntityTypes)
        {
            lines.Add($"+ {ShortName(entityTypeName)}: now seedable");
        }

        foreach (string entityTypeName in RemovedEntityTypes)
        {
            lines.Add($"- {ShortName(entityTypeName)}: no longer seedable");
        }

        foreach (AutoSeedRowCountChange change in RowCountChanges)
        {
            lines.Add($"~ {ShortName(change.EntityTypeName)}: {change.BaselineRowCount:N0} -> {change.CurrentRowCount:N0} rows");
        }

        foreach (AutoSeedSkipChange change in SkipChanges)
        {
            if (change.CurrentReason is null)
            {
                lines.Add($"+ {ShortName(change.EntityTypeName)}: no longer skipped (was: {change.BaselineReason})");
            }
            else if (change.BaselineReason is null)
            {
                lines.Add($"- {ShortName(change.EntityTypeName)}: newly skipped ({change.CurrentReason})");
            }
            else
            {
                lines.Add($"~ {ShortName(change.EntityTypeName)}: skip reason changed ({change.BaselineReason} -> {change.CurrentReason})");
            }
        }

        foreach (AutoSeedPlanCycle cycle in AddedCycles)
        {
            lines.Add($"+ Cycle: {ShortName(cycle.DependentEntityTypeName)}.{cycle.ForeignKeyProperties} -> {ShortName(cycle.PrincipalEntityTypeName)}");
        }

        foreach (AutoSeedPlanCycle cycle in RemovedCycles)
        {
            lines.Add($"- Cycle: {ShortName(cycle.DependentEntityTypeName)}.{cycle.ForeignKeyProperties} -> {ShortName(cycle.PrincipalEntityTypeName)}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string ShortName(string entityTypeFullName) => entityTypeFullName.Split('.')[^1];
}

/// <summary>
/// An entity type seedable in both snapshots whose row count changed.
/// </summary>
/// <param name="EntityTypeName">The full name of the entity type.</param>
/// <param name="BaselineRowCount">The row count in the baseline snapshot.</param>
/// <param name="CurrentRowCount">The row count in the current snapshot.</param>
public sealed record AutoSeedRowCountChange(string EntityTypeName, int BaselineRowCount, int CurrentRowCount);

/// <summary>
/// An entity type whose skip status changed between two snapshots.
/// </summary>
/// <param name="EntityTypeName">The full name of the entity type.</param>
/// <param name="BaselineReason">Why it was skipped in the baseline, or <see langword="null"/> if it was seedable there.</param>
/// <param name="CurrentReason">Why it is skipped now, or <see langword="null"/> if it is seedable now.</param>
public sealed record AutoSeedSkipChange(string EntityTypeName, string? BaselineReason, string? CurrentReason);
