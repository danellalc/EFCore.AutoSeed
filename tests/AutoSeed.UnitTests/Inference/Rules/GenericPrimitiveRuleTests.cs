using EFCore.AutoSeed.Inference;
using EFCore.AutoSeed.Inference.Rules;
using EFCore.AutoSeed.Pipeline;
using EFCore.AutoSeed.UnitTests.Inference.Fixtures;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.UnitTests.Inference.Rules;

public sealed class GenericPrimitiveRuleTests
{
    [Fact]
    public void GenericNumber_CanInferAnIntProperty()
    {
        IProperty property = InferenceFixtureModel.GetProperty("VisitCount");
        GenericNumberInferenceRule rule = new();

        Assert.True(rule.CanInfer(property));
        object first = rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>());
        object second = rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>());

        int value = Assert.IsType<int>(first);
        Assert.True(value is >= 0 and < 1_000);
        Assert.Equal(first, second);
    }

    [Fact]
    public void GenericNumber_RespectsDecimalScaleWhenNoNameMatches()
    {
        IProperty property = InferenceFixtureModel.GetProperty("EqualScalePrice");
        GenericNumberInferenceRule rule = new();

        object value = rule.Infer(property, SeededRandom.FromRootSeed(7), new Dictionary<string, object>());

        decimal decimalValue = Assert.IsType<decimal>(value);
        Assert.True(decimalValue.Scale <= 2);
    }

    [Fact]
    public void GenericNumber_DoesNotClaimTheDatabaseGeneratedPrimaryKey()
    {
        IProperty property = InferenceFixtureModel.GetProperty("Id");
        Assert.False(new GenericNumberInferenceRule().CanInfer(property));
    }

    [Fact]
    public void GenericBoolean_CanInferAndIsDeterministic()
    {
        IProperty property = InferenceFixtureModel.GetProperty("IsActive");
        GenericBooleanInferenceRule rule = new();

        Assert.True(rule.CanInfer(property));
        object first = rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>());
        object second = rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>());

        Assert.IsType<bool>(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void GenericBoolean_ProducesBothValuesAcrossSeeds()
    {
        GenericBooleanInferenceRule rule = new();
        IProperty property = InferenceFixtureModel.GetProperty("IsActive");

        HashSet<bool> seen = [];
        for (int seed = 0; seed < 20; seed++)
        {
            seen.Add((bool)rule.Infer(property, SeededRandom.FromRootSeed(seed), new Dictionary<string, object>()));
        }

        Assert.Equal(2, seen.Count);
    }

    [Fact]
    public void GenericEnum_CanInferAndOnlyProducesDeclaredValues()
    {
        IProperty property = InferenceFixtureModel.GetProperty("Status");
        GenericEnumInferenceRule rule = new();

        Assert.True(rule.CanInfer(property));

        for (int seed = 0; seed < 30; seed++)
        {
            object value = rule.Infer(property, SeededRandom.FromRootSeed(seed), new Dictionary<string, object>());
            PersonStatus status = Assert.IsType<PersonStatus>(value);
            Assert.True(Enum.IsDefined(status));
        }
    }

    [Fact]
    public void GenericEnum_IsDeterministic()
    {
        IProperty property = InferenceFixtureModel.GetProperty("Status");
        GenericEnumInferenceRule rule = new();

        object first = rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>());
        object second = rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>());

        Assert.Equal(first, second);
    }

    [Fact]
    public void GenericGuid_CanInferAndIsDeterministic()
    {
        IProperty property = InferenceFixtureModel.GetProperty("ExternalId");
        GenericGuidInferenceRule rule = new();

        Assert.True(rule.CanInfer(property));
        object first = rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>());
        object second = rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>());

        Guid guid = Assert.IsType<Guid>(first);
        Assert.NotEqual(Guid.Empty, guid);
        Assert.Equal(first, second);
    }

    [Fact]
    public void GenericGuid_DifferentSeedsProduceDifferentValues()
    {
        IProperty property = InferenceFixtureModel.GetProperty("ExternalId");
        GenericGuidInferenceRule rule = new();

        Guid first = (Guid)rule.Infer(property, SeededRandom.FromRootSeed(1), new Dictionary<string, object>());
        Guid second = (Guid)rule.Infer(property, SeededRandom.FromRootSeed(2), new Dictionary<string, object>());

        Assert.NotEqual(first, second);
    }
}
