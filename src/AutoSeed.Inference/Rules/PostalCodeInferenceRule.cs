using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Inference.Rules;

/// <summary>
/// Infers a postal code for properties named <c>PostalCode</c>, <c>Cep</c>, <c>ZipCode</c> or <c>Zip</c>.
/// </summary>
public sealed class PostalCodeInferenceRule : IPropertyInferenceRule
{
    private readonly string _locale;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostalCodeInferenceRule"/> class.
    /// </summary>
    /// <param name="locale">The Bogus locale that determines the postal code format, for example <c>"en"</c> or <c>"pt_BR"</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="locale"/> is <see langword="null"/>.</exception>
    public PostalCodeInferenceRule(string locale = "en")
    {
        ArgumentNullException.ThrowIfNull(locale);
        _locale = locale;
    }

    /// <inheritdoc />
    public int Priority => 0;

    /// <inheritdoc />
    public bool CanInfer(IProperty property) =>
        property.ClrType == typeof(string) && PropertyNameMatch.EndsWithAny(property, "PostalCode", "Cep", "ZipCode", "Zip");

    /// <inheritdoc />
    public object Infer(IProperty property, SeededRandom random, IReadOnlyDictionary<string, object> generatedValues)
    {
        Bogus.DataSets.Address address = new(_locale) { Random = new Bogus.Randomizer(random.Seed) };
        return StringLengthHelper.TruncateToMaxLength(address.ZipCode(), property);
    }
}
