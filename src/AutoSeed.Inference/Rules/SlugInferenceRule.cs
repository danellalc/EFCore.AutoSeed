using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Inference.Rules;

/// <summary>
/// Infers a URL slug for properties named <c>Slug</c>.
/// </summary>
public sealed class SlugInferenceRule : IPropertyInferenceRule
{
    /// <inheritdoc />
    public int Priority => 0;

    /// <inheritdoc />
    public bool CanInfer(IProperty property) =>
        property.ClrType == typeof(string) && PropertyNameMatch.EndsWithAny(property, "Slug");

    /// <inheritdoc />
    public object Infer(IProperty property, SeededRandom random, IReadOnlyDictionary<string, object> generatedValues)
    {
        Bogus.DataSets.Internet internet = new() { Random = new Bogus.Randomizer(random.Seed) };
        string slug = string.Join('-', internet.DomainWord(), internet.DomainWord());
        return StringLengthHelper.TruncateToMaxLength(slug, property);
    }
}
