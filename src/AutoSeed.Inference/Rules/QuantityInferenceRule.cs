using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Inference.Rules;

/// <summary>
/// Infers a small positive integer for properties named <c>Quantity</c> or ending in <c>Qty</c>
/// (<c>OrderQty</c>, say), instead of the generic numeric fallback's much wider, unrealistic range.
/// </summary>
public sealed class QuantityInferenceRule : IPropertyInferenceRule
{
    private const int MinQuantity = 1;
    private const int MaxQuantity = 20;

    /// <inheritdoc />
    public int Priority => 0;

    /// <inheritdoc />
    public bool CanInfer(IProperty property) =>
        PropertyNameMatch.IsClrType<int>(property)
        && !property.IsForeignKey()
        && property.ValueGenerated == ValueGenerated.Never
        && PropertyNameMatch.EndsWithAny(property, "Quantity", "Qty");

    /// <inheritdoc />
    public object? Infer(IProperty property, SeededRandom random, IReadOnlyDictionary<string, object> generatedValues) =>
        random.Next(MinQuantity, MaxQuantity + 1);
}
