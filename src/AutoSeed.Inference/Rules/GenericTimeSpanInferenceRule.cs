using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Inference.Rules;

/// <summary>
/// Infers a <see cref="TimeSpan"/> duration between zero and 24 hours, for any
/// <see cref="TimeSpan"/> property no other rule recognizes. Runs last, so any more specific rule
/// gets first refusal.
/// </summary>
public sealed class GenericTimeSpanInferenceRule : IPropertyInferenceRule
{
    /// <inheritdoc />
    public int Priority => 100;

    /// <inheritdoc />
    public bool CanInfer(IProperty property) =>
        PropertyNameMatch.IsClrType<TimeSpan>(property) && !property.IsForeignKey() && property.ValueGenerated == ValueGenerated.Never;

    /// <inheritdoc />
    public object? Infer(IProperty property, SeededRandom random, IReadOnlyDictionary<string, object> generatedValues) =>
        TimeSpan.FromMinutes(random.Next(0, 24 * 60));
}
