using EFCore.AutoSeed.Coverage;
using EFCore.AutoSeed.Inference;
using EFCore.AutoSeed.Inference.Rules;
using EFCore.AutoSeed.Pipeline;
using EFCore.AutoSeed.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed;

/// <summary>
/// The public entry point: seeds a <see cref="DbContext"/> by reading its model.
/// </summary>
public static class DbContextAutoSeedExtensions
{
    /// <summary>
    /// A fixed point in time every inferred lifecycle timestamp (<c>CreatedAt</c>, <c>UpdatedAt</c>,
    /// <c>DeletedAt</c>) is relative to. Deliberately not <see cref="DateTime.UtcNow"/>: reading the
    /// clock here would make the same seed produce different data on different days, breaking the
    /// determinism guarantee. Revisit only as part of a documented breaking change.
    /// </summary>
    private static readonly DateTime ReferenceNow = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// The seed every <see cref="AutoSeedCoverageAsync"/> run derives from. Fixed rather than an
    /// argument: coverage mode's row counts and boundary values are already structural, not scaled,
    /// so there is nothing meaningful for a caller-supplied seed to vary except Guid properties.
    /// </summary>
    private const long CoverageSeed = 0;

    /// <summary>
    /// Reads <paramref name="context"/>'s model, works out an insertion order that satisfies every
    /// foreign key, infers realistic values from each property's name, and writes the rows through
    /// EF Core.
    /// </summary>
    /// <param name="context">The context to seed.</param>
    /// <param name="seed">The seed every generated value derives from. The same seed always produces the same data.</param>
    /// <param name="scale">The row count for entity types with no required principal.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number of rows inserted, keyed by entity type name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scale"/> is not positive.</exception>
    /// <exception cref="Exceptions.UnresolvableCycleException">
    /// The model contains a dependency cycle made entirely of required foreign keys.
    /// </exception>
    /// <exception cref="Exceptions.UnsupportedEntityTypeException">
    /// An entity type has no public parameterless constructor, or a required principal has no generated rows.
    /// </exception>
    /// <exception cref="Exceptions.UnsatisfiableUniquenessException">
    /// A unique property ran out of deterministic candidates to resolve a collision.
    /// </exception>
    public static async Task<IReadOnlyDictionary<string, int>> AutoSeedAsync(
        this DbContext context, long seed, int scale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        ModelReadResult read = new ModelReader().Read(context.Model);
        CycleResolution resolution = new CycleResolver().Resolve(read.EntityTypes, read.Edges);

        SeededRandom rootRandom = SeededRandom.FromRootSeed(seed);

        IReadOnlyList<EntityGenerationPlan> plan = new GenerationPlan()
            .Plan(resolution.Order, read.Edges, scale, rootRandom.Derive("GenerationPlan"));

        RowValueGenerator rowValueGenerator = new(BuildDefaultRules());

        return await new Persistence()
            .InsertAsync(
                context,
                plan,
                read.Edges,
                resolution.DeferredEdges,
                (entityType, random) => rowValueGenerator.GenerateRow(entityType, random),
                rootRandom.Derive("Persistence"),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Like <see cref="AutoSeedAsync"/>, but writes through a database-specific bulk insert
    /// (<c>SqlBulkCopy</c> on SQL Server, binary <c>COPY</c> on PostgreSQL) instead of
    /// <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>, bypassing the change tracker
    /// entirely. Faster at scale; supports only entity types simple enough to make that safe.
    /// </summary>
    /// <param name="context">The context to seed.</param>
    /// <param name="seed">The seed every generated value derives from. The same seed always produces the same data.</param>
    /// <param name="scale">The row count for entity types with no required principal.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number of rows inserted, keyed by entity type name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scale"/> is not positive.</exception>
    /// <exception cref="Exceptions.UnresolvableCycleException">
    /// The model contains a dependency cycle made entirely of required foreign keys.
    /// </exception>
    /// <exception cref="Exceptions.UnsupportedProviderException">
    /// <paramref name="context"/>'s configured EF Core provider is neither SQL Server nor PostgreSQL.
    /// </exception>
    /// <exception cref="Exceptions.UnsupportedEntityTypeException">
    /// The model needs a cycle-breaking second pass, declares an inherited or owned entity type, a
    /// single-column identity primary key of a type fast mode does not assign itself, has no public
    /// parameterless constructor, or a required principal has no generated rows.
    /// </exception>
    /// <exception cref="Exceptions.UnsatisfiableUniquenessException">
    /// A unique property ran out of deterministic candidates to resolve a collision.
    /// </exception>
    public static async Task<IReadOnlyDictionary<string, int>> AutoSeedFastAsync(
        this DbContext context, long seed, int scale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        ModelReadResult read = new ModelReader().Read(context.Model);
        CycleResolution resolution = new CycleResolver().Resolve(read.EntityTypes, read.Edges);

        SeededRandom rootRandom = SeededRandom.FromRootSeed(seed);

        IReadOnlyList<EntityGenerationPlan> plan = new GenerationPlan()
            .Plan(resolution.Order, read.Edges, scale, rootRandom.Derive("GenerationPlan"));

        RowValueGenerator rowValueGenerator = new(BuildDefaultRules());
        IBulkInsertProvider provider = BulkInsertProviderFactory.Create(context);

        return await new BulkPersistence(provider)
            .InsertAsync(
                context,
                plan,
                read.Edges,
                resolution.DeferredEdges,
                (entityType, random) => rowValueGenerator.GenerateRow(entityType, random),
                rootRandom.Derive("Persistence"),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Works out what <see cref="AutoSeedAsync"/> would do for <paramref name="context"/> and
    /// <paramref name="scale"/>, without writing anything.
    /// </summary>
    /// <param name="context">The context to plan for.</param>
    /// <param name="seed">The seed the plan's cardinality draws derive from.</param>
    /// <param name="scale">The row count for entity types with no required principal.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The insertion order, row counts, resolved cycles and skipped entity types.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scale"/> is not positive.</exception>
    /// <exception cref="Exceptions.UnresolvableCycleException">
    /// The model contains a dependency cycle made entirely of required foreign keys.
    /// </exception>
    public static Task<AutoSeedExplainResult> AutoSeedExplainAsync(
        this DbContext context, long seed, int scale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        ModelReadResult read = new ModelReader().Read(context.Model);
        CycleResolution resolution = new CycleResolver().Resolve(read.EntityTypes, read.Edges);

        SeededRandom rootRandom = SeededRandom.FromRootSeed(seed);

        IReadOnlyList<EntityGenerationPlan> plan = new GenerationPlan()
            .Plan(resolution.Order, read.Edges, scale, rootRandom.Derive("GenerationPlan"));

        Dictionary<string, int> rowCounts = plan.ToDictionary(entry => entry.EntityType.Name, entry => entry.RowCount);
        Dictionary<string, IReadOnlyList<int>> childCounts = [];
        foreach (EntityGenerationPlan entry in plan)
        {
            if (entry.ChildCountsByDriverRow is { } counts)
            {
                childCounts[entry.EntityType.Name] = counts;
            }
        }

        return Task.FromResult(new AutoSeedExplainResult(resolution.Order, rowCounts, childCounts, read.SkippedEntityTypes, resolution.DeferredEdges));
    }

    /// <summary>
    /// Reads <paramref name="context"/>'s model and writes the smallest dataset that touches every
    /// enum value, every nullable property in both states, every string at its length boundaries,
    /// and every relationship at zero, one and many, instead of realistic values at scale.
    /// </summary>
    /// <param name="context">The context to seed.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number of rows inserted, keyed by entity type name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="Exceptions.UnresolvableCycleException">
    /// The model contains a dependency cycle made entirely of required foreign keys.
    /// </exception>
    /// <exception cref="Exceptions.UnsupportedEntityTypeException">
    /// An entity type has no public parameterless constructor, or a required principal has no generated rows.
    /// </exception>
    /// <exception cref="Exceptions.UnsatisfiableUniquenessException">
    /// A unique property ran out of deterministic candidates to resolve a collision.
    /// </exception>
    public static async Task<IReadOnlyDictionary<string, int>> AutoSeedCoverageAsync(
        this DbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        ModelReadResult read = new ModelReader().Read(context.Model);
        CycleResolution resolution = new CycleResolver().Resolve(read.EntityTypes, read.Edges);

        IReadOnlyList<EntityGenerationPlan> plan = new CoveragePlan().Plan(resolution.Order, read.Edges);

        CoverageValueGenerator coverageValueGenerator = new();

        return await new Persistence()
            .InsertAsync(
                context,
                plan,
                read.Edges,
                resolution.DeferredEdges,
                (entityType, random) => coverageValueGenerator.GenerateRow(entityType, random),
                SeededRandom.FromRootSeed(CoverageSeed),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static IReadOnlyList<IPropertyInferenceRule> BuildDefaultRules() =>
    [
        new QueryFilterInferenceRule(),
        new NameInferenceRule(),
        new EmailInferenceRule(),
        new DocumentInferenceRule(),
        new PostalCodeInferenceRule(),
        new PhoneInferenceRule(),
        new DecimalAmountInferenceRule(),
        new QuantityInferenceRule(),
        new UrlInferenceRule(),
        new SlugInferenceRule(),
        new IpAddressInferenceRule(),
        new CreatedAtInferenceRule(ReferenceNow),
        new UpdatedAtInferenceRule(ReferenceNow),
        new CorrelatedTotalInferenceRule(),
        new DeletedAtInferenceRule(ReferenceNow),
        new GenericTextInferenceRule(),
        new GenericNumberInferenceRule(),
        new GenericBooleanInferenceRule(),
        new GenericEnumInferenceRule(),
        new GenericGuidInferenceRule(),
    ];
}
