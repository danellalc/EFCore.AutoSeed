namespace EFCore.AutoSeed.Cli;

internal static class CliExitCodes
{
    internal const int Success = 0;
    internal const int UsageError = 1;
    internal const int ContextResolutionError = 2;
    internal const int ModelError = 3;
    internal const int UnexpectedError = 4;
    internal const int DiffFound = 5;
}
