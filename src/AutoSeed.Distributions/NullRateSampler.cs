using EFCore.AutoSeed.Pipeline;

namespace EFCore.AutoSeed.Distributions;

/// <summary>
/// Decides, for a single nullable property on a single row, whether to leave it null instead of
/// whatever value an inference rule would otherwise produce. Reflects that in real data, most
/// nullable columns are not populated on every row.
/// </summary>
public static class NullRateSampler
{
    /// <summary>
    /// The fraction of eligible nullable properties left null when no explicit rate is supplied.
    /// </summary>
    public const double DefaultRate = 0.1;

    /// <summary>
    /// Draws whether a property's generated value should be discarded in favor of null.
    /// </summary>
    /// <param name="random">The random source this draw derives from.</param>
    /// <param name="rate">The probability of returning <see langword="true"/>, in <c>[0, 1]</c>.</param>
    /// <returns><see langword="true"/> if the property should be left null for this row.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="random"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rate"/> is outside <c>[0, 1]</c>.</exception>
    public static bool ShouldLeaveNull(SeededRandom random, double rate = DefaultRate)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (rate is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), rate, "Must be between 0 and 1.");
        }

        return random.NextDouble() < rate;
    }
}
