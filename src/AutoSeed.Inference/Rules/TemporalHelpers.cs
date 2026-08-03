using EFCore.AutoSeed.Distributions;
using EFCore.AutoSeed.Pipeline;

namespace EFCore.AutoSeed.Inference.Rules;

internal static class TemporalHelpers
{
    internal static DateTime Between(SeededRandom random, DateTime start, DateTime end, TemporalClusteringOptions options) =>
        TemporalClustering.Between(random, start, end, options);

    internal static DateTime? FindSiblingDateTime(IReadOnlyDictionary<string, object> generatedValues, params ReadOnlySpan<string> nameSuffixes)
    {
        foreach (KeyValuePair<string, object> entry in generatedValues)
        {
            if (entry.Value is not DateTime dateTime)
            {
                continue;
            }

            foreach (string suffix in nameSuffixes)
            {
                if (entry.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return dateTime;
                }
            }
        }

        return null;
    }
}
