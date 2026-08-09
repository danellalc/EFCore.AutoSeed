namespace EFCore.AutoSeed.Cli;

internal static class CliApplication
{
    private const string TopLevelUsage =
        """
        autoseed: seed and inspect an EF Core model.

        Usage:
          autoseed explain --context <FullTypeName> --assembly <path-to-dll> [--seed <long>] [--scale <int>]
          autoseed capture --context <FullTypeName> --assembly <path-to-dll> --output <path-to-json>
          autoseed apply --context <FullTypeName> --assembly <path-to-dll> --shape <path-to-json> [--seed <long>] [--scale <int>]
          autoseed diff --context <FullTypeName> --assembly <path-to-dll> --baseline <path-to-json> [--seed <long>] [--scale <int>] [--update-baseline]

        Run 'autoseed <command> --help' for details on a specific command.
        """;

    internal static async Task<int> RunAsync(string[] args)
    {
        try
        {
            return await DispatchAsync(args).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"autoseed failed unexpectedly: {exception.GetType().Name}: {exception.Message}");
            return CliExitCodes.UnexpectedError;
        }
    }

    private static async Task<int> DispatchAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            Console.WriteLine(TopLevelUsage);
            return CliExitCodes.Success;
        }

        string command = args[0];
        string[] remaining = args[1..];

        return command switch
        {
            "explain" => await ExplainCommand.RunAsync(remaining).ConfigureAwait(false),
            "capture" => await CaptureCommand.RunAsync(remaining).ConfigureAwait(false),
            "apply" => await ApplyCommand.RunAsync(remaining).ConfigureAwait(false),
            "diff" => await DiffCommand.RunAsync(remaining).ConfigureAwait(false),
            _ => UnknownCommand(command),
        };
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'. Only 'explain', 'capture', 'apply' and 'diff' are currently implemented.");
        Console.Error.WriteLine();
        Console.Error.WriteLine(TopLevelUsage);
        return CliExitCodes.UsageError;
    }
}
