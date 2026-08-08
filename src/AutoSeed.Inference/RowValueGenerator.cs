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
    private readonly IReadOnlyDictionary<(IEntityType EntityType, string PropertyName), Func<SeededRandom, IReadOnlyDictionary<string, object>, object?>> _customGenerators;

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
    /// <param name="customGenerators">
    /// A generator for a specific entity type's specific property, keyed by both. Takes priority
    /// over every rule in <paramref name="rules"/> for that property, and is exempt from the null
    /// rate and dirty-data noise. Defaults to none configured.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> is <see langword="null"/>.</exception>
    public RowValueGenerator(
        IReadOnlyList<IPropertyInferenceRule> rules,
        double nullRate = NullRateSampler.DefaultRate,
        DirtyDataKind dirtyData = DirtyDataKind.None,
        IReadOnlyDictionary<(IEntityType EntityType, string PropertyName), Func<SeededRandom, IReadOnlyDictionary<string, object>, object?>>? customGenerators = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rulesByPriority = [.. rules.OrderBy(rule => rule.Priority)];
        _nullRate = nullRate;
        _dirtyData = dirtyData;
        _customGenerators = customGenerators ?? new Dictionary<(IEntityType, string), Func<SeededRandom, IReadOnlyDictionary<string, object>, object?>>();
    }

    /// <summary>
    /// Generates a value for every property of <paramref name="entityType"/> that has a custom
    /// generator (see the constructor's <c>customGenerators</c> parameter, which always wins over
    /// every rule) or that at least one rule recognizes. A property neither covers is simply absent
    /// from the result, unless it is required (non-nullable), in which case a custom generator
    /// returning <see langword="null"/> for it throws instead of leaving it absent, see
    /// <see cref="UnsupportedPropertyException"/> below. The table-per-hierarchy discriminator column, if any, is never touched: EF Core sets it from
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
    /// <exception cref="UnsupportedPropertyException">
    /// A required (non-nullable), non-foreign-key property with <see cref="ValueGenerated.Never"/>
    /// has a custom generator that returned <see langword="null"/> for this row.
    /// </exception>
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

        foreach (IProperty property in properties)
        {
            if (!_customGenerators.TryGetValue((entityType, property.Name), out Func<SeededRandom, IReadOnlyDictionary<string, object>, object?>? generator))
            {
                continue;
            }

            claimedProperties.Add(property.Name);
            nullRateExempt.Add(property.Name);

            SeededRandom propertyRandom = rowRandom.Derive(property.Name);
            object? value = generator(propertyRandom, values);
            if (value is not null)
            {
                values[property.Name] = value;
            }
            else if (IsRequired(property))
            {
                throw new UnsupportedPropertyException(entityType.Name, property.Name, "its custom generator returned null for this row");
            }
        }

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
    /// (<see cref="ValueGenerated.Never"/>) has a custom generator or is recognized by at least one
    /// rule, so <see cref="GenerateRow"/> never silently leaves it at its CLR default. Neither
    /// depends on generated data, so this can run once up front instead of after every row.
    /// </summary>
    /// <param name="entityTypes">The entity types about to be seeded.</param>
    /// <exception cref="ArgumentNullException"><paramref name="entityTypes"/> is <see langword="null"/>.</exception>
    /// <exception cref="UnsupportedPropertyException">
    /// A required property is unclaimed, or a required complex property is present: neither
    /// <see cref="GenerateRow"/> nor a custom generator walks a complex property's own properties yet.
    /// </exception>
    public void ValidateRequiredProperties(IEnumerable<IEntityType> entityTypes)
    {
        ArgumentNullException.ThrowIfNull(entityTypes);

        foreach (IEntityType entityType in entityTypes)
        {
            ValidateComplexProperties(entityType, entityType.GetComplexProperties(), path: null);

            IProperty? discriminatorProperty = entityType.FindDiscriminatorProperty();
            foreach (IProperty property in entityType.GetProperties())
            {
                if (property == discriminatorProperty
                    || !IsRequired(property)
                    || _customGenerators.ContainsKey((entityType, property.Name))
                    || _rulesByPriority.Any(rule => rule.CanInfer(property)))
                {
                    continue;
                }

                throw new UnsupportedPropertyException(
                    entityType.Name,
                    property.Name,
                    $"no inference rule recognizes its type ({property.ClrType.Name}). Open an issue describing the property's shape");
            }
        }
    }

    private static void ValidateComplexProperties(IEntityType entityType, IEnumerable<IComplexProperty> complexProperties, string? path)
    {
        foreach (IComplexProperty complexProperty in complexProperties)
        {
            string propertyPath = path is null ? complexProperty.Name : $"{path}.{complexProperty.Name}";
            if (!complexProperty.IsNullable)
            {
                throw new UnsupportedPropertyException(
                    entityType.Name,
                    propertyPath,
                    $"is a complex property ({complexProperty.ClrType.Name}); AutoSeed does not generate values for complex " +
                    "properties yet, open an issue describing the shape");
            }

            ValidateComplexProperties(entityType, complexProperty.ComplexType.GetComplexProperties(), propertyPath);
        }
    }

    private static bool IsRequired(IProperty property) =>
        !property.IsNullable && !property.IsForeignKey() && property.ValueGenerated == ValueGenerated.Never;

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
