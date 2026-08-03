namespace EFCore.AutoSeed.Exceptions;

/// <summary>
/// Thrown when fast mode (<c>AutoSeedFastAsync</c>) is used against a <see cref="Microsoft.EntityFrameworkCore.DbContext"/>
/// whose configured EF Core provider has no bulk insert implementation.
/// </summary>
public sealed class UnsupportedProviderException : AutoSeedException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnsupportedProviderException"/> class.
    /// </summary>
    /// <param name="providerName">The EF Core provider name reported by <c>Database.ProviderName</c>.</param>
    public UnsupportedProviderException(string providerName)
        : base($"Fast mode has no bulk insert provider for '{providerName}'. Use AutoSeedAsync instead, or SQL Server / PostgreSQL.")
    {
        ProviderName = providerName;
    }

    /// <summary>
    /// The EF Core provider name reported by <c>Database.ProviderName</c>.
    /// </summary>
    public string ProviderName { get; }
}
