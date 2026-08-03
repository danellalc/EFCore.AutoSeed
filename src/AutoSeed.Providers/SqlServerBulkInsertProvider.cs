using System.Data;
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

        SqlConnection connection = (SqlConnection)context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        SqlTransaction? transaction = context.Database.CurrentTransaction?.GetDbTransaction() as SqlTransaction;
        IReadOnlyList<IProperty> properties = [.. entityType.GetProperties()];

        using DataTable table = BuildDataTable(properties, rows);
        using SqlBulkCopy bulkCopy = new(connection, SqlBulkCopyOptions.KeepIdentity, transaction)
        {
            DestinationTableName = QualifiedTableName(entityType),
        };

        foreach (IProperty property in properties)
        {
            string columnName = property.GetColumnName();
            bulkCopy.ColumnMappings.Add(columnName, columnName);
        }

        await bulkCopy.WriteToServerAsync(table, cancellationToken).ConfigureAwait(false);
    }

    private static string QualifiedTableName(IEntityType entityType)
    {
        string tableName = entityType.GetTableName()
            ?? throw new InvalidOperationException($"'{entityType.Name}' has no mapped table.");
        string? schema = entityType.GetSchema();

        return schema is null ? $"[{tableName}]" : $"[{schema}].[{tableName}]";
    }

    private static DataTable BuildDataTable(IReadOnlyList<IProperty> properties, IReadOnlyList<IReadOnlyDictionary<string, object>> rows)
    {
        DataTable table = new();
        foreach (IProperty property in properties)
        {
            Type columnType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            table.Columns.Add(property.GetColumnName(), columnType.IsEnum ? typeof(int) : columnType);
        }

        foreach (IReadOnlyDictionary<string, object> row in rows)
        {
            DataRow dataRow = table.NewRow();
            foreach (IProperty property in properties)
            {
                dataRow[property.GetColumnName()] = row.TryGetValue(property.Name, out object? value)
                    ? (value.GetType().IsEnum ? Convert.ToInt32(value) : value)
                    : DBNull.Value;
            }

            table.Rows.Add(dataRow);
        }

        return table;
    }
}
