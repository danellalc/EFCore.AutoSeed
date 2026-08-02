using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Inference.Rules;

/// <summary>
/// Infers a decimal amount for properties named <c>Price</c>, <c>Amount</c>, <c>Total</c> or
/// <c>Balance</c>, respecting the property's precision and scale when the model declares them.
/// </summary>
public sealed class DecimalAmountInferenceRule : IPropertyInferenceRule
{
    private const decimal DefaultMax = 10_000m;
    private const int MaxSafePowerOfTen = 27;

    /// <inheritdoc />
    public int Priority => 0;

    /// <inheritdoc />
    public bool CanInfer(IProperty property) =>
        PropertyNameMatch.IsClrType<decimal>(property) && PropertyNameMatch.EndsWithAny(property, "Price", "Amount", "Total", "Balance");

    /// <inheritdoc />
    public object Infer(IProperty property, SeededRandom random, IReadOnlyDictionary<string, object> generatedValues)
    {
        int scale = property.GetScale() ?? 2;
        decimal max = MaxForPrecision(property.GetPrecision(), scale);

        Bogus.DataSets.Finance finance = new() { Random = new Bogus.Randomizer(random.Seed) };
        return finance.Amount(0m, max, scale);
    }

    private static decimal MaxForPrecision(int? precision, int scale)
    {
        if (precision is not > 0 || precision.Value >= MaxSafePowerOfTen || scale is < 0 or >= MaxSafePowerOfTen)
        {
            return DefaultMax;
        }

        decimal precisionBound = (Pow10(precision.Value) - 1) / Pow10(scale);
        return Math.Min(DefaultMax, precisionBound);
    }

    private static decimal Pow10(int exponent)
    {
        decimal value = 1m;
        for (int index = 0; index < exponent; index++)
        {
            value *= 10m;
        }

        return value;
    }
}
