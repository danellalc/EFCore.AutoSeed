using EFCore.AutoSeed.Inference.Rules;
using EFCore.AutoSeed.Pipeline;
using EFCore.AutoSeed.UnitTests.Inference.Fixtures;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.UnitTests.Inference.Rules;

public sealed class EmailInferenceRuleTests
{
    [Fact]
    public void CanInfer_MatchesEmailProperty()
    {
        IProperty property = InferenceFixtureModel.GetProperty("Email");

        Assert.True(new EmailInferenceRule().CanInfer(property));
    }

    [Fact]
    public void Infer_WithFirstAndLastNameSiblings_ProducesADifferentValueThanWithoutThem()
    {
        IProperty property = InferenceFixtureModel.GetProperty("Email");
        EmailInferenceRule rule = new();

        Dictionary<string, object> withNames = new() { ["FirstName"] = "Ana", ["LastName"] = "Silva" };
        Dictionary<string, object> withoutNames = [];

        object withNamesResult = rule.Infer(property, SeededRandom.FromRootSeed(42).Derive("x"), withNames)!;
        object withoutNamesResult = rule.Infer(property, SeededRandom.FromRootSeed(42).Derive("x"), withoutNames)!;

        Assert.NotEqual(withNamesResult, withoutNamesResult);
    }

    [Fact]
    public void Infer_WithSameSeedAndSiblings_IsDeterministic()
    {
        IProperty property = InferenceFixtureModel.GetProperty("Email");
        EmailInferenceRule rule = new();
        Dictionary<string, object> siblings = new() { ["FirstName"] = "Ana", ["LastName"] = "Silva" };

        object first = rule.Infer(property, SeededRandom.FromRootSeed(42), siblings)!;
        object second = rule.Infer(property, SeededRandom.FromRootSeed(42), siblings)!;

        Assert.Equal(first, second);
    }

    [Fact]
    public void Infer_WithDifferentLocales_ProducesDifferentValues()
    {
        IProperty property = InferenceFixtureModel.GetProperty("Email");
        EmailInferenceRule englishRule = new("en");
        EmailInferenceRule brazilianRule = new("pt_BR");
        Dictionary<string, object> siblings = new() { ["FirstName"] = "Ana", ["LastName"] = "Silva" };

        object englishResult = englishRule.Infer(property, SeededRandom.FromRootSeed(42), siblings)!;
        object brazilianResult = brazilianRule.Infer(property, SeededRandom.FromRootSeed(42), siblings)!;

        Assert.NotEqual(englishResult, brazilianResult);
    }

    [Fact]
    public void Infer_ProducesASyntacticallyPlausibleEmail()
    {
        IProperty property = InferenceFixtureModel.GetProperty("Email");
        EmailInferenceRule rule = new();

        object value = rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>())!;

        string email = Assert.IsType<string>(value);
        Assert.Contains('@', email);
        Assert.Contains('.', email.Split('@')[1]);
    }
}
