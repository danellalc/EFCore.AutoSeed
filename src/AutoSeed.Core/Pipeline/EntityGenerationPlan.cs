using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Pipeline;

/// <summary>
/// How many rows to generate for one entity type, and how those rows distribute across its
/// driving principal's rows when it has one.
/// </summary>
/// <param name="EntityType">The entity type being planned for.</param>
/// <param name="RowCount">How many rows to generate in total.</param>
/// <param name="Driver">
/// The principal entity type this row count was derived from, or <see langword="null"/> if
/// <paramref name="EntityType"/> has no required principal and got <c>scale</c> rows directly.
/// </param>
/// <param name="ChildCountsByDriverRow">
/// How many of <paramref name="EntityType"/>'s rows belong to each of <paramref name="Driver"/>'s
/// rows, in driver row order. <see langword="null"/> when <paramref name="Driver"/> is <see langword="null"/>.
/// </param>
public sealed record EntityGenerationPlan(
    IEntityType EntityType,
    int RowCount,
    IEntityType? Driver,
    IReadOnlyList<int>? ChildCountsByDriverRow);
