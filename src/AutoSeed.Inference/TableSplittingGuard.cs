using EFCore.AutoSeed.Exceptions;
using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore;

namespace EFCore.AutoSeed.Inference;

/// <summary>
/// Rejects true table splitting: two entity types mapped to the very same table through a
/// shared-primary-key one-to-one, so generating a row for each independently would insert into
/// that table twice for what has to be a single row. <see cref="Pipeline.ModelReader"/> cannot
/// detect this itself, because knowing a table name needs
/// <c>Microsoft.EntityFrameworkCore.Relational</c>, which <c>AutoSeed.Core</c> never references.
/// </summary>
public static class TableSplittingGuard
{
    /// <summary>
    /// Throws if any of <paramref name="edges"/> connects two entity types mapped to the same
    /// table through a shared primary key.
    /// </summary>
    /// <param name="edges">Every dependency edge <see cref="Pipeline.ModelReader"/> found.</param>
    /// <exception cref="ArgumentNullException"><paramref name="edges"/> is <see langword="null"/>.</exception>
    /// <exception cref="UnsupportedEntityTypeException">
    /// Two entity types are table-split: both map to the same table through a shared primary key.
    /// </exception>
    public static void EnsureNoTableSplitting(IReadOnlyList<GraphEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(edges);

        foreach (GraphEdge edge in edges)
        {
            if (!SharedPrimaryKey.IsDependent(edge.Dependent, edge.ForeignKey)
                || edge.Dependent.GetSchema() != edge.Principal.GetSchema()
                || edge.Dependent.GetTableName() != edge.Principal.GetTableName())
            {
                continue;
            }

            throw new UnsupportedEntityTypeException(
                edge.Dependent.Name,
                $"it is table-split with '{edge.Principal.Name}': both map to the same table " +
                $"'{edge.Dependent.GetTableName()}' through a shared primary key, and seeding two entity types that " +
                "occupy a single row together is not supported yet");
        }
    }
}
