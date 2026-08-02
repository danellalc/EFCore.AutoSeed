using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Inference.Rules;

/// <summary>
/// Infers a phone number for properties named <c>Phone</c>, <c>Mobile</c>, <c>PhoneNumber</c> or <c>MobileNumber</c>.
/// </summary>
public sealed class PhoneInferenceRule : IPropertyInferenceRule
{
    private readonly string _locale;

    /// <summary>
    /// Initializes a new instance of the <see cref="PhoneInferenceRule"/> class.
    /// </summary>
    /// <param name="locale">The Bogus locale that determines the phone number format, for example <c>"en"</c> or <c>"pt_BR"</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="locale"/> is <see langword="null"/>.</exception>
    public PhoneInferenceRule(string locale = "en")
    {
        ArgumentNullException.ThrowIfNull(locale);
        _locale = locale;
    }

    /// <inheritdoc />
    public int Priority => 0;

    /// <inheritdoc />
    public bool CanInfer(IProperty property) =>
        property.ClrType == typeof(string) && PropertyNameMatch.EndsWithAny(property, "Phone", "Mobile", "PhoneNumber", "MobileNumber");

    /// <inheritdoc />
    public object Infer(IProperty property, SeededRandom random, IReadOnlyDictionary<string, object> generatedValues)
    {
        Bogus.DataSets.PhoneNumbers phoneNumbers = new(_locale) { Random = new Bogus.Randomizer(random.Seed) };
        return StringLengthHelper.TruncateToMaxLength(phoneNumbers.PhoneNumber(), property);
    }
}
