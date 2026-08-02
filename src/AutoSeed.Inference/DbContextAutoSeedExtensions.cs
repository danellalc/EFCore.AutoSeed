using EFCore.AutoSeed.Inference;
using EFCore.AutoSeed.Inference.Rules;
using EFCore.AutoSeed.Pipeline;
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

    private static IReadOnlyList<IPropertyInferenceRule> BuildDefaultRules() =>
    [
        new NameInferenceRule(),
        new EmailInferenceRule(),
        new DocumentInferenceRule(),
        new PostalCodeInferenceRule(),
        new PhoneInferenceRule(),
        new DecimalAmountInferenceRule(),
        new UrlInferenceRule(),
        new SlugInferenceRule(),
        new IpAddressInferenceRule(),
        new CreatedAtInferenceRule(ReferenceNow),
        new UpdatedAtInferenceRule(ReferenceNow),
        new DeletedAtInferenceRule(ReferenceNow),
    ];
}
