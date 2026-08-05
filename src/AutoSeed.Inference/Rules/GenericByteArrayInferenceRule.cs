using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Inference.Rules;

/// <summary>
/// Infers a byte array of seeded, random bytes for any <see cref="byte"/>[] property no other rule
/// recognizes, sized to the property's <see cref="IReadOnlyProperty.GetMaxLength"/> when set.
/// Never claims a concurrency token: those are <see cref="ValueGenerated.OnAddOrUpdate"/> and
/// already excluded by <see cref="CanInfer"/>.
/// </summary>
public sealed class GenericByteArrayInferenceRule : IPropertyInferenceRule
{
    private const int DefaultLength = 16;

    /// <inheritdoc />
    public int Priority => 100;

    /// <inheritdoc />
    public bool CanInfer(IProperty property) =>
        property.ClrType == typeof(byte[]) && !property.IsForeignKey() && property.ValueGenerated == ValueGenerated.Never;

    /// <inheritdoc />
    public object? Infer(IProperty property, SeededRandom random, IReadOnlyDictionary<string, object> generatedValues)
    {
        int? maxLength = property.GetMaxLength();
        int length = maxLength is > 0 ? Math.Min(maxLength.Value, DefaultLength) : DefaultLength;
        byte[] bytes = new byte[length];
        for (int index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte)random.Next(0, 256);
        }

        return bytes;
    }
}
