using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Shape;

/// <summary>
/// Reads a single entity type's approximate row count from the database engine's own maintained
/// statistics, without ever querying the table's actual rows.
/// </summary>
public interface IShapeCaptureProvider
{
    /// <summary>
    /// Reads <paramref name="entityType"/>'s approximate row count.
    /// </summary>
    /// <param name="context">The context whose underlying connection to read through.</param>
    /// <param name="entityType">The entity type to read a row count for.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The approximate row count reported by the database engine's own statistics.</returns>
    Task<long> GetRowCountAsync(DbContext context, IEntityType entityType, CancellationToken cancellationToken);
}
