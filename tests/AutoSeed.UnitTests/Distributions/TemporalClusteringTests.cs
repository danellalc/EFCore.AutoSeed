using EFCore.AutoSeed.Distributions;
using EFCore.AutoSeed.Pipeline;

namespace EFCore.AutoSeed.UnitTests.Distributions;

public sealed class TemporalClusteringTests
{
    private static readonly DateTime Start = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Between_AlwaysStaysWithinTheWindow()
    {
        for (int seed = 0; seed < 300; seed++)
        {
            DateTime value = TemporalClustering.Between(SeededRandom.FromRootSeed(seed), Start, End);
            Assert.True(value >= Start && value <= End, $"seed {seed}: {value} outside [{Start}, {End}].");
        }
    }

    [Fact]
    public void Between_IsMostlyOnWeekdays()
    {
        int weekdays = 0;
        const int TotalSeeds = 300;

        for (int seed = 0; seed < TotalSeeds; seed++)
        {
            DateTime value = TemporalClustering.Between(SeededRandom.FromRootSeed(seed), Start, End);
            if (value.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                weekdays++;
            }
        }

        Assert.True(weekdays > TotalSeeds * 0.75, $"expected most of {TotalSeeds} draws to land on a weekday, got {weekdays}.");
        Assert.True(weekdays < TotalSeeds, "expected at least one draw to land on a weekend.");
    }

    [Fact]
    public void Between_IsMostlyDuringBusinessHours()
    {
        int duringBusinessHours = 0;
        const int TotalSeeds = 300;

        for (int seed = 0; seed < TotalSeeds; seed++)
        {
            DateTime value = TemporalClustering.Between(SeededRandom.FromRootSeed(seed), Start, End);
            if (value.TimeOfDay >= TimeSpan.FromHours(9) && value.TimeOfDay <= TimeSpan.FromHours(18))
            {
                duringBusinessHours++;
            }
        }

        Assert.True(duringBusinessHours > TotalSeeds * 0.7, $"expected most of {TotalSeeds} draws within business hours, got {duringBusinessHours}.");
        Assert.True(duringBusinessHours < TotalSeeds, "expected at least one draw outside business hours.");
    }

    [Fact]
    public void Between_WithTheSameSeed_IsDeterministic()
    {
        DateTime first = TemporalClustering.Between(SeededRandom.FromRootSeed(42), Start, End);
        DateTime second = TemporalClustering.Between(SeededRandom.FromRootSeed(42), Start, End);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Between_WhenEndIsNotAfterStart_ReturnsStart()
    {
        DateTime value = TemporalClustering.Between(SeededRandom.FromRootSeed(1), End, Start);
        Assert.Equal(End, value);
    }

    [Fact]
    public void Between_WithNullRandom_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => TemporalClustering.Between(null!, Start, End));
    }
}
