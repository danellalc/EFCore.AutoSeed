using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Inference.Rules;

/// <summary>
/// Infers a discount rate, as a fraction of a pre-discount amount between 0 and 0.3 (up to 30% off),
/// for properties named <c>Discount</c> or ending in <c>DiscountPercent</c> or <c>DiscountRate</c>.
/// A property this rule claims is always a fraction, never a flat amount subtracted directly from a
/// total: <see cref="AmountDueInferenceRule"/> depends on that semantic to compute a correlated net
/// amount from it.
/// </summary>
public sealed class DiscountInferenceRule : IPropertyInferenceRule
{
    private const decimal MaxDiscountRate = 0.3m;

    /// <inheritdoc />
    public int Priority => 0;

    /// <inheritdoc />
    public bool CanInfer(IProperty property) =>
        PropertyNameMatch.IsClrType<decimal>(property)
        && PropertyNameMatch.EndsWithAny(property, "Discount", "DiscountPercent", "DiscountRate");

    /// <inheritdoc />
    public object? Infer(IProperty property, SeededRandom random, IReadOnlyDictionary<string, object> generatedValues) =>
        DecimalAmountHelper.ClampToPrecision((decimal)random.NextDouble() * MaxDiscountRate, property);
}
