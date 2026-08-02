using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Inference;

/// <summary>
/// Infers a plausible value for a property from its name and CLR type. Rules run in
/// <see cref="Priority"/> order within a row, so a rule that needs a sibling property's value
/// (an email built from a first and last name, say) can declare a higher priority than the rule
/// that produces that sibling value.
/// </summary>
public interface IPropertyInferenceRule
{
    /// <summary>
    /// The order this rule runs in relative to other rules when generating a row. Lower values
    /// run first. A rule that reads sibling values through <see cref="Infer"/> must use a higher
    /// priority than the rules that produce the values it reads.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Determines whether this rule knows how to infer a value for <paramref name="property"/>.
    /// </summary>
    /// <param name="property">The property being generated.</param>
    /// <returns><see langword="true"/> if this rule can infer a value for <paramref name="property"/>.</returns>
    bool CanInfer(IProperty property);

    /// <summary>
    /// Infers a value for <paramref name="property"/>.
    /// </summary>
    /// <param name="property">The property being generated.</param>
    /// <param name="random">
    /// The random source scoped to this property. Every draw from it must go through
    /// <see cref="SeededRandom"/> or a generator seeded from <see cref="SeededRandom.Seed"/>.
    /// </param>
    /// <param name="generatedValues">
    /// The values already generated for other properties on this same row, keyed by property name.
    /// Only properties inferred by a lower-priority rule are guaranteed to be present.
    /// </param>
    /// <returns>The inferred value. Never <see langword="null"/>; whether a row actually gets a null is decided elsewhere.</returns>
    object Infer(IProperty property, SeededRandom random, IReadOnlyDictionary<string, object> generatedValues);
}
