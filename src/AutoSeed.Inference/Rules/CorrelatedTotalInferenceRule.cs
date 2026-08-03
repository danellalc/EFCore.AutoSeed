using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Inference.Rules;

/// <summary>
/// Infers a <c>Total</c>-style decimal (<c>Total</c>, <c>Subtotal</c>, <c>LineTotal</c>) as the
/// product of a sibling price and a sibling quantity already generated for the same row, when both
/// are present. Falls back to the same independent draw as <see cref="DecimalAmountInferenceRule"/>
/// when no such siblings exist, so every row still gets a plausible value either way.
/// </summary>
public sealed class CorrelatedTotalInferenceRule : IPropertyInferenceRule
{
    private static readonly string[] PriceSuffixes = ["Price"];
    private static readonly string[] QuantitySuffixes = ["Quantity", "Qty"];

    /// <inheritdoc />
    public int Priority => 1;

    /// <inheritdoc />
    public bool CanInfer(IProperty property) =>
        PropertyNameMatch.IsClrType<decimal>(property) && PropertyNameMatch.EndsWithAny(property, "Total");

    /// <inheritdoc />
    public object? Infer(IProperty property, SeededRandom random, IReadOnlyDictionary<string, object> generatedValues)
    {
        if (FindSibling<decimal>(generatedValues, PriceSuffixes) is { } price
            && FindSibling<int>(generatedValues, QuantitySuffixes) is { } quantity)
        {
            return DecimalAmountHelper.ClampToPrecision(price * quantity, property);
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
