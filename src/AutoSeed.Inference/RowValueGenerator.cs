using EFCore.AutoSeed.Distributions;
using EFCore.AutoSeed.Exceptions;
using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Inference;

/// <summary>
/// Generates values for every property of one row by applying a set of
/// <see cref="IPropertyInferenceRule"/> in priority order.
/// </summary>
public sealed class RowValueGenerator
{
    private readonly IReadOnlyList<IPropertyInferenceRule> _rulesByPriority;
    private readonly double _nullRate;
    private readonly DirtyDataKind _dirtyData;

    /// <summary>
    /// Initializes a new instance of the <see cref="RowValueGenerator"/> class.
    /// </summary>
    /// <param name="rules">The rules to apply, in any order; they are sorted by <see cref="IPropertyInferenceRule.Priority"/> internally.</param>
    /// <param name="nullRate">
    /// The fraction of eligible nullable columns whose generated value is discarded, in <c>[0, 1]</c>.
    /// Defaults to <see cref="NullRateSampler.DefaultRate"/>.
    /// </param>
    /// <param name="dirtyData">
    /// The kinds of casing, whitespace and diacritic noise applied to free-text values after
    /// generation, for properties whose claiming rule allows it (see
    /// <see cref="IPropertyInferenceRule.AllowsDirtyData"/>). Defaults to
    /// <see cref="DirtyDataKind.None"/>: no noise.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> is <see langword="null"/>.</exception>
    public RowValueGenerator(
        IReadOnlyList<IPropertyInferenceRule> rules, double nullRate = NullRateSampler.DefaultRate, DirtyDataKind dirtyData = DirtyDataKind.None)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rulesByPriority = [.. rules.OrderBy(rule => rule.Priority)];
        _nullRate = nullRate;
        _dirtyData = dirtyData;
    }

    /// <summary>
    /// Generates a value for every property of <paramref name="entityType"/> that at least one
    /// rule recognizes. Properties no rule recognizes are simply absent from the result. The
    /// table-per-hierarchy discriminator column, if any, is never touched: EF Core sets it from
    /// the instance's actual CLR type during <c>SaveChanges</c>, and overwriting it with a
    /// generated value breaks every query that filters by it on a real relational database.
    /// After every rule has run, a nullable, non-foreign-key property that a rule claimed (and
    /// that the claiming rule does not already control the nullability of, see
    /// <see cref="IPropertyInferenceRule.ControlsNullability"/>) has its generated value discarded
    /// for about 10% of rows, so nullable columns are not populated on every single row.
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

        IProperty? discriminatorProperty = entityType.FindDiscriminatorProperty();
        IReadOnlyList<IProperty> properties = [.. entityType.GetProperties()
            .Where(property => property != discriminatorProperty)
            .OrderBy(property => property.Name, StringComparer.Ordinal)];
        Dictionary<string, object> values = [];
        HashSet<string> claimedProperties = [];
        HashSet<string> nullRateExempt = [];
        HashSet<string> dirtyDataEligible = [];

        foreach (IPropertyInferenceRule rule in _rulesByPriority)
        {
            foreach (IProperty property in properties)
            {
                if (claimedProperties.Contains(property.Name) || !rule.CanInfer(property))
                {
                    continue;
                }

                claimedProperties.Add(property.Name);
                if (rule.ControlsNullability)
                {
                    nullRateExempt.Add(property.Name);
                }

                if (rule.AllowsDirtyData)
                {
                    dirtyDataEligible.Add(property.Name);
                }

                SeededRandom propertyRandom = rowRandom.Derive(property.Name);
                object? value = rule.Infer(property, propertyRandom, values);
                if (value is not null)
                {
                    values[property.Name] = value;
                }
            }
        }

        ApplyNullRate(properties, values, nullRateExempt, rowRandom);
        if (_dirtyData != DirtyDataKind.None)
        {
            ApplyDirtyData(properties, values, dirtyDataEligible, rowRandom);
        }

        return values;
    }

    /// <summary>
    /// Checks that every required (non-nullable), non-foreign-key property of each entity type in
    /// <paramref name="entityTypes"/> that EF Core itself does not generate a value for
    /// (<see cref="ValueGenerated.Never"/>) is recognized by at least one rule, so
    /// <see cref="GenerateRow"/> never silently leaves it at its CLR default. Whether a rule
    /// recognizes a property depends only on the entity type and the property itself, never on
    /// generated data, so this can run once up front instead of after every row.
    /// </summary>
    /// <param name="entityTypes">The entity types about to be seeded.</param>
    /// <exception cref="ArgumentNullException"><paramref name="entityTypes"/> is <see langword="null"/>.</exception>
    /// <exception cref="UnsupportedPropertyException">A required property is unclaimed.</exception>
    public void ValidateRequiredProperties(IEnumerable<IEntityType> entityTypes)
    {
        ArgumentNullException.ThrowIfNull(entityTypes);

        foreach (IEntityType entityType in entityTypes)
        {
            IProperty? discriminatorProperty = entityType.FindDiscriminatorProperty();
            foreach (IProperty property in entityType.GetProperties())
            {
                if (property == discriminatorProperty
                    || property.IsNullable
                    || property.IsForeignKey()
                    || property.ValueGenerated != ValueGenerated.Never
                    || _rulesByPriority.Any(rule => rule.CanInfer(property)))
                {
                    continue;
                }

                throw new UnsupportedPropertyException(entityType.Name, property.Name, property.ClrType.Name);
            }
        }
    }

    private void ApplyNullRate(
        IReadOnlyList<IProperty> properties, Dictionary<string, object> values, HashSet<string> exempt, SeededRandom rowRandom)
    {
        foreach (IProperty property in properties)
        {
            if (!values.ContainsKey(property.Name) || !property.IsNullable || property.IsForeignKey() || exempt.Contains(property.Name))
            {
                continue;
            }

            SeededRandom nullRateRandom = rowRandom.Derive(property.Name).Derive("NullRate");
            if (NullRateSampler.ShouldLeaveNull(nullRateRandom, _nullRate))
            {
                values.Remove(property.Name);
            }
        }
    }

    private void ApplyDirtyData(
        IReadOnlyList<IProperty> properties, Dictionary<string, object> values, HashSet<string> eligible, SeededRandom rowRandom)
    {
        foreach (IProperty property in properties)
        {
            if (!eligible.Contains(property.Name)
                || !values.TryGetValue(property.Name, out object? value)
                || value is not string stringValue)
            {
                continue;
            }

            SeededRandom dirtyRandom = rowRandom.Derive(property.Name).Derive("DirtyData");
            values[property.Name] = DirtyDataTransform.Apply(stringValue, dirtyRandom, _dirtyData);
        }
    }
}
