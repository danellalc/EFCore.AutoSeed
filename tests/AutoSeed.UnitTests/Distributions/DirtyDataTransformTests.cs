using EFCore.AutoSeed.Distributions;
using EFCore.AutoSeed.Pipeline;

namespace EFCore.AutoSeed.UnitTests.Distributions;

public sealed class DirtyDataTransformTests
{
    private const string NameWithDiacritics = "José da Conceição";
    private const string NameWithSpaces = "Rua das Flores";
    private const string SingleWord = "Palmeiras";
    private const int TotalSeeds = 500;

    [Fact]
    public void ApplyCasingNoise_WithDefaultRate_IsMostlyUnchanged()
    {
        int changed = 0;

        for (int seed = 0; seed < TotalSeeds; seed++)
        {
            string result = DirtyDataTransform.ApplyCasingNoise(NameWithSpaces, SeededRandom.FromRootSeed(seed));
            if (result != NameWithSpaces)
            {
                changed++;
            }
        }

        double rate = (double)changed / TotalSeeds;
        Assert.True(rate is > 0.05 and < 0.15, $"expected a rate near 0.1, got {rate}.");
    }

    [Fact]
    public void ApplyCasingNoise_WithRateOne_ProducesAllThreeVariants()
    {
        bool sawUpper = false;
        bool sawLower = false;
        bool sawMixedOrUnchanged = false;

        for (int seed = 0; seed < TotalSeeds; seed++)
        {
            string result = DirtyDataTransform.ApplyCasingNoise(NameWithSpaces, SeededRandom.FromRootSeed(seed), rate: 1);
            sawUpper |= result == NameWithSpaces.ToUpperInvariant();
            sawLower |= result == NameWithSpaces.ToLowerInvariant();
            sawMixedOrUnchanged |= result != NameWithSpaces.ToUpperInvariant() && result != NameWithSpaces.ToLowerInvariant();
        }

        Assert.True(sawUpper, "expected at least one seed to produce the all-caps variant.");
        Assert.True(sawLower, "expected at least one seed to produce the all-lowercase variant.");
        Assert.True(sawMixedOrUnchanged, "expected at least one seed to produce the mixed-casing variant.");
    }

    [Fact]
    public void ApplyCasingNoise_NeverChangesLetterContentIgnoringCase()
    {
        for (int seed = 0; seed < TotalSeeds; seed++)
        {
            string result = DirtyDataTransform.ApplyCasingNoise(NameWithSpaces, SeededRandom.FromRootSeed(seed), rate: 1);
            Assert.Equal(NameWithSpaces.ToUpperInvariant(), result.ToUpperInvariant());
        }
    }

