using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Inference.Rules;

/// <summary>
/// Infers a decimal amount for properties named <c>Price</c>, <c>Amount</c> or <c>Balance</c>,
/// respecting the property's precision and scale when the model declares them. A <c>Total</c>-style
/// property is handled by <see cref="CorrelatedTotalInferenceRule"/> instead, since it can often be
/// derived from sibling properties rather than drawn independently.
/// </summary>
public sealed class DecimalAmountInferenceRule : IPropertyInferenceRule
{
    /// <inheritdoc />
    public int Priority => 0;

    /// <inheritdoc />
    public bool CanInfer(IProperty property) =>
        PropertyNameMatch.IsClrType<decimal>(property) && PropertyNameMatch.EndsWithAny(property, "Price", "Amount", "Balance");

    /// <inheritdoc />
    public object? Infer(IProperty property, SeededRandom random, IReadOnlyDictionary<string, object> generatedValues) =>
        DecimalAmountHelper.DrawIndependentAmount(property, random);
}
