using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Inference.Rules;

/// <summary>
/// Infers a person's first, last or full name for properties named <c>FirstName</c>,
/// <c>LastName</c> or <c>FullName</c>.
/// </summary>
public sealed class NameInferenceRule : IPropertyInferenceRule
{
    private readonly string _locale;

    /// <summary>
    /// Initializes a new instance of the <see cref="NameInferenceRule"/> class.
    /// </summary>
    /// <param name="locale">The Bogus locale used to generate names, for example <c>"en"</c> or <c>"pt_BR"</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="locale"/> is <see langword="null"/>.</exception>
    public NameInferenceRule(string locale = "en")
    {
        ArgumentNullException.ThrowIfNull(locale);
        _locale = locale;
    }

    /// <inheritdoc />
    public int Priority => 0;

    /// <inheritdoc />
    public bool CanInfer(IProperty property) =>
        property.ClrType == typeof(string) && PropertyNameMatch.EndsWithAny(property, "FirstName", "LastName", "FullName");

    /// <inheritdoc />
    public object? Infer(IProperty property, SeededRandom random, IReadOnlyDictionary<string, object> generatedValues)
    {
        Bogus.DataSets.Name nameDataSet = new(_locale) { Random = new Bogus.Randomizer(random.Seed) };

        string value = property.Name.EndsWith("FirstName", StringComparison.OrdinalIgnoreCase) ? nameDataSet.FirstName()
            : property.Name.EndsWith("LastName", StringComparison.OrdinalIgnoreCase) ? nameDataSet.LastName()
            : nameDataSet.FullName();

        return StringLengthHelper.TruncateToMaxLength(value, property);
    }
}
