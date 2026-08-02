using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Inference.Rules;

/// <summary>
/// Infers an email address for properties named <c>Email</c> or <c>EmailAddress</c>, built from
/// the row's <c>FirstName</c>/<c>LastName</c> when they were already generated.
/// </summary>
public sealed class EmailInferenceRule : IPropertyInferenceRule
{
    private readonly string _locale;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailInferenceRule"/> class.
    /// </summary>
    /// <param name="locale">The Bogus locale used when no name is available to build the address from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="locale"/> is <see langword="null"/>.</exception>
    public EmailInferenceRule(string locale = "en")
    {
        ArgumentNullException.ThrowIfNull(locale);
        _locale = locale;
    }

    /// <inheritdoc />
    public int Priority => 1;

    /// <inheritdoc />
    public bool CanInfer(IProperty property) =>
        property.ClrType == typeof(string) && PropertyNameMatch.EndsWithAny(property, "Email", "EmailAddress");

    /// <inheritdoc />
    public object Infer(IProperty property, SeededRandom random, IReadOnlyDictionary<string, object> generatedValues)
    {
        string? firstName = FindSiblingString(generatedValues, "FirstName");
        string? lastName = FindSiblingString(generatedValues, "LastName");

        Bogus.DataSets.Internet internet = new(_locale) { Random = new Bogus.Randomizer(random.Seed) };
        string value = internet.Email(firstName, lastName);

        return StringLengthHelper.TruncateToMaxLength(value, property);
    }

    private static string? FindSiblingString(IReadOnlyDictionary<string, object> generatedValues, string suffix)
    {
        foreach (KeyValuePair<string, object> entry in generatedValues)
        {
            if (entry.Value is string text && entry.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return text;
            }
        }

        return null;
    }
}
