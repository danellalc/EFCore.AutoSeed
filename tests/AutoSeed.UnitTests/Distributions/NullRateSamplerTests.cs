using EFCore.AutoSeed.Distributions;
using EFCore.AutoSeed.Pipeline;

namespace EFCore.AutoSeed.UnitTests.Distributions;

public sealed class NullRateSamplerTests
{
    [Fact]
    public void ShouldLeaveNull_WithTheDefaultRate_IsMostlyFalse()
    {
        int leftNull = 0;
        const int TotalSeeds = 500;

        for (int seed = 0; seed < TotalSeeds; seed++)
        {
            if (NullRateSampler.ShouldLeaveNull(SeededRandom.FromRootSeed(seed)))
            {
                leftNull++;
            }
        }

        double rate = (double)leftNull / TotalSeeds;
        Assert.True(rate is > 0.05 and < 0.15, $"expected a rate near 0.1, got {rate}.");
    }

    [Fact]
    public void ShouldLeaveNull_WithRateZero_NeverReturnsTrue()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            Assert.False(NullRateSampler.ShouldLeaveNull(SeededRandom.FromRootSeed(seed), rate: 0));
        }
    }

    [Fact]
    public void ShouldLeaveNull_WithRateOne_AlwaysReturnsTrue()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            Assert.True(NullRateSampler.ShouldLeaveNull(SeededRandom.FromRootSeed(seed), rate: 1));
        }
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void ShouldLeaveNull_WithAnOutOfRangeRate_ThrowsArgumentOutOfRangeException(double rate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NullRateSampler.ShouldLeaveNull(SeededRandom.FromRootSeed(1), rate));
    }

    [Fact]
    public void ShouldLeaveNull_WithNullRandom_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => NullRateSampler.ShouldLeaveNull(null!));
    }

    [Fact]
    public void ShouldLeaveNull_WithTheSameSeed_IsDeterministic()
    {
        bool first = NullRateSampler.ShouldLeaveNull(SeededRandom.FromRootSeed(7));
        bool second = NullRateSampler.ShouldLeaveNull(SeededRandom.FromRootSeed(7));

        Assert.Equal(first, second);
    }
}
