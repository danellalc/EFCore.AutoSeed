using EFCore.AutoSeed.Distributions;
using EFCore.AutoSeed.Inference;

namespace EFCore.AutoSeed.UnitTests.Inference;

public sealed class AutoSeedOptionsTests
{
    [Fact]
    public void Default_MatchesTheParameterlessConstructor()
    {
        Assert.Equal(new AutoSeedOptions(), AutoSeedOptions.Default);
    }

    [Fact]
    public void Default_ReproducesThePreviouslyHardcodedBehavior()
    {
        AutoSeedOptions options = AutoSeedOptions.Default;

        Assert.Equal(0.9, options.QueryFilterPassRate);
        Assert.Equal(NullRateSampler.DefaultRate, options.NullRate);
        Assert.Null(options.TemporalClustering);
        Assert.Equal("en", options.Locale);
    }
}
