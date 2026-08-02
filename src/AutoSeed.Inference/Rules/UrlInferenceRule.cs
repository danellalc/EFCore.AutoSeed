using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Inference.Rules;

/// <summary>
/// Infers a URL for properties named <c>Url</c>, <c>Uri</c> or <c>Link</c>.
/// </summary>
public sealed class UrlInferenceRule : IPropertyInferenceRule
{
    /// <inheritdoc />
    public int Priority => 0;

    /// <inheritdoc />
    public bool CanInfer(IProperty property) =>
        property.ClrType == typeof(string) && PropertyNameMatch.EndsWithAny(property, "Url", "Uri", "Link");

    /// <inheritdoc />
    public object Infer(IProperty property, SeededRandom random, IReadOnlyDictionary<string, object> generatedValues)
    {
        Bogus.DataSets.Internet internet = new() { Random = new Bogus.Randomizer(random.Seed) };
        return StringLengthHelper.TruncateToMaxLength(internet.Url(), property);
    }
}
