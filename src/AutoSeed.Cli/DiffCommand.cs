using EFCore.AutoSeed.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EFCore.AutoSeed.Cli;

internal static class DiffCommand
{
    private const long DefaultSeed = 42;
    private const int DefaultScale = 1000;

    private static readonly HashSet<string> KnownFlagNames = new(StringComparer.Ordinal)
    {
        "context", "assembly", "baseline", "seed", "scale",
    };

    private const string Usage =
        """
        Usage: autoseed diff --context <FullTypeName> --assembly <path-to-dll> --baseline <path-to-json>
                              [--seed <long>] [--scale <int>] [--update-baseline]

        Compares the model's current seeding plan against a saved baseline, so a change that would
        alter what gets seeded (a new required property with no rule for it, a newly-introduced
        cycle, an entity type that stopped or started being seedable) shows up as an explicit,
        reviewable diff. Exits non-zero if a difference is found, for use as a CI gate.

        Options:
          --context          <string>  Full name of the DbContext type to load. Required.
          --assembly         <path>    Path to the assembly (.dll) containing the DbContext. Required.
          --baseline         <path>    Path to the saved baseline plan (JSON). Required.
          --seed             <long>    Seed the plan's cardinality draws derive from. Default: 42.
          --scale            <int>     Row count for entity types with no required principal. Default: 1000.
          --update-baseline             Write the current plan to --baseline instead of diffing against it.
          -h, --help                    Show this message.

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

        bool updateBaseline = args.Contains("--update-baseline");
        List<string> remainingArgs = [.. args.Where(argument => argument != "--update-baseline")];

        Dictionary<string, string> flags;
        try
        {
            flags = CliFlags.Parse(remainingArgs, KnownFlagNames);
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

        if (!flags.TryGetValue("baseline", out string? baselinePath) || string.IsNullOrWhiteSpace(baselinePath))
        {
            return UsageFailure("Missing required option --baseline.");
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

        if (!updateBaseline && !File.Exists(baselinePath))
        {
            Console.Error.WriteLine(
                $"Baseline file '{baselinePath}' does not exist yet. Run this same command with --update-baseline " +
                "to create it from the current plan, commit it, then run without --update-baseline to diff against it.");
            return CliExitCodes.UsageError;
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
            AutoSeedPlanSnapshot current = AutoSeedPlanSnapshot.FromExplainResult(result);

            if (updateBaseline)
            {
                await AutoSeedPlanFile.WriteAsync(current, baselinePath).ConfigureAwait(false);
                Console.WriteLine($"Wrote the current plan to '{baselinePath}'.");
                return CliExitCodes.Success;
            }

            AutoSeedPlanSnapshot baseline = await AutoSeedPlanFile.ReadAsync(baselinePath).ConfigureAwait(false);
            AutoSeedDiffResult diff = AutoSeedDiff.Compare(baseline, current);
            Console.WriteLine(diff.ToReport());
            return diff.HasChanges ? CliExitCodes.DiffFound : CliExitCodes.Success;
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
