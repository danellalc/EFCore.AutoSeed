using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Inference.Rules;

internal static class DecimalAmountHelper
{
    private const decimal DefaultMax = 10_000m;
    private const int MaxSafePowerOfTen = 27;

    internal static decimal DrawIndependentAmount(IProperty property, SeededRandom random)
    {
        int scale = property.GetScale() ?? 2;
        decimal max = MaxForPrecision(property.GetPrecision(), scale);

        Bogus.DataSets.Finance finance = new() { Random = new Bogus.Randomizer(random.Seed) };
        return finance.Amount(0m, max, scale);
    }

    /// <summary>
    /// Clamps an already-computed value (a correlated product, say) to what the property's own
    /// declared precision can actually hold. Unlike <see cref="MaxForPrecision"/>, this never
    /// substitutes the smaller "realistic independent amount" ceiling: a value derived from real
    /// sibling data should only be rejected by the column's own limit, not by a limit meant for
    /// values drawn out of thin air.
    /// </summary>
    internal static decimal ClampToPrecision(decimal value, IProperty property)
    {
        int scale = property.GetScale() ?? 2;
        decimal max = TruePrecisionBound(property.GetPrecision(), scale);
        return Math.Round(Math.Min(Math.Max(value, 0m), max), scale);
    }

    private static decimal MaxForPrecision(int? precision, int scale) => Math.Min(DefaultMax, TruePrecisionBound(precision, scale));

    private static decimal TruePrecisionBound(int? precision, int scale)
    {
        if (precision is not > 0 || precision.Value >= MaxSafePowerOfTen || scale is < 0 or >= MaxSafePowerOfTen)
        {
            return decimal.MaxValue;
        }

        return (Pow10(precision.Value) - 1) / Pow10(scale);
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
