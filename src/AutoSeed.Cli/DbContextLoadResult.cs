using Microsoft.EntityFrameworkCore;

namespace EFCore.AutoSeed.Cli;

internal readonly struct DbContextLoadResult
{
    private DbContextLoadResult(bool succeeded, DbContext? context, string? errorMessage)
    {
        Succeeded = succeeded;
        Context = context;
        ErrorMessage = errorMessage;
    }

    internal bool Succeeded { get; }
    internal DbContext? Context { get; }
    internal string? ErrorMessage { get; }

    internal static DbContextLoadResult Success(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new DbContextLoadResult(true, context, null);
    }

    internal static DbContextLoadResult Failure(string errorMessage) => new(false, null, errorMessage);
}
