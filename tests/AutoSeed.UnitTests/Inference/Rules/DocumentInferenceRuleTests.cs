using EFCore.AutoSeed.Inference.Rules;
using EFCore.AutoSeed.Pipeline;
using EFCore.AutoSeed.UnitTests.Inference.Fixtures;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.UnitTests.Inference.Rules;

public sealed class DocumentInferenceRuleTests
{
    [Theory]
    [InlineData("Cpf")]
    [InlineData("Cnpj")]
    public void CanInfer_MatchesDocumentProperties(string propertyName)
    {
        IProperty property = InferenceFixtureModel.GetProperty(propertyName);

        Assert.True(new DocumentInferenceRule().CanInfer(property));
    }

    [Fact]
    public void Infer_ForCpf_ProducesElevenDigitsWithAValidCheckDigit()
    {
        IProperty property = InferenceFixtureModel.GetProperty("Cpf");
        DocumentInferenceRule rule = new();

        for (int seed = 0; seed < 200; seed++)
        {
            object value = rule.Infer(property, SeededRandom.FromRootSeed(seed), new Dictionary<string, object>());
            string cpf = Assert.IsType<string>(value);

            Assert.Equal(11, cpf.Length);
            Assert.True(IsValidCpf(cpf), $"'{cpf}' (seed {seed}) is not a valid CPF.");
        }
    }

    [Fact]
    public void Infer_ForCnpj_ProducesFourteenDigitsWithAValidCheckDigit()
    {
        IProperty property = InferenceFixtureModel.GetProperty("Cnpj");
        DocumentInferenceRule rule = new();

        for (int seed = 0; seed < 200; seed++)
        {
            object value = rule.Infer(property, SeededRandom.FromRootSeed(seed), new Dictionary<string, object>());
            string cnpj = Assert.IsType<string>(value);

            Assert.Equal(14, cnpj.Length);
            Assert.True(IsValidCnpj(cnpj), $"'{cnpj}' (seed {seed}) is not a valid CNPJ.");
        }
    }

    [Fact]
    public void IsValidCpf_AcceptsAKnownValidCpf()
    {
        Assert.True(IsValidCpf("11144477735"));
    }

    [Fact]
    public void IsValidCpf_RejectsAWrongCheckDigit()
    {
        Assert.False(IsValidCpf("11144477736"));
    }

    [Fact]
    public void IsValidCnpj_AcceptsAKnownValidCnpj()
    {
        Assert.True(IsValidCnpj("11222333000181"));
    }

    [Fact]
    public void IsValidCnpj_RejectsAWrongCheckDigit()
    {
        Assert.False(IsValidCnpj("11222333000182"));
    }

    [Fact]
    public void Infer_WithSameSeed_IsDeterministic()
    {
        IProperty property = InferenceFixtureModel.GetProperty("Cpf");
        DocumentInferenceRule rule = new();

        object first = rule.Infer(property, SeededRandom.FromRootSeed(7), new Dictionary<string, object>());
        object second = rule.Infer(property, SeededRandom.FromRootSeed(7), new Dictionary<string, object>());

        Assert.Equal(first, second);
    }

    private static bool IsValidCpf(string cpf)
    {
        if (cpf.Length != 11 || !cpf.All(char.IsDigit))
        {
            return false;
        }

        int[] digits = [.. cpf.Select(character => character - '0')];

        int firstCheck = CheckDigit(digits[..9], 10);
        if (firstCheck != digits[9])
        {
            return false;
        }

        int secondCheck = CheckDigit(digits[..10], 11);
        return secondCheck == digits[10];
    }

    private static bool IsValidCnpj(string cnpj)
    {
        if (cnpj.Length != 14 || !cnpj.All(char.IsDigit))
        {
            return false;
        }

        int[] digits = [.. cnpj.Select(character => character - '0')];
        int[] firstWeights = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        int[] secondWeights = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

        int firstCheck = CheckDigit(digits[..12], firstWeights);
        if (firstCheck != digits[12])
        {
            return false;
        }

        int secondCheck = CheckDigit(digits[..13], secondWeights);
        return secondCheck == digits[13];
    }

    private static int CheckDigit(int[] digits, int firstWeight)
    {
        int sum = 0;
        int weight = firstWeight;
        foreach (int digit in digits)
        {
            sum += digit * weight;
            weight--;
        }

        int remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }

    private static int CheckDigit(int[] digits, int[] weights)
    {
        int sum = 0;
        for (int index = 0; index < digits.Length; index++)
        {
            sum += digits[index] * weights[index];
        }

        int remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}
