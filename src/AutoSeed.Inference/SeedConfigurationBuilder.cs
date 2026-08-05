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

    /// <summary>
    /// Every excluded entity type's CLR type, paired with a reader that fetches its already-existing
    /// rows from the database being seeded.
    /// </summary>
    internal IReadOnlyDictionary<Type, Func<DbContext, CancellationToken, Task<IReadOnlyList<object>>>> ExcludedEntityReaders =>
        _excludedEntityReaders;

    /// <summary>
    /// Every entity type with a pinned row count, keyed by CLR type.
    /// </summary>
    internal IReadOnlyDictionary<Type, int> RowCountOverrides => _rowCountOverrides;

    /// <summary>
    /// Every property with a custom generator, keyed by the owning entity type's CLR type and the property's name.
    /// </summary>
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
    /// other entity type that requires one, exactly like freshly generated rows would be.
    /// </summary>
    public void Exclude() => _parent.Exclude<TEntity>();

    /// <summary>
    /// Pins <typeparamref name="TEntity"/>'s row count to <paramref name="rowCount"/>, regardless of
    /// the seeding call's <c>scale</c>.
    /// </summary>
    /// <param name="rowCount">The exact number of rows to generate for <typeparamref name="TEntity"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rowCount"/> is negative.</exception>
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
    /// <exception cref="ArgumentException"><paramref name="propertySelector"/> is not a simple property access.</exception>
    public PropertyConfigurationBuilder<TEntity, TProperty> Property<TProperty>(Expression<Func<TEntity, TProperty>> propertySelector) =>
        new(_parent, GetPropertyName(propertySelector));

    private static string GetPropertyName<TProperty>(Expression<Func<TEntity, TProperty>> propertySelector)
    {
        if (propertySelector.Body is MemberExpression memberExpression)
        {
            return memberExpression.Member.Name;
        }

        if (propertySelector.Body is UnaryExpression { Operand: MemberExpression unaryMemberExpression })
        {
            return unaryMemberExpression.Member.Name;
        }

        throw new ArgumentException("Must be a simple property access, such as 'entity => entity.Sku'.", nameof(propertySelector));
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
    /// dirty-data noise: it is called for every row, and its result is used exactly as returned.
    /// </summary>
    /// <param name="generator">
    /// Produces the property's value for one row. Called with the random source scoped to this
    /// property (derive further from it for more than one random draw, never use an unseeded source)
    /// and the values already generated for other properties on this same row. Returning
    /// <see langword="null"/> leaves the property absent for that row, same as a built-in rule doing
    /// so for a nullable property.
    /// </param>
    public void GenerateWith(Func<SeededRandom, IReadOnlyDictionary<string, object>, TProperty> generator)
    {
        ArgumentNullException.ThrowIfNull(generator);
        _parent.SetCustomGenerator(typeof(TEntity), _propertyName, (random, generatedValues) => generator(random, generatedValues));
    }
}
