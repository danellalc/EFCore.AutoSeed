using System.Data;
using EFCore.AutoSeed.Exceptions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace EFCore.AutoSeed.Providers;

/// <summary>
/// Bulk-inserts rows into SQL Server via <see cref="SqlBulkCopy"/>.
/// </summary>
public sealed class SqlServerBulkInsertProvider : IBulkInsertProvider
{
    /// <inheritdoc />
    public async Task InsertAsync(
        DbContext context, IEntityType entityType, IReadOnlyList<IReadOnlyDictionary<string, object>> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(rows);

        if (rows.Count == 0)
        {
            return;
        }

        string destinationTableName = QualifiedTableName(entityType);
        IReadOnlyList<(IProperty Property, string ColumnName)> columns = BulkPersistence.GetFlattenedColumns(entityType);

        SqlConnection connection = (SqlConnection)context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        SqlTransaction? transaction = context.Database.CurrentTransaction?.GetDbTransaction() as SqlTransaction;

        using DataTable table = BuildDataTable(columns, rows);
        using SqlBulkCopy bulkCopy = new(connection, SqlBulkCopyOptions.KeepIdentity, transaction)
        {
            DestinationTableName = destinationTableName,
        };

        foreach ((_, string columnName) in columns)
        {
            bulkCopy.ColumnMappings.Add(columnName, columnName);
        }

        await bulkCopy.WriteToServerAsync(table, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<long> GetMaxIdentityValueAsync(
        DbContext context, IEntityType entityType, string keyColumnName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(keyColumnName);

        string query = $"SELECT COALESCE(MAX({Bracket(keyColumnName)}), 0) FROM {QualifiedTableName(entityType)}";

        SqlConnection connection = (SqlConnection)context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        SqlTransaction? transaction = context.Database.CurrentTransaction?.GetDbTransaction() as SqlTransaction;

        await using SqlCommand command = new(query, connection, transaction);
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

    private static DataTable BuildDataTable(
        IReadOnlyList<(IProperty Property, string ColumnName)> columns, IReadOnlyList<IReadOnlyDictionary<string, object>> rows)
    {
        DataTable table = new();
        foreach ((IProperty property, string columnName) in columns)
        {
            Type columnType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            table.Columns.Add(columnName, columnType.IsEnum ? typeof(int) : columnType);
        }

        foreach (IReadOnlyDictionary<string, object> row in rows)
        {
            DataRow dataRow = table.NewRow();
            foreach ((_, string columnName) in columns)
            {
                dataRow[columnName] = row.TryGetValue(columnName, out object? value)
                    ? (value.GetType().IsEnum ? Convert.ToInt32(value) : value)
                    : DBNull.Value;
            }

            table.Rows.Add(dataRow);
        }

        return table;
    }
}
