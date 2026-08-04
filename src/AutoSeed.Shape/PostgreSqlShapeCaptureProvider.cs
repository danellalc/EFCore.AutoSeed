using EFCore.AutoSeed.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

namespace EFCore.AutoSeed.Shape;

/// <summary>
/// Reads row counts from PostgreSQL's <c>pg_class.reltuples</c>, the same maintained estimate the
/// query planner itself reads (kept current by <c>ANALYZE</c> and autovacuum). Never issues a
/// query against the table.
/// </summary>
public sealed class PostgreSqlShapeCaptureProvider : IShapeCaptureProvider
{
    private const string RowCountQuery = "SELECT reltuples FROM pg_class WHERE oid = to_regclass(@table)";

    /// <inheritdoc />
    public async Task<long> GetRowCountAsync(DbContext context, IEntityType entityType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entityType);

        NpgsqlConnection connection = (NpgsqlConnection)context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await using NpgsqlCommand command = new(RowCountQuery, connection);
        command.Parameters.AddWithValue("table", QualifiedTableName(entityType));

        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (result is null or DBNull)
        {
            return 0;
        }

        float estimate = Convert.ToSingle(result);
        return estimate < 0 ? 0 : Convert.ToInt64(estimate);
    }

    private static string QualifiedTableName(IEntityType entityType)
    {
        string tableName = entityType.GetTableName()
            ?? throw new UnsupportedEntityTypeException(entityType.Name, "has no mapped table");
        string? schema = entityType.GetSchema();

        return schema is null ? Quote(tableName) : $"{Quote(schema)}.{Quote(tableName)}";
    }

    private static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";
}
