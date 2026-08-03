using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Inference.Rules;

/// <summary>
/// Infers an <c>AmountDue</c>-style decimal (<c>AmountDue</c>, <c>AmountPayable</c>) as a sibling
/// price and quantity's product, reduced by a sibling discount rate already generated for the same
/// row, when all three are present. The discount always applies as a fraction of the gross amount
/// (<c>price * quantity * (1 - rate)</c>), matching the semantic <see cref="DiscountInferenceRule"/>
/// produces, never as a flat amount subtracted from the gross. Falls back to the same independent
/// draw as <see cref="DecimalAmountInferenceRule"/> when any of the three siblings is missing, so
/// every row still gets a plausible value either way.
/// </summary>
public sealed class AmountDueInferenceRule : IPropertyInferenceRule
{
    private static readonly string[] PriceSuffixes = ["Price"];
    private static readonly string[] QuantitySuffixes = ["Quantity", "Qty"];
    private static readonly string[] DiscountSuffixes = ["Discount", "DiscountPercent", "DiscountRate"];

    /// <inheritdoc />
    public int Priority => 1;

    /// <inheritdoc />
    public bool CanInfer(IProperty property) =>
        PropertyNameMatch.IsClrType<decimal>(property) && PropertyNameMatch.EndsWithAny(property, "AmountDue", "AmountPayable");

    /// <inheritdoc />
    public object? Infer(IProperty property, SeededRandom random, IReadOnlyDictionary<string, object> generatedValues)
    {
        if (FindSibling<decimal>(generatedValues, PriceSuffixes) is { } price
            && FindSibling<int>(generatedValues, QuantitySuffixes) is { } quantity
            && FindSibling<decimal>(generatedValues, DiscountSuffixes) is { } discountRate)
        {
            return DecimalAmountHelper.ClampToPrecision(price * quantity * (1m - discountRate), property);
        }

        return DecimalAmountHelper.DrawIndependentAmount(property, random);
    }

    private static TValue? FindSibling<TValue>(IReadOnlyDictionary<string, object> generatedValues, ReadOnlySpan<string> suffixes)
        where TValue : struct
    {
        foreach (KeyValuePair<string, object> entry in generatedValues)
        {
            if (entry.Value is not TValue value)
            {
                continue;
            }

            foreach (string suffix in suffixes)
            {
                if (entry.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return value;
                }
            }
        }

        return null;
    }
}
