using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Inference.Rules;

/// <summary>
/// Infers a <see cref="TimeOnly"/> somewhere within a day, for any <see cref="TimeOnly"/> property
/// no other rule recognizes. Runs last, so any more specific rule gets first refusal.
/// </summary>
public sealed class GenericTimeOnlyInferenceRule : IPropertyInferenceRule
{
    /// <inheritdoc />
    public int Priority => 100;

    /// <inheritdoc />
    public bool CanInfer(IProperty property) =>
        PropertyNameMatch.IsClrType<TimeOnly>(property) && !property.IsForeignKey() && property.ValueGenerated == ValueGenerated.Never;

    /// <inheritdoc />
    public object? Infer(IProperty property, SeededRandom random, IReadOnlyDictionary<string, object> generatedValues) =>
        TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(random.Next(0, 24 * 60)));
}
