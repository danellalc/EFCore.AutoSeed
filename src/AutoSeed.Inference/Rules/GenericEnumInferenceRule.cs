using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Inference.Rules;

/// <summary>
/// Infers a valid value for any enum-typed property, picked from that enum's own declared values.
/// </summary>
public sealed class GenericEnumInferenceRule : IPropertyInferenceRule
{
    /// <inheritdoc />
    public int Priority => 100;

    /// <inheritdoc />
    public bool CanInfer(IProperty property) =>
        UnderlyingEnumType(property) is not null && !property.IsForeignKey() && property.ValueGenerated == ValueGenerated.Never;

    /// <inheritdoc />
    public object? Infer(IProperty property, SeededRandom random, IReadOnlyDictionary<string, object> generatedValues)
    {
        Type clrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
        object[] values = [.. Enum.GetValues(clrType).Cast<object>()];
        return values[random.Next(0, values.Length)];
    }

    private static Type? UnderlyingEnumType(IProperty property)
    {
        Type clrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
        return clrType.IsEnum ? clrType : null;
    }
}
