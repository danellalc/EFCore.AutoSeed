using System.Linq.Expressions;
using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore;

namespace EFCore.AutoSeed.Inference;

/// <summary>
/// Configures per-entity-type overrides for a seeding call: excluding an entity type entirely,
/// pinning its row count regardless of the call's <c>scale</c>, or replacing how one property's
/// value is generated. Passed to a seeding method's <c>configure</c> callback; the common case
/// needs none of this.
/// </summary>
public sealed class SeedConfigurationBuilder
{
    private readonly Dictionary<Type, Func<DbContext, CancellationToken, Task<IReadOnlyList<object>>>> _excludedEntityReaders = [];
    private readonly Dictionary<Type, int> _rowCountOverrides = [];
    private readonly Dictionary<(Type EntityType, string PropertyName), Func<SeededRandom, IReadOnlyDictionary<string, object>, object?>> _customGenerators = [];

    /// <summary>
    /// Starts configuring overrides for <typeparamref name="TEntity"/>.
    /// </summary>
    /// <typeparam name="TEntity">The entity type to configure.</typeparam>
    /// <returns>A builder scoped to <typeparamref name="TEntity"/>.</returns>
    public EntityConfigurationBuilder<TEntity> Entity<TEntity>()
        where TEntity : class =>
        new(this);

    internal IReadOnlyDictionary<Type, Func<DbContext, CancellationToken, Task<IReadOnlyList<object>>>> ExcludedEntityReaders =>
        _excludedEntityReaders;

    internal IReadOnlyDictionary<Type, int> RowCountOverrides => _rowCountOverrides;

    internal IReadOnlyDictionary<(Type EntityType, string PropertyName), Func<SeededRandom, IReadOnlyDictionary<string, object>, object?>> CustomGenerators =>
        _customGenerators;

    internal void Exclude<TEntity>()
        where TEntity : class =>
        _excludedEntityReaders[typeof(TEntity)] = static async (context, cancellationToken) =>
            await context.Set<TEntity>().Cast<object>().ToListAsync(cancellationToken).ConfigureAwait(false);

    internal void SetRowCount(Type entityType, int rowCount) => _rowCountOverrides[entityType] = rowCount;

    internal void SetCustomGenerator(Type entityType, string propertyName, Func<SeededRandom, IReadOnlyDictionary<string, object>, object?> generator) =>
        _customGenerators[(entityType, propertyName)] = generator;
}

/// <summary>
/// Configures overrides for one entity type within a <see cref="SeedConfigurationBuilder"/>.
/// </summary>
/// <typeparam name="TEntity">The entity type this builder configures.</typeparam>
public sealed class EntityConfigurationBuilder<TEntity>
    where TEntity : class
{
    private readonly SeedConfigurationBuilder _parent;

    internal EntityConfigurationBuilder(SeedConfigurationBuilder parent)
    {
        _parent = parent;
    }

    /// <summary>
    /// Excludes <typeparamref name="TEntity"/> from seeding entirely. Its already-existing rows in
    /// the database being seeded are read instead, and used as valid foreign key targets for any
    /// other entity type that requires one, exactly like freshly generated rows would be. Only
    /// supported for an entity type with no required foreign key of its own: one whose row count
    /// would otherwise be derived from a required principal's cannot be fixed independently of it.
    /// </summary>
    /// <exception cref="Exceptions.UnsupportedSeedConfigurationException">
    /// <typeparamref name="TEntity"/> has a required foreign key, thrown when the seeding call resolves this configuration.
    /// </exception>
    public void Exclude() => _parent.Exclude<TEntity>();

    /// <summary>
    /// Pins <typeparamref name="TEntity"/>'s row count to <paramref name="rowCount"/>, regardless of
    /// the seeding call's <c>scale</c>. Only supported for an entity type with no required foreign
    /// key of its own: one whose row count would otherwise be derived from a required principal's
    /// cannot be fixed independently of it.
    /// </summary>
    /// <param name="rowCount">The exact number of rows to generate for <typeparamref name="TEntity"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rowCount"/> is negative.</exception>
    /// <exception cref="Exceptions.UnsupportedSeedConfigurationException">
    /// <typeparamref name="TEntity"/> has a required foreign key, thrown when the seeding call resolves this configuration.
    /// </exception>
    public void HasRowCount(int rowCount)
    {
        if (rowCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rowCount), rowCount, "Must not be negative.");
        }

        _parent.SetRowCount(typeof(TEntity), rowCount);
    }

    /// <summary>
    /// Starts configuring a custom generator for one property, overriding every built-in inference
    /// rule for it.
    /// </summary>
    /// <typeparam name="TProperty">The property's type.</typeparam>
    /// <param name="propertySelector">A simple property access, such as <c>entity =&gt; entity.Sku</c>.</param>
    /// <returns>A builder scoped to that property.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="propertySelector"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="propertySelector"/> is not a simple property access.</exception>
    public PropertyConfigurationBuilder<TEntity, TProperty> Property<TProperty>(Expression<Func<TEntity, TProperty>> propertySelector)
    {
        ArgumentNullException.ThrowIfNull(propertySelector);
        return new(_parent, GetPropertyName(propertySelector));
    }

    private static string GetPropertyName<TProperty>(Expression<Func<TEntity, TProperty>> propertySelector)
    {
        if (propertySelector.Body is MemberExpression { Expression: ParameterExpression } memberExpression)
        {
            return memberExpression.Member.Name;
        }

        if (propertySelector.Body is UnaryExpression { Operand: MemberExpression { Expression: ParameterExpression } unaryMemberExpression })
        {
            return unaryMemberExpression.Member.Name;
        }

        throw new ArgumentException("Must be a simple property access on the entity itself, such as 'entity => entity.Sku', not a nested or owned property.", nameof(propertySelector));
    }
}

