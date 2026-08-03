namespace EFCore.AutoSeed.Distributions;

/// <summary>
/// Tunes how strongly <see cref="TemporalClustering.Between(EFCore.AutoSeed.Pipeline.SeededRandom, System.DateTime, System.DateTime, TemporalClusteringOptions)"/>
/// biases a draw toward weekdays and business hours.
/// </summary>
/// <param name="WeekdayProbability">The probability a draw that landed on a weekend gets shifted to the nearest weekday, in <c>[0, 1]</c>.</param>
/// <param name="BusinessHourProbability">The probability a draw's time of day is confined to business hours instead of the full day, in <c>[0, 1]</c>.</param>
/// <param name="BusinessHoursStart">The start of the business-hours window, or <see langword="null"/> for the default of 09:00.</param>
/// <param name="BusinessHoursEnd">The end of the business-hours window, or <see langword="null"/> for the default of 18:00.</param>
public sealed record TemporalClusteringOptions(
    double WeekdayProbability = 0.9,
    double BusinessHourProbability = 0.85,
    TimeSpan? BusinessHoursStart = null,
    TimeSpan? BusinessHoursEnd = null)
{
    /// <summary>
    /// The default clustering shape: 90% of draws land on a weekday, 85% fall within 09:00-18:00.
    /// </summary>
    public static readonly TemporalClusteringOptions Default = new();

    internal TimeSpan EffectiveBusinessHoursStart => BusinessHoursStart ?? TimeSpan.FromHours(9);

    internal TimeSpan EffectiveBusinessHoursEnd => BusinessHoursEnd ?? TimeSpan.FromHours(18);
}
