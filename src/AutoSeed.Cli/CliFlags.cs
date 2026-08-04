namespace EFCore.AutoSeed.Cli;

internal static class CliFlags
{
    internal static Dictionary<string, string> Parse(IReadOnlyList<string> args, IReadOnlySet<string> knownNames)
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
            if (!knownNames.Contains(name))
            {
                throw new FormatException($"Unrecognized option '--{name}'.");
            }

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
