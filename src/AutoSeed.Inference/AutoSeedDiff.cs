namespace EFCore.AutoSeed;

/// <summary>
/// Compares two <see cref="AutoSeedPlanSnapshot"/>s: a saved baseline against the model's current
/// shape, so a change that would alter what gets seeded (a new required property with no rule for
/// it, a newly-introduced cycle, an entity type that stopped or started being seedable) is caught
/// as an explicit, reviewable diff instead of only showing up the next time something actually
/// seeds the database.
/// </summary>
public static class AutoSeedDiff
{
    /// <summary>
    /// Compares <paramref name="baseline"/> against <paramref name="current"/>.
    /// </summary>
    /// <param name="baseline">A snapshot saved earlier, typically checked into source control.</param>
    /// <param name="current">A snapshot of the model as it is right now.</param>
    /// <returns>Every difference found, empty if none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="baseline"/> or <paramref name="current"/> is <see langword="null"/>.</exception>
    public static AutoSeedDiffResult Compare(AutoSeedPlanSnapshot baseline, AutoSeedPlanSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);

        HashSet<string> baselineOrder = [.. baseline.Order];
        HashSet<string> currentOrder = [.. current.Order];

        List<string> addedEntityTypes = [.. currentOrder.Except(baselineOrder).OrderBy(name => name, StringComparer.Ordinal)];
        List<string> removedEntityTypes = [.. baselineOrder.Except(currentOrder).OrderBy(name => name, StringComparer.Ordinal)];

        List<AutoSeedRowCountChange> rowCountChanges = [];
        foreach (string entityTypeName in baselineOrder.Intersect(currentOrder).OrderBy(name => name, StringComparer.Ordinal))
        {
            int baselineRowCount = baseline.RowCounts.GetValueOrDefault(entityTypeName);
            int currentRowCount = current.RowCounts.GetValueOrDefault(entityTypeName);
            if (baselineRowCount != currentRowCount)
            {
                rowCountChanges.Add(new AutoSeedRowCountChange(entityTypeName, baselineRowCount, currentRowCount));
            }
        }

        Dictionary<string, string> baselineSkipReasons = baseline.SkippedEntityTypes.ToDictionary(
            skipped => skipped.EntityTypeName, skipped => skipped.Reason);
        Dictionary<string, string> currentSkipReasons = current.SkippedEntityTypes.ToDictionary(
            skipped => skipped.EntityTypeName, skipped => skipped.Reason);

        List<AutoSeedSkipChange> skipChanges = [];
        foreach (string entityTypeName in baselineSkipReasons.Keys.Union(currentSkipReasons.Keys).OrderBy(name => name, StringComparer.Ordinal))
        {
            baselineSkipReasons.TryGetValue(entityTypeName, out string? baselineReason);
            currentSkipReasons.TryGetValue(entityTypeName, out string? currentReason);
            if (baselineReason != currentReason)
            {
                skipChanges.Add(new AutoSeedSkipChange(entityTypeName, baselineReason, currentReason));
            }
        }

        HashSet<AutoSeedPlanCycle> baselineCycles = [.. baseline.Cycles];
        HashSet<AutoSeedPlanCycle> currentCycles = [.. current.Cycles];
        List<AutoSeedPlanCycle> addedCycles = [.. currentCycles.Except(baselineCycles)
            .OrderBy(cycle => cycle.DependentEntityTypeName, StringComparer.Ordinal)
            .ThenBy(cycle => cycle.PrincipalEntityTypeName, StringComparer.Ordinal)];
        List<AutoSeedPlanCycle> removedCycles = [.. baselineCycles.Except(currentCycles)
            .OrderBy(cycle => cycle.DependentEntityTypeName, StringComparer.Ordinal)
            .ThenBy(cycle => cycle.PrincipalEntityTypeName, StringComparer.Ordinal)];

        return new AutoSeedDiffResult(addedEntityTypes, removedEntityTypes, rowCountChanges, skipChanges, addedCycles, removedCycles);
    }
}
