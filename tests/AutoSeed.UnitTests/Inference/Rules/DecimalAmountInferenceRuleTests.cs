using EFCore.AutoSeed.Inference.Rules;
using EFCore.AutoSeed.Pipeline;
using EFCore.AutoSeed.UnitTests.Inference.Fixtures;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.UnitTests.Inference.Rules;

public sealed class DecimalAmountInferenceRuleTests
{
    [Fact]
    public void CanInfer_MatchesBalanceProperty()
    {
        IProperty property = InferenceFixtureModel.GetProperty("Balance");

        Assert.True(new DecimalAmountInferenceRule().CanInfer(property));
    }

    [Fact]
    public void Infer_RespectsThePropertysScale()
    {
        IProperty property = InferenceFixtureModel.GetProperty("Balance");
        DecimalAmountInferenceRule rule = new();

        for (int seed = 0; seed < 100; seed++)
        {
            decimal value = (decimal)rule.Infer(property, SeededRandom.FromRootSeed(seed), new Dictionary<string, object>())!;
            int scale = (decimal.GetBits(value)[3] >> 16) & 0xFF;
            Assert.True(scale <= 2, $"seed {seed}: {value} has more than 2 decimal places.");
        }
    }

    [Fact]
    public void Infer_RespectsThePropertysPrecision()
    {
        IProperty property = InferenceFixtureModel.GetProperty("Balance");
        DecimalAmountInferenceRule rule = new();

        for (int seed = 0; seed < 100; seed++)
        {
            decimal value = (decimal)rule.Infer(property, SeededRandom.FromRootSeed(seed), new Dictionary<string, object>())!;
            Assert.True(value < 1_000_000m, $"seed {seed}: {value} exceeds the precision(8,2) bound.");
            Assert.True(value >= 0m);
        }
    }

    [Fact]
    public void Infer_ForATightPrecision_StaysWithinTheDeclaredBound()
    {
        IProperty property = InferenceFixtureModel.GetProperty("TinyPrice");
        DecimalAmountInferenceRule rule = new();

        for (int seed = 0; seed < 100; seed++)
        {
            decimal value = (decimal)rule.Infer(property, SeededRandom.FromRootSeed(seed), new Dictionary<string, object>())!;
            Assert.True(value < 100m, $"seed {seed}: {value} exceeds the precision(4,2) bound of 99.99.");
        }
    }

    [Fact]
    public void Infer_WhenScaleEqualsPrecision_NeverExceedsTheDeclaredMagnitude()
    {
        IProperty property = InferenceFixtureModel.GetProperty("EqualScalePrice");
        DecimalAmountInferenceRule rule = new();

        for (int seed = 0; seed < 100; seed++)
        {
            decimal value = (decimal)rule.Infer(property, SeededRandom.FromRootSeed(seed), new Dictionary<string, object>())!;
            Assert.True(value < 1m, $"seed {seed}: {value} exceeds the precision(2,2) bound of 0.99.");
        }
    }

    [Fact]
    public void Infer_ForAVeryHighPrecisionColumn_DoesNotThrow()
    {
        IProperty property = InferenceFixtureModel.GetProperty("HugeTotal");
        DecimalAmountInferenceRule rule = new();

        for (int seed = 0; seed < 20; seed++)
        {
            object value = rule.Infer(property, SeededRandom.FromRootSeed(seed), new Dictionary<string, object>())!;
            Assert.IsType<decimal>(value);
        }
    }

    [Fact]
    public void Infer_WithSameSeed_IsDeterministic()
    {
        IProperty property = InferenceFixtureModel.GetProperty("Balance");
        DecimalAmountInferenceRule rule = new();

        object first = rule.Infer(property, SeededRandom.FromRootSeed(7), new Dictionary<string, object>())!;
        object second = rule.Infer(property, SeededRandom.FromRootSeed(7), new Dictionary<string, object>())!;

        Assert.Equal(first, second);
    }
}