    [Fact]
    public void ApplyCasingNoise_WithRateZero_NeverChangesValue()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            string result = DirtyDataTransform.ApplyCasingNoise(NameWithSpaces, SeededRandom.FromRootSeed(seed), rate: 0);
            Assert.Equal(NameWithSpaces, result);
        }
    }

    [Fact]
    public void ApplyCasingNoise_WithTheSameSeed_IsDeterministic()
    {
        string first = DirtyDataTransform.ApplyCasingNoise(NameWithSpaces, SeededRandom.FromRootSeed(7), rate: 1);
        string second = DirtyDataTransform.ApplyCasingNoise(NameWithSpaces, SeededRandom.FromRootSeed(7), rate: 1);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ApplyCasingNoise_WithNullValue_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => DirtyDataTransform.ApplyCasingNoise(null!, SeededRandom.FromRootSeed(1)));
    }

    [Fact]
    public void ApplyCasingNoise_WithNullRandom_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => DirtyDataTransform.ApplyCasingNoise(NameWithSpaces, null!));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void ApplyCasingNoise_WithAnOutOfRangeRate_ThrowsArgumentOutOfRangeException(double rate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DirtyDataTransform.ApplyCasingNoise(NameWithSpaces, SeededRandom.FromRootSeed(1), rate));
    }

    [Fact]
    public void ApplyWhitespaceNoise_WithDefaultRate_IsMostlyUnchanged()
    {
        int changed = 0;

        for (int seed = 0; seed < TotalSeeds; seed++)
        {
            string result = DirtyDataTransform.ApplyWhitespaceNoise(NameWithSpaces, SeededRandom.FromRootSeed(seed));
            if (result != NameWithSpaces)
            {
                changed++;
            }
        }

        double rate = (double)changed / TotalSeeds;
        Assert.True(rate is > 0.05 and < 0.15, $"expected a rate near 0.1, got {rate}.");
    }

    [Fact]
    public void ApplyWhitespaceNoise_WithRateOne_NeverChangesNonSpaceContent()
    {
        for (int seed = 0; seed < TotalSeeds; seed++)
        {
            string result = DirtyDataTransform.ApplyWhitespaceNoise(NameWithSpaces, SeededRandom.FromRootSeed(seed), rate: 1);
            Assert.Equal(NameWithSpaces.Replace(" ", string.Empty), result.Replace(" ", string.Empty));
        }
    }

    [Fact]
    public void ApplyWhitespaceNoise_WithRateOne_SometimesAddsLeadingOrTrailingWhitespace()
    {
        int paddedAtEdge = 0;

        for (int seed = 0; seed < TotalSeeds; seed++)
        {
            string result = DirtyDataTransform.ApplyWhitespaceNoise(NameWithSpaces, SeededRandom.FromRootSeed(seed), rate: 1);
            if (result.StartsWith(' ') || result.EndsWith(' '))
            {
                paddedAtEdge++;
            }
        }

        Assert.True(paddedAtEdge > 0, "expected at least one seed to pad a leading or trailing space.");
        Assert.True(paddedAtEdge < TotalSeeds, "expected at least one seed to not pad at the edges.");
    }

    [Fact]
    public void ApplyWhitespaceNoise_WithRateOne_SometimesChangesInternalSpacing()
    {
        int internalSpacingChanged = 0;

        for (int seed = 0; seed < TotalSeeds; seed++)
        {
            string result = DirtyDataTransform.ApplyWhitespaceNoise(NameWithSpaces, SeededRandom.FromRootSeed(seed), rate: 1);
            if (result.Trim() != NameWithSpaces)
            {
                internalSpacingChanged++;
            }
        }

        Assert.True(internalSpacingChanged > 0, "expected at least one seed to duplicate or collapse internal whitespace.");
    }

    [Fact]
    public void ApplyWhitespaceNoise_WithNoInternalSpaces_FallsBackToEdgePadding()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            string result = DirtyDataTransform.ApplyWhitespaceNoise(SingleWord, SeededRandom.FromRootSeed(seed), rate: 1);
            Assert.Equal(SingleWord, result.Trim());
        }
    }

    [Fact]
    public void ApplyWhitespaceNoise_WithRateZero_NeverChangesValue()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            string result = DirtyDataTransform.ApplyWhitespaceNoise(NameWithSpaces, SeededRandom.FromRootSeed(seed), rate: 0);
            Assert.Equal(NameWithSpaces, result);
        }
    }

    [Fact]
    public void ApplyWhitespaceNoise_WithTheSameSeed_IsDeterministic()
    {
        string first = DirtyDataTransform.ApplyWhitespaceNoise(NameWithSpaces, SeededRandom.FromRootSeed(7), rate: 1);
        string second = DirtyDataTransform.ApplyWhitespaceNoise(NameWithSpaces, SeededRandom.FromRootSeed(7), rate: 1);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ApplyWhitespaceNoise_WithNullValue_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => DirtyDataTransform.ApplyWhitespaceNoise(null!, SeededRandom.FromRootSeed(1)));
    }

    [Fact]
    public void ApplyWhitespaceNoise_WithNullRandom_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => DirtyDataTransform.ApplyWhitespaceNoise(NameWithSpaces, null!));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void ApplyWhitespaceNoise_WithAnOutOfRangeRate_ThrowsArgumentOutOfRangeException(double rate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DirtyDataTransform.ApplyWhitespaceNoise(NameWithSpaces, SeededRandom.FromRootSeed(1), rate));
    }

    [Fact]
    public void StripDiacritics_WithDefaultRate_IsMostlyUnchanged()
    {
        int changed = 0;

        for (int seed = 0; seed < TotalSeeds; seed++)
        {
            string result = DirtyDataTransform.StripDiacritics(NameWithDiacritics, SeededRandom.FromRootSeed(seed));
            if (result != NameWithDiacritics)
            {
                changed++;
            }
        }

        double rate = (double)changed / TotalSeeds;
        Assert.True(rate is > 0.05 and < 0.15, $"expected a rate near 0.1, got {rate}.");
    }

    [Fact]
    public void StripDiacritics_WithRateOne_StripsKnownPortugueseDiacritics()
    {
        string result = DirtyDataTransform.StripDiacritics(NameWithDiacritics, SeededRandom.FromRootSeed(1), rate: 1);
        Assert.Equal("Jose da Conceicao", result);
    }

    [Fact]
    public void StripDiacritics_WithRateOne_NeverLeavesAKnownDiacriticBehind()
    {
        const string diacritics = "áàâãäÁÀÂÃÄéèêëÉÈÊËíìîïÍÌÎÏóòôõöÓÒÔÕÖúùûüÚÙÛÜçÇ";

        for (int seed = 0; seed < TotalSeeds; seed++)
        {
            string result = DirtyDataTransform.StripDiacritics(NameWithDiacritics, SeededRandom.FromRootSeed(seed), rate: 1);
            Assert.False(result.Any(diacritics.Contains), $"seed {seed}: '{result}' still contains a known diacritic.");
        }
    }

    [Fact]
    public void StripDiacritics_AlwaysPreservesLength()
    {
        for (int seed = 0; seed < TotalSeeds; seed++)
        {
            string result = DirtyDataTransform.StripDiacritics(NameWithDiacritics, SeededRandom.FromRootSeed(seed), rate: 1);
            Assert.Equal(NameWithDiacritics.Length, result.Length);
        }
    }

    [Fact]
    public void StripDiacritics_WithNoDiacriticsPresent_AlwaysReturnsTheSameValue()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            string result = DirtyDataTransform.StripDiacritics(NameWithSpaces, SeededRandom.FromRootSeed(seed), rate: 1);
            Assert.Equal(NameWithSpaces, result);
        }
    }

    [Fact]
    public void StripDiacritics_WithRateZero_NeverChangesValue()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            string result = DirtyDataTransform.StripDiacritics(NameWithDiacritics, SeededRandom.FromRootSeed(seed), rate: 0);
            Assert.Equal(NameWithDiacritics, result);
        }
    }

    [Fact]
    public void StripDiacritics_WithTheSameSeed_IsDeterministic()
    {
        string first = DirtyDataTransform.StripDiacritics(NameWithDiacritics, SeededRandom.FromRootSeed(7), rate: 1);
        string second = DirtyDataTransform.StripDiacritics(NameWithDiacritics, SeededRandom.FromRootSeed(7), rate: 1);

        Assert.Equal(first, second);
    }

    [Fact]
    public void StripDiacritics_WithNullValue_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => DirtyDataTransform.StripDiacritics(null!, SeededRandom.FromRootSeed(1)));
    }

    [Fact]
    public void StripDiacritics_WithNullRandom_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => DirtyDataTransform.StripDiacritics(NameWithDiacritics, null!));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void StripDiacritics_WithAnOutOfRangeRate_ThrowsArgumentOutOfRangeException(double rate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DirtyDataTransform.StripDiacritics(NameWithDiacritics, SeededRandom.FromRootSeed(1), rate));
    }

    [Fact]
    public void Apply_WithKindNone_AlwaysReturnsValueUnchanged()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            string result = DirtyDataTransform.Apply(NameWithDiacritics, SeededRandom.FromRootSeed(seed), DirtyDataKind.None);
            Assert.Equal(NameWithDiacritics, result);
        }
    }

    [Fact]
    public void Apply_WithOnlyWhitespaceKind_NeverStripsDiacriticsOrChangesCasing()
    {
        for (int seed = 0; seed < TotalSeeds; seed++)
        {
            string result = DirtyDataTransform.Apply(NameWithDiacritics, SeededRandom.FromRootSeed(seed), DirtyDataKind.Whitespace);
            Assert.Equal(NameWithDiacritics.Replace(" ", string.Empty), result.Replace(" ", string.Empty));
        }
    }

    [Fact]
    public void Apply_WithOnlyDiacriticsKind_NeverChangesWhitespaceOrCasing()
    {
        for (int seed = 0; seed < TotalSeeds; seed++)
        {
            string result = DirtyDataTransform.Apply(NameWithDiacritics, SeededRandom.FromRootSeed(seed), DirtyDataKind.Diacritics);
            Assert.True(result == NameWithDiacritics || result == "Jose da Conceicao");
        }
    }

    [Fact]
    public void Apply_WithDefaultKinds_IsMostlyUnchangedButNotAlways()
    {
        int changed = 0;

        for (int seed = 0; seed < TotalSeeds; seed++)
        {
            string result = DirtyDataTransform.Apply(NameWithDiacritics, SeededRandom.FromRootSeed(seed));
            if (result != NameWithDiacritics)
            {
                changed++;
            }
        }

        double rate = (double)changed / TotalSeeds;
        Assert.True(rate is > 0.1 and < 0.5, $"expected a minority-but-nonzero change rate, got {rate}.");
    }

    [Fact]
    public void Apply_WithTheSameSeed_IsDeterministic()
    {
        string first = DirtyDataTransform.Apply(NameWithDiacritics, SeededRandom.FromRootSeed(7));
        string second = DirtyDataTransform.Apply(NameWithDiacritics, SeededRandom.FromRootSeed(7));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Apply_WithNullValue_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => DirtyDataTransform.Apply(null!, SeededRandom.FromRootSeed(1)));
    }

    [Fact]
    public void Apply_WithNullRandom_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => DirtyDataTransform.Apply(NameWithDiacritics, null!));
    }
}
