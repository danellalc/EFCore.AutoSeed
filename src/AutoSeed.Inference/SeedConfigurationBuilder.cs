using Microsoft.EntityFrameworkCore;

namespace EFCore.AutoSeed.Inference;

/// <summary>
/// Configures per-entity-type overrides for a seeding call: excluding an entity type entirely, or
/// pinning its row count regardless of the call's <c>scale</c>. Passed to a seeding method's
/// <c>configure</c> callback; the common case needs none of this.
/// </summary>
public sealed class SeedConfigurationBuilder
{
    private readonly Dictionary<Type, Func<DbContext, CancellationToken, Task<IReadOnlyList<object>>>> _excludedEntityReaders = [];
    private readonly Dictionary<Type, int> _rowCountOverrides = [];

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

    internal void Exclude<TEntity>()
        where TEntity : class =>
        _excludedEntityReaders[typeof(TEntity)] = static async (context, cancellationToken) =>
            await context.Set<TEntity>().Cast<object>().ToListAsync(cancellationToken).ConfigureAwait(false);

    internal void SetRowCount(Type entityType, int rowCount) => _rowCountOverrides[entityType] = rowCount;
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
}
