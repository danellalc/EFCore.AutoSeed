using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Providers;

/// <summary>
/// Writes already-generated, already-FK-resolved rows for one entity type straight into the
/// database, bypassing <see cref="DbContext.SaveChangesAsync(System.Threading.CancellationToken)"/>
/// and its change tracker entirely.
/// </summary>
public interface IBulkInsertProvider
{
    /// <summary>
    /// Bulk-inserts <paramref name="rows"/> into the table <paramref name="entityType"/> maps to.
    /// </summary>
    /// <param name="context">The context whose underlying connection to write through.</param>
    /// <param name="entityType">The entity type <paramref name="rows"/> were generated for.</param>
    /// <param name="rows">The rows to insert, keyed by property name; every column the table needs must already be present.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task InsertAsync(
        DbContext context,
        IEntityType entityType,
        IReadOnlyList<IReadOnlyDictionary<string, object>> rows,
        CancellationToken cancellationToken);
}
