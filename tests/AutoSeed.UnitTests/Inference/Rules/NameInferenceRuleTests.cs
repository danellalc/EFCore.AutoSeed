using EFCore.AutoSeed.Inference.Rules;
using EFCore.AutoSeed.Pipeline;
using EFCore.AutoSeed.UnitTests.Inference.Fixtures;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.UnitTests.Inference.Rules;

public sealed class NameInferenceRuleTests
{
    [Theory]
    [InlineData("FirstName")]
    [InlineData("LastName")]
    public void CanInfer_MatchesNameProperties(string propertyName)
    {
        IProperty property = InferenceFixtureModel.GetProperty(propertyName);

        Assert.True(new NameInferenceRule().CanInfer(property));
    }

    [Fact]
    public void CanInfer_DoesNotMatchUnrelatedStringProperty()
    {
        IProperty property = InferenceFixtureModel.GetProperty("Url");

        Assert.False(new NameInferenceRule().CanInfer(property));
    }

    [Fact]
    public void Infer_ForFirstName_ProducesANonEmptyValue()
    {
        IProperty property = InferenceFixtureModel.GetProperty("FirstName");
        NameInferenceRule rule = new();

        object value = rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>());

        Assert.IsType<string>(value);
        Assert.NotEmpty((string)value);
    }

    [Fact]
    public void Infer_WithSameSeed_IsDeterministic()
    {
        IProperty property = InferenceFixtureModel.GetProperty("FirstName");
        NameInferenceRule rule = new();

        object first = rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>());
        object second = rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>());

        Assert.Equal(first, second);
    }

}
