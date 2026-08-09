using EFCore.AutoSeed.Coverage;
using EFCore.AutoSeed.Exceptions;
using EFCore.AutoSeed.Inference;
using EFCore.AutoSeed.Inference.Rules;
using EFCore.AutoSeed.Pipeline;
using EFCore.AutoSeed.Providers;
using EFCore.AutoSeed.Shape;
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
    /// <param name="options">Tunes the built-in inference rules. Defaults to <see cref="AutoSeedOptions.Default"/>.</param>
    /// <param name="configure">
    /// Excludes an entity type, pins its row count, or seeds it with an exact set of rows instead of
    /// generated ones. The common case needs none of this.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number of rows inserted, keyed by entity type name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scale"/> is not positive.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="configure"/> configures more than one of <c>Exclude()</c>, <c>HasRowCount()</c>
    /// and <c>SeedWith(...)</c> for the same entity type, or configures both <c>Exclude()</c> or
    /// <c>SeedWith(...)</c> and <c>Property(...).GenerateWith(...)</c> for the same entity type.
    /// </exception>
    /// <exception cref="Exceptions.InvalidSeedConfigurationException">
    /// <paramref name="configure"/> configures a type that is not an entity type in <paramref name="context"/>'s
    /// model, or a custom generator for a property that is not mapped.
    /// </exception>
    /// <exception cref="Exceptions.UnsupportedSeedConfigurationException">
    /// <paramref name="configure"/> excludes, pins the row count of, or seeds with exact rows an
    /// entity type that has a required foreign key.
    /// </exception>
    /// <exception cref="Exceptions.UnresolvableCycleException">
    /// The model contains a dependency cycle made entirely of required foreign keys.
    /// </exception>
    /// <exception cref="Exceptions.UnsupportedEntityTypeException">
    /// An entity type has no public parameterless constructor, a required principal has no
    /// generated rows, or two entity types are table-split (mapped to the same table through a
    /// shared primary key).
    /// </exception>
    /// <exception cref="Exceptions.UnsatisfiableUniquenessException">
    /// A unique property ran out of deterministic candidates to resolve a collision.
    /// </exception>
    /// <exception cref="Exceptions.UnsupportedPropertyException">
    /// A required property has no database-generated value and no rule recognizes it.
    /// </exception>
    public static async Task<IReadOnlyDictionary<string, int>> AutoSeedAsync(
        this DbContext context,
        long seed,
        int scale,
        AutoSeedOptions? options = null,
        Action<SeedConfigurationBuilder>? configure = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        options ??= AutoSeedOptions.Default;

        ModelReadResult read = new ModelReader().Read(context.Model);
        TableSplittingGuard.EnsureNoTableSplitting(read.Edges);
        CycleResolution resolution = new CycleResolver().Resolve(read.EntityTypes, read.Edges);

        SeededRandom rootRandom = SeededRandom.FromRootSeed(seed);

        SeedConfiguration configuration = await ResolveConfigurationAsync(context, configure, read.Edges, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<EntityGenerationPlan> plan = new GenerationPlan()
            .Plan(resolution.Order, read.Edges, configuration.ScaleOverrides, scale, rootRandom.Derive("GenerationPlan"));

        RowValueGenerator rowValueGenerator = new(BuildDefaultRules(options), options.NullRate, options.DirtyData, configuration.CustomGenerators);
        rowValueGenerator.ValidateRequiredProperties(resolution.Order.Where(
            entityType => !configuration.ExistingRows.ContainsKey(entityType) && !configuration.ExactRows.ContainsKey(entityType)));

        return await new Persistence()
            .InsertAsync(
                context,
                plan,
                read.Edges,
                resolution.DeferredEdges,
                (entityType, random) => rowValueGenerator.GenerateRow(entityType, random),
                rootRandom.Derive("Persistence"),
                cancellationToken,
                configuration.ExistingRows,
                options.NullRate,
                configuration.ExactRows)
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
    /// <param name="options">Tunes the built-in inference rules. Defaults to <see cref="AutoSeedOptions.Default"/>.</param>
    /// <param name="configure">
    /// Excludes an entity type or pins its row count. <c>SeedWith(...)</c> is not supported here yet;
    /// use <see cref="AutoSeedAsync"/> for that. The common case needs none of this.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number of rows inserted, keyed by entity type name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scale"/> is not positive.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="configure"/> configures more than one of <c>Exclude()</c>, <c>HasRowCount()</c>
    /// and <c>SeedWith(...)</c> for the same entity type, or configures both <c>Exclude()</c> or
    /// <c>SeedWith(...)</c> and <c>Property(...).GenerateWith(...)</c> for the same entity type.
    /// </exception>
    /// <exception cref="Exceptions.InvalidSeedConfigurationException">
    /// <paramref name="configure"/> configures a type that is not an entity type in <paramref name="context"/>'s
    /// model, or a custom generator for a property that is not mapped.
    /// </exception>
    /// <exception cref="Exceptions.UnsupportedSeedConfigurationException">
    /// <paramref name="configure"/> excludes, pins the row count of, or seeds with exact rows an
    /// entity type that has a required foreign key.
    /// </exception>
    /// <exception cref="Exceptions.UnresolvableCycleException">
    /// The model contains a dependency cycle made entirely of required foreign keys.
    /// </exception>
    /// <exception cref="Exceptions.UnsupportedProviderException">
    /// <paramref name="context"/>'s configured EF Core provider is neither SQL Server nor PostgreSQL.
    /// </exception>
    /// <exception cref="Exceptions.UnsupportedEntityTypeException">
    /// The model needs a cycle-breaking second pass, declares an inherited entity type, a
    /// single-column identity primary key of a type other than int, long or Guid, has no public
    /// parameterless constructor, a required principal has no generated rows, two entity types
    /// are table-split (mapped to the same table through a shared primary key), or
    /// <paramref name="configure"/> uses <c>SeedWith(...)</c>, not supported here yet.
    /// </exception>
    /// <exception cref="Exceptions.UnsatisfiableUniquenessException">
    /// A unique property ran out of deterministic candidates to resolve a collision.
    /// </exception>
    /// <exception cref="Exceptions.UnsupportedPropertyException">
    /// A required property has no database-generated value and no rule recognizes it.
    /// </exception>
    public static async Task<IReadOnlyDictionary<string, int>> AutoSeedFastAsync(
        this DbContext context,
        long seed,
        int scale,
        AutoSeedOptions? options = null,
        Action<SeedConfigurationBuilder>? configure = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        options ??= AutoSeedOptions.Default;

        ModelReadResult read = new ModelReader().Read(context.Model);
        TableSplittingGuard.EnsureNoTableSplitting(read.Edges);
        CycleResolution resolution = new CycleResolver().Resolve(read.EntityTypes, read.Edges);

        SeededRandom rootRandom = SeededRandom.FromRootSeed(seed);

        SeedConfiguration configuration = await ResolveConfigurationAsync(context, configure, read.Edges, cancellationToken).ConfigureAwait(false);
        if (configuration.ExactRows.Count > 0)
        {
            throw new UnsupportedEntityTypeException(
                configuration.ExactRows.Keys.First().Name, "SeedWith is not supported by AutoSeedFastAsync yet; use AutoSeedAsync instead");
        }

        IReadOnlyList<EntityGenerationPlan> plan = new GenerationPlan()
            .Plan(resolution.Order, read.Edges, configuration.ScaleOverrides, scale, rootRandom.Derive("GenerationPlan"));

        RowValueGenerator rowValueGenerator = new(BuildDefaultRules(options), options.NullRate, options.DirtyData, configuration.CustomGenerators);
        rowValueGenerator.ValidateRequiredProperties(resolution.Order.Where(entityType => !configuration.ExistingRows.ContainsKey(entityType)));
        IBulkInsertProvider provider = BulkInsertProviderFactory.Create(context);

        return await new BulkPersistence(provider)
            .InsertAsync(
                context,
                plan,
                read.Edges,
                resolution.DeferredEdges,
                (entityType, random) => rowValueGenerator.GenerateRow(entityType, random),
                rootRandom.Derive("Persistence"),
                cancellationToken,
                configuration.ExistingRows,
                options.NullRate)
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
    /// <exception cref="Exceptions.UnsupportedEntityTypeException">
    /// Two entity types are table-split (mapped to the same table through a shared primary key).
    /// </exception>
    public static Task<AutoSeedExplainResult> AutoSeedExplainAsync(
        this DbContext context, long seed, int scale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        ModelReadResult read = new ModelReader().Read(context.Model);
        TableSplittingGuard.EnsureNoTableSplitting(read.Edges);
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
    /// An entity type has no public parameterless constructor, a required principal has no
    /// generated rows, or two entity types are table-split (mapped to the same table through a
    /// shared primary key).
    /// </exception>
    /// <exception cref="Exceptions.UnsatisfiableUniquenessException">
    /// A unique property ran out of deterministic candidates to resolve a collision.
    /// </exception>
    /// <exception cref="Exceptions.UnsupportedPropertyException">A required complex property is present.</exception>
    public static async Task<IReadOnlyDictionary<string, int>> AutoSeedCoverageAsync(
        this DbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        ModelReadResult read = new ModelReader().Read(context.Model);
        TableSplittingGuard.EnsureNoTableSplitting(read.Edges);
        CycleResolution resolution = new CycleResolver().Resolve(read.EntityTypes, read.Edges);
        CoverageValueGenerator.ValidateRequiredProperties(resolution.Order);

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

    /// <summary>
    /// Reads <paramref name="context"/>'s row counts straight from the database engine's own
    /// maintained statistics (<c>sys.dm_db_partition_stats</c> on SQL Server, <c>pg_class.reltuples</c>
    /// on PostgreSQL): never a query against an actual table. Feed the result into
    /// <see cref="AutoSeedFromShapeAsync"/>, save it to disk with the <c>EFCore.AutoSeed.Shape</c>
    /// package's <c>ShapeFile.WriteAsync</c>, or inspect it directly.
    /// </summary>
    /// <param name="context">The context to capture from, typically pointed at a production replica.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The captured row count for every seedable entity type in the model.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="Exceptions.UnsupportedProviderException">
    /// <paramref name="context"/>'s configured EF Core provider is neither SQL Server nor PostgreSQL.
    /// </exception>
    public static Task<ShapeCapture> CaptureShapeAsync(this DbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        IShapeCaptureProvider provider = ShapeCaptureProviderFactory.Create(context);
        return new ShapeCapturer(provider).CaptureAsync(context, cancellationToken);
    }

    /// <summary>
    /// Like <see cref="AutoSeedAsync"/>, but an entity type present in <paramref name="shape"/> uses
    /// a scale proportional to its captured row count relative to the largest captured entity type,
    /// instead of <paramref name="scale"/> directly, so the seeded database's relative table sizes
    /// resemble where <paramref name="shape"/> was captured from. Only shipped for fidelity mode; no
    /// <c>AutoSeedFastAsync</c> equivalent yet.
    /// </summary>
    /// <param name="context">The context to seed.</param>
    /// <param name="seed">The seed every generated value derives from. The same seed always produces the same data.</param>
    /// <param name="shape">A capture from <see cref="CaptureShapeAsync"/>, or read from disk with <c>ShapeFile.ReadAsync</c>.</param>
    /// <param name="scale">
    /// The row count for the largest entity type <paramref name="shape"/> captured a row count for,
    /// and for any entity type with no required principal that <paramref name="shape"/> did not capture.
    /// </param>
    /// <param name="options">Tunes the built-in inference rules. Defaults to <see cref="AutoSeedOptions.Default"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number of rows inserted, keyed by entity type name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="shape"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scale"/> is not positive.</exception>
    /// <exception cref="Exceptions.UnresolvableCycleException">
    /// The model contains a dependency cycle made entirely of required foreign keys.
    /// </exception>
    /// <exception cref="Exceptions.UnsupportedEntityTypeException">
    /// An entity type has no public parameterless constructor, a required principal has no
    /// generated rows, or two entity types are table-split (mapped to the same table through a
    /// shared primary key).
    /// </exception>
    /// <exception cref="Exceptions.UnsatisfiableUniquenessException">
    /// A unique property ran out of deterministic candidates to resolve a collision.
    /// </exception>
    /// <exception cref="Exceptions.UnsupportedPropertyException">
    /// A required property has no database-generated value and no rule recognizes it.
    /// </exception>
    public static async Task<IReadOnlyDictionary<string, int>> AutoSeedFromShapeAsync(
        this DbContext context, long seed, ShapeCapture shape, int scale, AutoSeedOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(shape);
        options ??= AutoSeedOptions.Default;
        if (scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "Must be positive.");
        }

        ModelReadResult read = new ModelReader().Read(context.Model);
        TableSplittingGuard.EnsureNoTableSplitting(read.Edges);
        CycleResolution resolution = new CycleResolver().Resolve(read.EntityTypes, read.Edges);

        SeededRandom rootRandom = SeededRandom.FromRootSeed(seed);

        Dictionary<string, long> capturedRowCounts = shape.Tables.ToDictionary(table => table.EntityTypeName, table => table.RowCount);
        long maxCapturedRowCount = capturedRowCounts.Values.DefaultIfEmpty(0).Max();

        Dictionary<IEntityType, int> scaleByEntityType = [];
        if (maxCapturedRowCount > 0)
        {
            foreach (IEntityType entityType in read.EntityTypes)
            {
                if (capturedRowCounts.TryGetValue(entityType.Name, out long rowCount) && rowCount > 0)
                {
                    scaleByEntityType[entityType] = Math.Max(1, (int)Math.Round(scale * (double)rowCount / maxCapturedRowCount));
                }
            }
        }

        IReadOnlyList<EntityGenerationPlan> plan = new GenerationPlan()
            .Plan(resolution.Order, read.Edges, scaleByEntityType, scale, rootRandom.Derive("GenerationPlan"));

        RowValueGenerator rowValueGenerator = new(BuildDefaultRules(options), options.NullRate, options.DirtyData);
        rowValueGenerator.ValidateRequiredProperties(resolution.Order);

        return await new Persistence()
            .InsertAsync(
                context,
                plan,
                read.Edges,
                resolution.DeferredEdges,
                (entityType, random) => rowValueGenerator.GenerateRow(entityType, random),
                rootRandom.Derive("Persistence"),
                cancellationToken,
                existingRowsByEntityType: null,
                nullRate: options.NullRate)
            .ConfigureAwait(false);
    }

    private static IReadOnlyList<IPropertyInferenceRule> BuildDefaultRules(AutoSeedOptions options) =>
    [
        new QueryFilterInferenceRule(options.QueryFilterPassRate),
        new NameInferenceRule(options.Locale),
        new EmailInferenceRule(options.Locale),
        new DocumentInferenceRule(),
        new PostalCodeInferenceRule(options.Locale),
        new PhoneInferenceRule(options.Locale),
        new DecimalAmountInferenceRule(),
        new QuantityInferenceRule(),
        new DiscountInferenceRule(),
        new UrlInferenceRule(),
        new SlugInferenceRule(),
        new IpAddressInferenceRule(),
        new CreatedAtInferenceRule(ReferenceNow, temporalOptions: options.TemporalClustering),
        new UpdatedAtInferenceRule(ReferenceNow, temporalOptions: options.TemporalClustering),
        new CorrelatedTotalInferenceRule(),
        new AmountDueInferenceRule(),
        new DeletedAtInferenceRule(ReferenceNow, temporalOptions: options.TemporalClustering),
        new GenericTextInferenceRule(),
        new GenericNumberInferenceRule(),
        new GenericBooleanInferenceRule(),
        new GenericEnumInferenceRule(),
        new GenericGuidInferenceRule(),
        new GenericDateTimeInferenceRule(ReferenceNow, temporalOptions: options.TemporalClustering),
        new GenericDateOnlyInferenceRule(ReferenceNow, temporalOptions: options.TemporalClustering),
        new GenericTimeOnlyInferenceRule(),
        new GenericTimeSpanInferenceRule(),
        new GenericByteArrayInferenceRule(),
    ];

    private static async Task<SeedConfiguration> ResolveConfigurationAsync(
        DbContext context, Action<SeedConfigurationBuilder>? configure, IReadOnlyList<GraphEdge> edges, CancellationToken cancellationToken)
    {
        if (configure is null)
        {
            return new SeedConfiguration(
                new Dictionary<IEntityType, int>(),
                new Dictionary<IEntityType, IReadOnlyList<object>>(),
                new Dictionary<IEntityType, IReadOnlyList<object>>(),
                new Dictionary<(IEntityType, string), Func<SeededRandom, IReadOnlyDictionary<string, object>, object?>>());
        }

        SeedConfigurationBuilder builder = new();
        configure(builder);

        foreach (Type excludedEntityType in builder.ExcludedEntityReaders.Keys)
        {
            if (builder.RowCountOverrides.ContainsKey(excludedEntityType))
            {
                throw new ArgumentException(
                    $"'{excludedEntityType.Name}' is configured with both Exclude() and HasRowCount(): Exclude() " +
                    "reads its actual row count from the database, so a pinned row count can never apply. Remove one of the two calls.",
                    "configure");
            }

            if (builder.ExactRows.ContainsKey(excludedEntityType))
            {
                throw new ArgumentException(
                    $"'{excludedEntityType.Name}' is configured with both Exclude() and SeedWith(): Exclude() reads its " +
                    "actual rows from the database, so exact caller-supplied rows would never be inserted. Remove one of the two calls.",
                    "configure");
            }
        }

        foreach (Type exactRowsEntityType in builder.ExactRows.Keys)
        {
            if (builder.RowCountOverrides.ContainsKey(exactRowsEntityType))
            {
                throw new ArgumentException(
                    $"'{exactRowsEntityType.Name}' is configured with both SeedWith() and HasRowCount(): SeedWith() " +
                    "already determines its row count from the rows given. Remove one of the two calls.",
                    "configure");
            }
        }

        foreach ((Type customGeneratorEntityType, string propertyName) in builder.CustomGenerators.Keys)
        {
            if (builder.ExcludedEntityReaders.ContainsKey(customGeneratorEntityType))
            {
                throw new ArgumentException(
                    $"'{customGeneratorEntityType.Name}' is configured with both Exclude() and GenerateWith() on '{propertyName}': " +
                    "Exclude() reads its existing rows from the database instead of generating any, so the custom generator would " +
                    "never run. Remove one of the two calls.",
                    "configure");
            }

            if (builder.ExactRows.ContainsKey(customGeneratorEntityType))
            {
                throw new ArgumentException(
                    $"'{customGeneratorEntityType.Name}' is configured with both SeedWith() and GenerateWith() on '{propertyName}': " +
                    "SeedWith() rows are inserted exactly as given instead of generating any, so the custom generator would " +
                    "never run. Remove one of the two calls.",
                    "configure");
            }
        }

        ILookup<IEntityType, GraphEdge> requiredEdgesByDependent = edges
            .Where(edge => edge.ForeignKey.IsRequired)
            .ToLookup(edge => edge.Dependent);

        Dictionary<IEntityType, int> scaleOverrides = [];
        foreach (KeyValuePair<Type, int> entry in builder.RowCountOverrides)
        {
            IEntityType entityType = ResolveEntityType(context.Model, entry.Key);
            EnsureConfigurableEntityType(entityType, requiredEdgesByDependent);
            scaleOverrides[entityType] = entry.Value;
        }

        Dictionary<IEntityType, IReadOnlyList<object>> existingRows = [];
        foreach (KeyValuePair<Type, Func<DbContext, CancellationToken, Task<IReadOnlyList<object>>>> entry in builder.ExcludedEntityReaders)
        {
            IEntityType entityType = ResolveEntityType(context.Model, entry.Key);
            EnsureConfigurableEntityType(entityType, requiredEdgesByDependent);
            IReadOnlyList<object> rows = await entry.Value(context, cancellationToken).ConfigureAwait(false);
            existingRows[entityType] = rows;
            scaleOverrides[entityType] = rows.Count;
        }

        Dictionary<IEntityType, IReadOnlyList<object>> exactRows = [];
        foreach (KeyValuePair<Type, IReadOnlyList<object>> entry in builder.ExactRows)
        {
            IEntityType entityType = ResolveEntityType(context.Model, entry.Key);
            EnsureConfigurableEntityType(entityType, requiredEdgesByDependent);
            exactRows[entityType] = entry.Value;
            scaleOverrides[entityType] = entry.Value.Count;
        }

        Dictionary<(IEntityType, string), Func<SeededRandom, IReadOnlyDictionary<string, object>, object?>> customGenerators = [];
        foreach (KeyValuePair<(Type EntityType, string PropertyName), Func<SeededRandom, IReadOnlyDictionary<string, object>, object?>> entry
            in builder.CustomGenerators)
        {
            IEntityType entityType = ResolveEntityType(context.Model, entry.Key.EntityType);
            if (entityType.FindProperty(entry.Key.PropertyName) is null)
            {
                throw new InvalidSeedConfigurationException(entry.Key.EntityType.Name, entry.Key.PropertyName);
            }

            customGenerators[(entityType, entry.Key.PropertyName)] = entry.Value;
        }

        return new SeedConfiguration(scaleOverrides, existingRows, exactRows, customGenerators);
    }

    private static IEntityType ResolveEntityType(IModel model, Type clrType) =>
        model.FindEntityType(clrType)
            ?? throw new InvalidSeedConfigurationException(clrType.Name);

    private static void EnsureConfigurableEntityType(IEntityType entityType, ILookup<IEntityType, GraphEdge> requiredEdgesByDependent)
    {
        GraphEdge? requiredEdge = requiredEdgesByDependent[entityType].FirstOrDefault();
        if (requiredEdge is not null)
        {
            throw new UnsupportedSeedConfigurationException(entityType.Name, requiredEdge.Principal.Name);
        }
    }

    private sealed record SeedConfiguration(
        IReadOnlyDictionary<IEntityType, int> ScaleOverrides,
        IReadOnlyDictionary<IEntityType, IReadOnlyList<object>> ExistingRows,
        IReadOnlyDictionary<IEntityType, IReadOnlyList<object>> ExactRows,
        IReadOnlyDictionary<(IEntityType, string), Func<SeededRandom, IReadOnlyDictionary<string, object>, object?>> CustomGenerators);
}
