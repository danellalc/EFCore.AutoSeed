using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Inference;

/// <summary>
/// Generates values for every property of one row by applying a set of
/// <see cref="IPropertyInferenceRule"/> in priority order.
/// </summary>
public sealed class RowValueGenerator
{
    private readonly IReadOnlyList<IPropertyInferenceRule> _rulesByPriority;

    /// <summary>
    /// Initializes a new instance of the <see cref="RowValueGenerator"/> class.
    /// </summary>
    /// <param name="rules">The rules to apply, in any order; they are sorted by <see cref="IPropertyInferenceRule.Priority"/> internally.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> is <see langword="null"/>.</exception>
    public RowValueGenerator(IReadOnlyList<IPropertyInferenceRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rulesByPriority = [.. rules.OrderBy(rule => rule.Priority)];
    }

    /// <summary>
    /// Generates a value for every property of <paramref name="entityType"/> that at least one
    /// rule recognizes. Properties no rule recognizes are simply absent from the result.
    /// </summary>
    /// <param name="entityType">The entity type whose properties to generate values for.</param>
    /// <param name="rowRandom">
    /// The random source scoped to this row. Each property's value is derived from
    /// <c>rowRandom.Derive(property.Name)</c>, so it is independent of every other property.
    /// </param>
    /// <returns>The generated values, keyed by property name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entityType"/> or <paramref name="rowRandom"/> is <see langword="null"/>.</exception>
    public Dictionary<string, object> GenerateRow(IEntityType entityType, SeededRandom rowRandom)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(rowRandom);

        IReadOnlyList<IProperty> properties = [.. entityType.GetProperties().OrderBy(property => property.Name, StringComparer.Ordinal)];
        Dictionary<string, object> values = [];

        foreach (IPropertyInferenceRule rule in _rulesByPriority)
        {
            foreach (IProperty property in properties)
            {
                if (values.ContainsKey(property.Name) || !rule.CanInfer(property))
                {
                    continue;
                }

                SeededRandom propertyRandom = rowRandom.Derive(property.Name);
                values[property.Name] = rule.Infer(property, propertyRandom, values);
            }
        }

        return values;
    }
}
