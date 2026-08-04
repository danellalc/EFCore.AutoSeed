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
    /// <param name="rows">
    /// The rows to insert, one dictionary per row, each keyed by column name
    /// (<c>property.GetColumnName()</c>), not by property name: two different owned navigations can
    /// reuse the same owned CLR type, and therefore the same property name, so only the column name
    /// is guaranteed unique. Includes <paramref name="entityType"/>'s own columns plus every owned
    /// type's columns, flattened; every column the table needs must already be present.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task InsertAsync(
        DbContext context,
        IEntityType entityType,
        IReadOnlyList<IReadOnlyDictionary<string, object>> rows,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the current maximum value already stored in <paramref name="entityType"/>'s int or long
    /// identity primary key column, so a bulk insert into a table that already has rows (for example
    /// a second seeding run) can continue assigning values from there instead of restarting at 1 and
    /// colliding with what is already in the table.
    /// </summary>
    /// <param name="context">The context whose underlying connection to read through.</param>
    /// <param name="entityType">The entity type whose table to inspect.</param>
    /// <param name="keyColumnName">The identity primary key column's name.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The column's current maximum value, or 0 if the table has no rows yet.</returns>
    Task<long> GetMaxIdentityValueAsync(
        DbContext context, IEntityType entityType, string keyColumnName, CancellationToken cancellationToken);
}
