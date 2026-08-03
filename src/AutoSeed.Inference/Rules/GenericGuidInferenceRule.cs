using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Inference.Rules;

/// <summary>
/// Infers a <see cref="Guid"/> value from 16 seeded bytes, for any <see cref="Guid"/> property EF
/// Core is not already generating a value for.
/// </summary>
public sealed class GenericGuidInferenceRule : IPropertyInferenceRule
{
    /// <inheritdoc />
    public int Priority => 100;

    /// <inheritdoc />
    public bool CanInfer(IProperty property) =>
        PropertyNameMatch.IsClrType<Guid>(property) && !property.IsForeignKey() && property.ValueGenerated == ValueGenerated.Never;

    /// <inheritdoc />
    public object? Infer(IProperty property, SeededRandom random, IReadOnlyDictionary<string, object> generatedValues)
    {
        byte[] bytes = new byte[16];
        for (int index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte)random.Next(0, 256);
        }

        return new Guid(bytes);
    }
}
