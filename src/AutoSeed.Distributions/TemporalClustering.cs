using EFCore.AutoSeed.Pipeline;

namespace EFCore.AutoSeed.Distributions;

/// <summary>
/// Draws a <see cref="DateTime"/> within a window, biased toward weekdays and business hours
/// instead of spreading uniformly across the whole window. Reflects that most real-world
/// timestamps (orders, signups, edits) cluster around when people are actually working.
/// </summary>
public static class TemporalClustering
{
    /// <summary>
    /// Draws a <see cref="DateTime"/> in <c>[start, end]</c>, biased toward weekdays and business
    /// hours per <see cref="TemporalClusteringOptions.Default"/>.
    /// </summary>
    /// <param name="random">The random source this draw derives from.</param>
    /// <param name="start">The inclusive lower bound.</param>
    /// <param name="end">The inclusive upper bound.</param>
    /// <returns><paramref name="start"/> when <paramref name="end"/> is not after it; otherwise a biased draw within the window.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="random"/> is <see langword="null"/>.</exception>
    public static DateTime Between(SeededRandom random, DateTime start, DateTime end) =>
        Between(random, start, end, TemporalClusteringOptions.Default);

    /// <summary>
    /// Draws a <see cref="DateTime"/> in <c>[start, end]</c>, biased toward weekdays and business
    /// hours per <paramref name="options"/>.
    /// </summary>
    /// <param name="random">The random source this draw derives from.</param>
    /// <param name="start">The inclusive lower bound.</param>
    /// <param name="end">The inclusive upper bound.</param>
    /// <param name="options">The clustering shape to apply.</param>
    /// <returns><paramref name="start"/> when <paramref name="end"/> is not after it; otherwise a biased draw within the window.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="random"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    public static DateTime Between(SeededRandom random, DateTime start, DateTime end, TemporalClusteringOptions options)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(options);
        if (end <= start)
        {
            return start;
        }

        DateTime day = ClusterDay(random, start.Date, end.Date, options);
        TimeSpan timeOfDay = ClusterTimeOfDay(random, options);

        return Clamp(day + timeOfDay, start, end);
    }

    private static DateTime ClusterDay(SeededRandom random, DateTime startDay, DateTime endDay, TemporalClusteringOptions options)
    {
        long totalDays = (endDay - startDay).Days;
        long offset = (long)(totalDays * random.NextDouble());
        DateTime day = startDay.AddDays(offset);

        return IsWeekend(day) && random.NextDouble() < options.WeekdayProbability ? ShiftToNearestWeekday(day) : day;
    }

    private static TimeSpan ClusterTimeOfDay(SeededRandom random, TemporalClusteringOptions options)
    {
        bool duringBusinessHours = random.NextDouble() < options.BusinessHourProbability;
        TimeSpan windowStart = duringBusinessHours ? options.EffectiveBusinessHoursStart : TimeSpan.Zero;
        TimeSpan windowEnd = duringBusinessHours ? options.EffectiveBusinessHoursEnd : TimeSpan.FromHours(24);

        double fraction = random.NextDouble();
        return windowStart + TimeSpan.FromTicks((long)((windowEnd - windowStart).Ticks * fraction));
    }

    private static bool IsWeekend(DateTime day) => day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private static DateTime ShiftToNearestWeekday(DateTime day) => day.DayOfWeek switch
    {
        DayOfWeek.Saturday => day.AddDays(-1),
        DayOfWeek.Sunday => day.AddDays(1),
        _ => day,
    };

    private static DateTime Clamp(DateTime value, DateTime min, DateTime max) =>
        value < min ? min : value > max ? max : value;
}
