using EFCore.AutoSeed.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EFCore.AutoSeed.Providers;

/// <summary>
/// Picks the <see cref="IBulkInsertProvider"/> matching a <see cref="DbContext"/>'s configured EF
/// Core provider.
/// </summary>
public static class BulkInsertProviderFactory
{
    private const string SqlServerProviderName = "Microsoft.EntityFrameworkCore.SqlServer";
    private const string NpgsqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";

    /// <summary>
    /// Creates the <see cref="IBulkInsertProvider"/> for <paramref name="context"/>.
    /// </summary>
    /// <param name="context">The context whose configured provider to match.</param>
    /// <returns>A provider able to bulk-insert into <paramref name="context"/>'s database.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="UnsupportedProviderException">
    /// <paramref name="context"/>'s configured provider is neither SQL Server nor PostgreSQL.
    /// </exception>
    public static IBulkInsertProvider Create(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Database.ProviderName switch
        {
            SqlServerProviderName => new SqlServerBulkInsertProvider(),
            NpgsqlProviderName => new PostgreSqlBulkInsertProvider(),
            var providerName => throw new UnsupportedProviderException(providerName ?? "(none)"),
        };
    }
}