/// <summary>
/// Configures a custom generator for one property within an <see cref="EntityConfigurationBuilder{TEntity}"/>.
/// </summary>
/// <typeparam name="TEntity">The entity type that owns the property.</typeparam>
/// <typeparam name="TProperty">The property's type.</typeparam>
public sealed class PropertyConfigurationBuilder<TEntity, TProperty>
    where TEntity : class
{
    private readonly SeedConfigurationBuilder _parent;
    private readonly string _propertyName;

    internal PropertyConfigurationBuilder(SeedConfigurationBuilder parent, string propertyName)
    {
        _parent = parent;
        _propertyName = propertyName;
    }

    /// <summary>
    /// Replaces every built-in inference rule for this property with <paramref name="generator"/>.
    /// Runs before, and takes priority over, every built-in rule. Exempt from the null rate and from
    /// dirty-data noise: it is called for every row, and no rule ever sees or overwrites its result.
    /// Not exempt from uniqueness enforcement: if this property also carries a unique key or a
    /// unique index, a returned value that collides with another row's is still rewritten with a
    /// deterministic suffix, exactly like a rule-generated value would (see
    /// <see cref="Pipeline.UniquenessEnforcer"/>). Failing that fix-up is never an option: this
    /// library never lets a raw constraint violation reach the database.
    /// </summary>
    /// <param name="generator">
    /// Produces the property's value for one row. Called with the random source scoped to this
    /// property (derive further from it for more than one random draw, never use an unseeded source)
    /// and the values every other custom generator for this entity type has already produced for
    /// this same row, in ascending alphabetical order by property name. Because every custom
    /// generator for an entity type runs to completion before any built-in rule does, a rule-inferred
    /// value for another property is never in there, regardless of that property's name. Returning
    /// <see langword="null"/> leaves the property absent for that row, same as a built-in rule doing
    /// so for a nullable property. If the property is required (non-nullable) and not generated by
    /// the database, returning <see langword="null"/> instead makes the seeding call throw
    /// <see cref="Exceptions.UnsupportedPropertyException"/> for that row, rather than reaching the
    /// database at the property's CLR default.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="generator"/> is <see langword="null"/>.</exception>
    public void GenerateWith(Func<SeededRandom, IReadOnlyDictionary<string, object>, TProperty> generator)
    {
        ArgumentNullException.ThrowIfNull(generator);
        _parent.SetCustomGenerator(typeof(TEntity), _propertyName, (random, generatedValues) => generator(random, generatedValues));
    }
}
