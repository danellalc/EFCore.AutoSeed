using EFCore.AutoSeed.Distributions;
using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Inference.Rules;

/// <summary>
/// Infers a <see cref="DateTime"/> within a fixed lookback window, for any <see cref="DateTime"/>
/// property no name-specific rule (such as <see cref="CreatedAtInferenceRule"/>) recognized. Runs
/// last, so every more specific rule gets first refusal.
/// </summary>
public sealed class GenericDateTimeInferenceRule : IPropertyInferenceRule
{
    private readonly DateTime _referenceNow;
    private readonly TimeSpan _lookback;
    private readonly TemporalClusteringOptions _temporalOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenericDateTimeInferenceRule"/> class.
    /// </summary>
    /// <param name="referenceNow">
    /// The fixed point in time generated dates are relative to. Never <see cref="DateTime.Now"/>
    /// or <see cref="DateTime.UtcNow"/>: the caller must supply a value that stays the same across
    /// runs for the result to stay deterministic.
    /// </param>
    /// <param name="lookback">How far before <paramref name="referenceNow"/> generated dates can start. Defaults to two years.</param>
    /// <param name="temporalOptions">The weekday/business-hour clustering shape. Defaults to <see cref="TemporalClusteringOptions.Default"/>.</param>
    public GenericDateTimeInferenceRule(DateTime referenceNow, TimeSpan? lookback = null, TemporalClusteringOptions? temporalOptions = null)
    {
        _referenceNow = referenceNow;
        _lookback = lookback ?? TimeSpan.FromDays(730);
        _temporalOptions = temporalOptions ?? TemporalClusteringOptions.Default;
    }

    /// <inheritdoc />
    public int Priority => 100;

    /// <inheritdoc />
    public bool CanInfer(IProperty property) =>
        PropertyNameMatch.IsClrType<DateTime>(property) && !property.IsForeignKey() && property.ValueGenerated == ValueGenerated.Never;

    /// <inheritdoc />
    public object? Infer(IProperty property, SeededRandom random, IReadOnlyDictionary<string, object> generatedValues) =>
        TemporalHelpers.Between(random, _referenceNow - _lookback, _referenceNow, _temporalOptions);
}
