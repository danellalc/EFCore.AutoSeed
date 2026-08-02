using EFCore.AutoSeed.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EFCore.AutoSeed.Cli;

internal static class ExplainCommand
{
    private const long DefaultSeed = 42;
    private const int DefaultScale = 1000;

    private const string Usage =
        """
        Usage: autoseed explain --context <FullTypeName> --assembly <path-to-dll> [--seed <long>] [--scale <int>]

        Reads the DbContext's model and prints the seeding plan without writing anything to the database.

        Options:
          --context   <string>  Full name of the DbContext type to load. Required.
          --assembly  <path>    Path to the assembly (.dll) containing the DbContext. Required.
          --seed      <long>    Seed every generated value derives from. Default: 42.
          --scale     <int>     Row count for entity types with no required principal. Default: 1000.
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
            flags = ParseFlags(args);
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

        DbContextLoadResult loadResult = DbContextLoader.Load(assemblyPath, contextTypeName);
        if (loadResult.Context is not DbContext loadedContext)
        {
            Console.Error.WriteLine(loadResult.ErrorMessage ?? $"Could not load '{contextTypeName}' from '{assemblyPath}'.");
            return CliExitCodes.ContextResolutionError;
        }

        await using DbContext context = loadedContext;
        try
        {
            AutoSeedExplainResult result = await context.AutoSeedExplainAsync(seed, scale).ConfigureAwait(false);
            Console.WriteLine(result.ToReport());
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

    private static Dictionary<string, string> ParseFlags(IReadOnlyList<string> args)
    {
        Dictionary<string, string> flags = new(StringComparer.Ordinal);
        int index = 0;
        while (index < args.Count)
        {
            string token = args[index];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                throw new FormatException($"Unexpected argument '{token}'.");
            }

            string name = token[2..];
            if (index + 1 >= args.Count)
            {
                throw new FormatException($"Option --{name} requires a value.");
            }

            flags[name] = args[index + 1];
            index += 2;
        }

        return flags;
    }
}
