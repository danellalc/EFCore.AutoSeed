using EFCore.AutoSeed.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EFCore.AutoSeed.Shape;

/// <summary>
/// Picks the <see cref="IShapeCaptureProvider"/> matching a <see cref="DbContext"/>'s configured
/// EF Core provider.
/// </summary>
public static class ShapeCaptureProviderFactory
{
    private const string SqlServerProviderName = "Microsoft.EntityFrameworkCore.SqlServer";
    private const string NpgsqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";

    /// <summary>
    /// Creates the <see cref="IShapeCaptureProvider"/> for <paramref name="context"/>.
    /// </summary>
    /// <param name="context">The context whose configured provider to match.</param>
    /// <returns>A provider able to read row counts from <paramref name="context"/>'s database.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="UnsupportedProviderException">
    /// <paramref name="context"/>'s configured provider is neither SQL Server nor PostgreSQL.
    /// </exception>
    public static IShapeCaptureProvider Create(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Database.ProviderName switch
        {
            SqlServerProviderName => new SqlServerShapeCaptureProvider(),
            NpgsqlProviderName => new PostgreSqlShapeCaptureProvider(),
            var providerName => throw new UnsupportedProviderException(providerName ?? "(none)"),
        };
    }
}
