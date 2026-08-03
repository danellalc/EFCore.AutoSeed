using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Inference.Rules;

/// <summary>
/// Infers a small, non-negative number for any numeric property no other rule recognizes. Runs
/// last, so every more specific rule (like <see cref="DecimalAmountInferenceRule"/>) gets first refusal.
/// </summary>
public sealed class GenericNumberInferenceRule : IPropertyInferenceRule
{
    private const int UpperBound = 1_000;

    /// <inheritdoc />
    public int Priority => 100;

    /// <inheritdoc />
    public bool CanInfer(IProperty property) =>
        IsSupportedNumericType(property) && !property.IsForeignKey() && property.ValueGenerated == ValueGenerated.Never;

    /// <inheritdoc />
    public object? Infer(IProperty property, SeededRandom random, IReadOnlyDictionary<string, object> generatedValues)
    {
        Type clrType = UnderlyingType(property);

        if (clrType == typeof(int))
        {
            return random.Next(0, UpperBound);
        }

        if (clrType == typeof(long))
        {
            return (long)random.Next(0, UpperBound);
        }

        if (clrType == typeof(short))
        {
            return (short)random.Next(0, UpperBound);
        }

        if (clrType == typeof(byte))
        {
            return (byte)random.Next(0, 256);
        }

        if (clrType == typeof(float))
        {
            return (float)(random.NextDouble() * UpperBound);
        }

        if (clrType == typeof(double))
        {
            return random.NextDouble() * UpperBound;
        }

        int scale = Math.Clamp(property.GetScale() ?? 2, 0, 28);
        return Math.Round((decimal)(random.NextDouble() * UpperBound), scale);
    }

    private static bool IsSupportedNumericType(IProperty property)
    {
        Type clrType = UnderlyingType(property);
        return clrType == typeof(int) || clrType == typeof(long) || clrType == typeof(short) || clrType == typeof(byte)
            || clrType == typeof(float) || clrType == typeof(double) || clrType == typeof(decimal);
    }

    private static Type UnderlyingType(IProperty property) => Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
}
