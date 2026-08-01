using EFCore.AutoSeed.Pipeline;

namespace EFCore.AutoSeed.UnitTests.Pipeline;

public sealed class SeededRandomTests
{
    [Fact]
    public void FromRootSeed_WithSamePathDerivedTwice_ProducesTheSameValues()
    {
        SeededRandom first = SeededRandom.FromRootSeed(42).Derive("Customer").Derive(7).Derive("Email");
        SeededRandom second = SeededRandom.FromRootSeed(42).Derive("Customer").Derive(7).Derive("Email");

        Assert.Equal(first.NextDouble(), second.NextDouble());
        Assert.Equal(first.Next(0, 1_000_000), second.Next(0, 1_000_000));
    }

    [Fact]
    public void Derive_ForARowGeneratedAlone_MatchesTheSameRowGeneratedInsideABatch()
    {
        SeededRandom entityScope = SeededRandom.FromRootSeed(42).Derive("Order");

        SeededRandom rowAlone = entityScope.Derive(500);

        for (int index = 0; index < 500; index++)
        {
            entityScope.Derive(index);
        }

        SeededRandom rowAfterBatch = entityScope.Derive(500);

        Assert.Equal(rowAlone.NextDouble(), rowAfterBatch.NextDouble());
    }

    [Fact]
    public void Derive_WithDifferentSegments_ProducesDifferentValues()
    {
        SeededRandom root = SeededRandom.FromRootSeed(42);

        double first = root.Derive("Customer").NextDouble();
        double second = root.Derive("Order").NextDouble();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Derive_WithNullSegment_ThrowsArgumentNullException()
    {
        SeededRandom root = SeededRandom.FromRootSeed(42);

        Assert.Throws<ArgumentNullException>(() => root.Derive((string)null!));
    }

    [Fact]
    public void Derive_ChainedStringSegments_DoNotCollideWithTheirConcatenation()
    {
        SeededRandom chained = SeededRandom.FromRootSeed(42).Derive("Cust").Derive("omer");
        SeededRandom concatenated = SeededRandom.FromRootSeed(42).Derive("Customer");

        Assert.NotEqual(chained.NextDouble(), concatenated.NextDouble());
    }

    [Fact]
    public void FromRootSeed_ProducesTheSamePinnedValueForAFixedSeedAndPath()
    {
        SeededRandom random = SeededRandom.FromRootSeed(42).Derive("Customer").Derive(7).Derive("Email");

        Assert.Equal(0.92564584171662379, random.NextDouble(), precision: 12);
    }

    [Fact]
    public void NextBoolean_ReturnsBothValuesAcrossManyDraws()
    {
        SeededRandom random = SeededRandom.FromRootSeed(42);

        bool[] draws = Enumerable.Range(0, 50)
            .Select(index => random.Derive(index).NextBoolean())
            .ToArray();

        Assert.Contains(true, draws);
        Assert.Contains(false, draws);
    }
}
