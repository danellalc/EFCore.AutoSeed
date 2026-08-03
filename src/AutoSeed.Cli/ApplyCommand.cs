using EFCore.AutoSeed.Exceptions;
using EFCore.AutoSeed.Shape;
using Microsoft.EntityFrameworkCore;

namespace EFCore.AutoSeed.Cli;

internal static class ApplyCommand
{
    private const long DefaultSeed = 42;
    private const int DefaultScale = 1000;

    private const string Usage =
        """
        Usage: autoseed apply --context <FullTypeName> --assembly <path-to-dll> --shape <path-to-json> [--seed <long>] [--scale <int>]

        Seeds the database like AutoSeedAsync, but a table present in the shape file uses a row
        count proportional to its captured row count relative to the largest captured table,
        instead of --scale directly, so the seeded database's relative table sizes resemble where
        the shape was captured from.

        Options:
          --context   <string>  Full name of the DbContext type to load. Required.
          --assembly  <path>    Path to the assembly (.dll) containing the DbContext. Required.
          --shape     <path>    Path to a shape file written by 'autoseed capture'. Required.
          --seed      <long>    Seed every generated value derives from. Default: 42.
          --scale     <int>     Row count for the largest captured table, and for any table the
                                shape did not capture. Default: 1000.
          -h, --help            Show this message.

        --assembly must point at a 'dotnet publish' output (or an executable project's build
        output). A plain 'dotnet build' output of a class library does not copy its NuGet package
        dependencies locally, so the DbContext will fail to load.
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
            flags = CliFlags.Parse(args);
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

        if (!flags.TryGetValue("shape", out string? shapePath) || string.IsNullOrWhiteSpace(shapePath))
        {
            return UsageFailure("Missing required option --shape.");
        }

        long seed = DefaultSeed;
        if (flags.TryGetValue("seed", out string? seedText) && !long.TryParse(seedText, out seed))
        {
            return UsageFailure($"Invalid --seed value '{seedText}': expected an integer.");
        }

        int scale = DefaultScale;
        if (flags.TryGetValue("scale", out string? scaleText) && (!int.TryParse(scaleText, out scale) || scale <= 0))
        {
            return UsageFailure($"Invalid --scale value '{scaleText}': expected a positive integer.");
        }

        if (!File.Exists(shapePath))
        {
            Console.Error.WriteLine($"Shape file not found at '{shapePath}'.");
            return CliExitCodes.ContextResolutionError;
        }

        ShapeCapture shape;
        try
        {
            shape = await ShapeFile.ReadAsync(shapePath).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Could not read shape file '{shapePath}': {exception.Message}");
            return CliExitCodes.ContextResolutionError;
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
            IReadOnlyDictionary<string, int> result = await context.AutoSeedFromShapeAsync(seed, shape, scale).ConfigureAwait(false);
            foreach (KeyValuePair<string, int> entry in result.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                Console.WriteLine($"{entry.Key}: {entry.Value} rows");
            }

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
