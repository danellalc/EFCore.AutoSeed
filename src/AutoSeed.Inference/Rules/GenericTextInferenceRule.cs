using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Inference.Rules;

/// <summary>
/// Infers a short, generic phrase for any string property no other rule recognizes. Runs last, so
/// every more specific rule gets first refusal.
/// </summary>
public sealed class GenericTextInferenceRule : IPropertyInferenceRule
{
    /// <inheritdoc />
    public int Priority => 100;

    /// <inheritdoc />
    public bool CanInfer(IProperty property) =>
        property.ClrType == typeof(string) && !property.IsForeignKey();

    /// <inheritdoc />
    public object Infer(IProperty property, SeededRandom random, IReadOnlyDictionary<string, object> generatedValues)
    {
        Bogus.DataSets.Lorem lorem = new() { Random = new Bogus.Randomizer(random.Seed) };
        string value = string.Join(' ', lorem.Words(2).Select(Capitalize));
        return StringLengthHelper.TruncateToMaxLength(value, property);
    }

    private static string Capitalize(string word) =>
        word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];
}
