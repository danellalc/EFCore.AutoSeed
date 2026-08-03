using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using NpgsqlTypes;

namespace EFCore.AutoSeed.Providers;

/// <summary>
/// Bulk-inserts rows into PostgreSQL via <see cref="NpgsqlBinaryImporter"/> (<c>COPY ... FROM STDIN (FORMAT BINARY)</c>).
/// </summary>
public sealed class PostgreSqlBulkInsertProvider : IBulkInsertProvider
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

        NpgsqlConnection connection = (NpgsqlConnection)context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<IProperty> properties = [.. entityType.GetProperties()];
        string columnList = string.Join(", ", properties.Select(property => Quote(property.GetColumnName())));
        string copyCommand = $"COPY {QualifiedTableName(entityType)} ({columnList}) FROM STDIN (FORMAT BINARY)";

        await using NpgsqlBinaryImporter importer = await connection.BeginBinaryImportAsync(copyCommand, cancellationToken).ConfigureAwait(false);
        foreach (IReadOnlyDictionary<string, object> row in rows)
        {
            await importer.StartRowAsync(cancellationToken).ConfigureAwait(false);
            foreach (IProperty property in properties)
            {
                object? value = row.TryGetValue(property.Name, out object? found) ? found : null;
                await WriteValueAsync(importer, value, property.ClrType, cancellationToken).ConfigureAwait(false);
            }
        }

        await importer.CompleteAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string QualifiedTableName(IEntityType entityType)
    {
        string tableName = entityType.GetTableName()
            ?? throw new InvalidOperationException($"'{entityType.Name}' has no mapped table.");
        string? schema = entityType.GetSchema();

        return schema is null ? Quote(tableName) : $"{Quote(schema)}.{Quote(tableName)}";
    }

    private static string Quote(string identifier) => $"\"{identifier}\"";

    private static async Task WriteValueAsync(NpgsqlBinaryImporter importer, object? value, Type clrType, CancellationToken cancellationToken)
    {
        if (value is null)
        {
            await importer.WriteNullAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        Type type = Nullable.GetUnderlyingType(clrType) ?? clrType;
        if (type.IsEnum)
        {
            await importer.WriteAsync(Convert.ToInt32(value), NpgsqlDbType.Integer, cancellationToken).ConfigureAwait(false);
            return;
        }

        switch (Type.GetTypeCode(type))
        {
            case TypeCode.Int32:
                await importer.WriteAsync((int)value, NpgsqlDbType.Integer, cancellationToken).ConfigureAwait(false);
                break;
            case TypeCode.Int64:
                await importer.WriteAsync((long)value, NpgsqlDbType.Bigint, cancellationToken).ConfigureAwait(false);
                break;
            case TypeCode.Int16:
                await importer.WriteAsync((short)value, NpgsqlDbType.Smallint, cancellationToken).ConfigureAwait(false);
                break;
            case TypeCode.Byte:
                await importer.WriteAsync((short)(byte)value, NpgsqlDbType.Smallint, cancellationToken).ConfigureAwait(false);
                break;
            case TypeCode.Boolean:
                await importer.WriteAsync((bool)value, NpgsqlDbType.Boolean, cancellationToken).ConfigureAwait(false);
                break;
            case TypeCode.Decimal:
                await importer.WriteAsync((decimal)value, NpgsqlDbType.Numeric, cancellationToken).ConfigureAwait(false);
                break;
            case TypeCode.Double:
                await importer.WriteAsync((double)value, NpgsqlDbType.Double, cancellationToken).ConfigureAwait(false);
                break;
            case TypeCode.Single:
                await importer.WriteAsync((float)value, NpgsqlDbType.Real, cancellationToken).ConfigureAwait(false);
                break;
            case TypeCode.String:
                await importer.WriteAsync((string)value, NpgsqlDbType.Text, cancellationToken).ConfigureAwait(false);
                break;
            case TypeCode.DateTime:
                await importer.WriteAsync((DateTime)value, NpgsqlDbType.Timestamp, cancellationToken).ConfigureAwait(false);
                break;
            default:
                if (type == typeof(Guid))
                {
                    await importer.WriteAsync((Guid)value, NpgsqlDbType.Uuid, cancellationToken).ConfigureAwait(false);
                    break;
                }

                throw new NotSupportedException($"Unsupported CLR type '{type}' for PostgreSQL bulk insert.");
        }
    }
}
