using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Inference.Rules;

/// <summary>
/// Infers a <c>DeletedAt</c>-style timestamp that never falls before the row's <c>UpdatedAt</c> or
/// <c>CreatedAt</c>. Whether a given row actually receives this value or stays unset is decided
/// elsewhere; this rule always produces a value when asked.
/// </summary>
public sealed class DeletedAtInferenceRule : IPropertyInferenceRule
{
    private static readonly string[] NameSuffixes = ["DeletedAt", "DeletedOn"];
    private static readonly string[] UpdatedAtSuffixes = ["UpdatedAt", "UpdatedOn", "ModifiedAt", "ModifiedOn"];
    private static readonly string[] CreatedAtSuffixes = ["CreatedAt", "CreatedOn"];

    private readonly DateTime _referenceNow;
    private readonly TimeSpan _lookback;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeletedAtInferenceRule"/> class.
    /// </summary>
    /// <param name="referenceNow">
    /// The fixed point in time generated dates are relative to. Never <see cref="DateTime.Now"/>
    /// or <see cref="DateTime.UtcNow"/>: the caller must supply a value that stays the same across
    /// runs for the result to stay deterministic.
    /// </param>
    /// <param name="lookback">
    /// How far before <paramref name="referenceNow"/> generated dates can start when no
    /// <c>UpdatedAt</c> or <c>CreatedAt</c> sibling value is available. Defaults to two years.
    /// </param>
    public DeletedAtInferenceRule(DateTime referenceNow, TimeSpan? lookback = null)
    {
        _referenceNow = referenceNow;
        _lookback = lookback ?? TimeSpan.FromDays(730);
    }

    /// <inheritdoc />
    public int Priority => 2;

    /// <inheritdoc />
    public bool CanInfer(IProperty property) =>
        PropertyNameMatch.IsClrType<DateTime>(property) && PropertyNameMatch.EndsWithAny(property, NameSuffixes);

    /// <inheritdoc />
    public object? Infer(IProperty property, SeededRandom random, IReadOnlyDictionary<string, object> generatedValues)
    {
        DateTime lowerBound = TemporalHelpers.FindSiblingDateTime(generatedValues, UpdatedAtSuffixes)
            ?? TemporalHelpers.FindSiblingDateTime(generatedValues, CreatedAtSuffixes)
            ?? _referenceNow - _lookback;

        return TemporalHelpers.Between(random, lowerBound, _referenceNow);
    }
}
