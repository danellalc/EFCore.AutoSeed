using EFCore.AutoSeed.Exceptions;
using EFCore.AutoSeed.Shape;
using Microsoft.EntityFrameworkCore;

namespace EFCore.AutoSeed.Cli;

internal static class CaptureCommand
{
    private static readonly HashSet<string> KnownFlagNames = new(StringComparer.Ordinal)
    {
        "context", "assembly", "output",
    };

    private const string Usage =
        """
        Usage: autoseed capture --context <FullTypeName> --assembly <path-to-dll> --output <path-to-json>

        Reads each table's row count from the database engine's own maintained statistics
        (never an actual data row) and writes them to a shape file.

        Options:
          --context   <string>  Full name of the DbContext type to load. Required.
          --assembly  <path>    Path to the assembly (.dll) containing the DbContext. Required.
          --output    <path>    Path to write the captured shape to. Required.
          -h, --help            Show this message.

        Only SQL Server and PostgreSQL are supported. --assembly must point at a 'dotnet publish'
        output (or an executable project's build output); a plain 'dotnet build' output of a class
        library does not copy its NuGet package dependencies locally, so the DbContext will fail
        to load.
        """;

    internal static async Task<int> RunAsync(IReadOnlyList<string> args)
    {
        if (args.Any(argument => argument is "--help" or "-h"))
        {
            Console.WriteLine(Usage);
            return CliExitCodes.Success;
        }

        Dictionary<string, string> flags;
        try
        {
            flags = CliFlags.Parse(args, KnownFlagNames);
        }
        catch (FormatException exception)
        {
            return UsageFailure(exception.Message);
        }

        if (!flags.TryGetValue("context", out string? contextTypeName) || string.IsNullOrWhiteSpace(contextTypeName))
        {
            return UsageFailure("Missing required option --context.");
        }

        if (!flags.TryGetValue("assembly", out string? assemblyPath) || string.IsNullOrWhiteSpace(assemblyPath))
        {
            return UsageFailure("Missing required option --assembly.");
        }

        if (!flags.TryGetValue("output", out string? outputPath) || string.IsNullOrWhiteSpace(outputPath))
        {
            return UsageFailure("Missing required option --output.");
        }

        DbContextLoadResult loadResult = DbContextLoader.Load(assemblyPath, contextTypeName);
        if (loadResult.Context is not DbContext loadedContext)
        {
            Console.Error.WriteLine(loadResult.ErrorMessage ?? $"Could not load '{contextTypeName}' from '{assemblyPath}'.");
            return CliExitCodes.ContextResolutionError;
        }

        await using DbContext context = loadedContext;
        try
        {
            ShapeCapture shape = await context.CaptureShapeAsync().ConfigureAwait(false);
            await ShapeFile.WriteAsync(shape, outputPath).ConfigureAwait(false);
            Console.WriteLine($"Captured {shape.Tables.Count} table(s) to '{outputPath}'.");
            return CliExitCodes.Success;
        }
        catch (AutoSeedException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return CliExitCodes.ModelError;
        }
        catch (Exception exception) when (exception is FileNotFoundException or FileLoadException)
        {
            Console.Error.WriteLine(
                $"Could not load a dependency of '{contextTypeName}': {exception.Message} " +
                "This usually means --assembly points at a plain 'dotnet build' output for a class " +
                "library project, which does not copy its NuGet package dependencies locally. Point " +
                "--assembly at a 'dotnet publish' output instead, or at an executable project's build output.");
            return CliExitCodes.ContextResolutionError;
        }
    }

    private static int UsageFailure(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine();
        Console.Error.WriteLine(Usage);
        return CliExitCodes.UsageError;
    }
}
