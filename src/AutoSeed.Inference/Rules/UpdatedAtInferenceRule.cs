using EFCore.AutoSeed.Distributions;
using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Inference.Rules;

/// <summary>
/// Infers an <c>UpdatedAt</c>-style timestamp that never falls before the row's <c>CreatedAt</c>.
/// </summary>
public sealed class UpdatedAtInferenceRule : IPropertyInferenceRule
{
    private static readonly string[] NameSuffixes = ["UpdatedAt", "UpdatedOn", "ModifiedAt", "ModifiedOn"];
    private static readonly string[] CreatedAtSuffixes = ["CreatedAt", "CreatedOn"];

    private readonly DateTime _referenceNow;
    private readonly TimeSpan _lookback;
    private readonly TemporalClusteringOptions _temporalOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdatedAtInferenceRule"/> class.
    /// </summary>
    /// <param name="referenceNow">
    /// The fixed point in time generated dates are relative to. Never <see cref="DateTime.Now"/>
    /// or <see cref="DateTime.UtcNow"/>: the caller must supply a value that stays the same across
    /// runs for the result to stay deterministic.
    /// </param>
    /// <param name="lookback">
    /// How far before <paramref name="referenceNow"/> generated dates can start when no
    /// <c>CreatedAt</c> sibling value is available. Defaults to two years.
    /// </param>
    /// <param name="temporalOptions">The weekday/business-hour clustering shape. Defaults to <see cref="TemporalClusteringOptions.Default"/>.</param>
    public UpdatedAtInferenceRule(DateTime referenceNow, TimeSpan? lookback = null, TemporalClusteringOptions? temporalOptions = null)
    {
        _referenceNow = referenceNow;
        _lookback = lookback ?? TimeSpan.FromDays(730);
        _temporalOptions = temporalOptions ?? TemporalClusteringOptions.Default;
    }

    /// <inheritdoc />
    public int Priority => 1;

    /// <inheritdoc />
    public bool CanInfer(IProperty property) =>
        PropertyNameMatch.IsClrType<DateTime>(property) && PropertyNameMatch.EndsWithAny(property, NameSuffixes);

    /// <inheritdoc />
    public object? Infer(IProperty property, SeededRandom random, IReadOnlyDictionary<string, object> generatedValues)
    {
        DateTime lowerBound = TemporalHelpers.FindSiblingDateTime(generatedValues, CreatedAtSuffixes) ?? _referenceNow - _lookback;
        return TemporalHelpers.Between(random, lowerBound, _referenceNow, _temporalOptions);
    }
}
