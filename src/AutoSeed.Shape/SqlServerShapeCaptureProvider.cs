using System.Data;
using EFCore.AutoSeed.Exceptions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Shape;

/// <summary>
/// Reads row counts from SQL Server's <c>sys.dm_db_partition_stats</c>, the same maintained
/// counters the query optimizer itself reads. Never issues a query against the table.
/// </summary>
/// <remarks>
/// The connecting login needs <c>VIEW DATABASE STATE</c> to query this DMV at all, and, per table,
/// either <c>SELECT</c> or <c>VIEW DEFINITION</c>: SQL Server hides a table's rows from
/// <c>sys.dm_db_partition_stats</c> entirely for a login with neither, even with
/// <c>VIEW DATABASE STATE</c> granted. <c>VIEW DEFINITION</c> alone (no <c>SELECT</c>) is enough:
/// it grants schema-only visibility, never access to a row's actual values.
/// </remarks>
public sealed class SqlServerShapeCaptureProvider : IShapeCaptureProvider
{
    private const string RowCountQuery =
        """
        SELECT SUM(ps.row_count)
        FROM sys.dm_db_partition_stats ps
        WHERE ps.object_id = OBJECT_ID(@table) AND ps.index_id IN (0, 1)
        """;

    /// <inheritdoc />
    public async Task<long> GetRowCountAsync(DbContext context, IEntityType entityType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entityType);

        SqlConnection connection = (SqlConnection)context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await using SqlCommand command = new(RowCountQuery, connection);
        command.Parameters.AddWithValue("@table", QualifiedTableName(entityType));

        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null or DBNull ? 0 : Convert.ToInt64(result);
    }

    private static string QualifiedTableName(IEntityType entityType)
    {
        string tableName = entityType.GetTableName()
            ?? throw new UnsupportedEntityTypeException(entityType.Name, "has no mapped table");
        string? schema = entityType.GetSchema();

        return schema is null ? Bracket(tableName) : $"{Bracket(schema)}.{Bracket(tableName)}";
    }

    private static string Bracket(string identifier) => $"[{identifier.Replace("]", "]]")}]";
}
