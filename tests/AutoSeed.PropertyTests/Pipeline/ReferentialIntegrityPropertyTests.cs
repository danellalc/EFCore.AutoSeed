using EFCore.AutoSeed.Pipeline;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.PropertyTests.Pipeline;

public sealed class ReferentialIntegrityPropertyTests
{
    [Property(MaxTest = 50)]
    public async Task<bool> LinearRequiredChain_EveryForeignKeyPointsToAnExistingRow(long seed, int rawScale)
    {
        using LinearRequiredChainContext context = new(UniqueDatabaseName());
        return await SeedAndVerifyAsync(context, seed, BoundScale(rawScale)).ConfigureAwait(false);
    }

    [Property(MaxTest = 50)]
    public async Task<bool> DiamondRequiredPrincipals_EveryForeignKeyPointsToAnExistingRow(long seed, int rawScale)
    {
        using DiamondRequiredContext context = new(UniqueDatabaseName());
        return await SeedAndVerifyAsync(context, seed, BoundScale(rawScale)).ConfigureAwait(false);
    }

    [Property(MaxTest = 50)]
    public async Task<bool> NullableSelfReference_EveryForeignKeyPointsToAnExistingRow(long seed, int rawScale)
    {
        using NullableSelfReferenceContext context = new(UniqueDatabaseName());
        bool referentialIntegrityHolds = await SeedAndVerifyAsync(context, seed, BoundScale(rawScale)).ConfigureAwait(false);

        List<OrgNode> nodes = await context.Nodes.ToListAsync().ConfigureAwait(false);
        bool noNodeIsItsOwnParentUnlessOnlyOneExists =
            nodes.Count <= 1 || nodes.All(node => node.ParentId is null || node.ParentId != node.Id);

        return referentialIntegrityHolds && noNodeIsItsOwnParentUnlessOnlyOneExists;
    }

    private static async Task<bool> SeedAndVerifyAsync(DbContext context, long seed, int scale)
    {
        ModelReadResult read = new ModelReader().Read(context.Model);
        CycleResolution resolution = new CycleResolver().Resolve(read.EntityTypes, read.Edges);
        SeededRandom rootRandom = SeededRandom.FromRootSeed(seed);

        IReadOnlyList<EntityGenerationPlan> plan = new GenerationPlan()
            .Plan(resolution.Order, read.Edges, scale, rootRandom.Derive("GenerationPlan"));

        await new Persistence()
            .InsertAsync(
                context,
                plan,
                read.Edges,
                resolution.DeferredEdges,
                GenerateEmptyRow,
                rootRandom.Derive("Persistence"),
                CancellationToken.None)
            .ConfigureAwait(false);

        return EveryForeignKeyPointsToAnExistingRow(context, read.Edges);
    }

    private static bool EveryForeignKeyPointsToAnExistingRow(DbContext context, IReadOnlyList<GraphEdge> edges)
    {
        ILookup<IEntityType, EntityEntry> entriesByEntityType = context.ChangeTracker.Entries().ToLookup(entry => entry.Metadata);

        foreach (GraphEdge edge in edges)
        {
            HashSet<string> principalKeys = [.. entriesByEntityType[edge.Principal]
                .Select(entry => FormatKey(entry, edge.ForeignKey.PrincipalKey.Properties))];

            foreach (EntityEntry dependent in entriesByEntityType[edge.Dependent])
            {
                IReadOnlyList<object?> foreignKeyValues = [.. edge.ForeignKey.Properties
                    .Select(property => dependent.Property(property.Name).CurrentValue)];

                if (foreignKeyValues.Any(value => value is null))
                {
                    if (edge.ForeignKey.IsRequired)
                    {
                        return false;
                    }

                    continue;
                }

                if (!principalKeys.Contains(FormatKey(foreignKeyValues)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static Dictionary<string, object> GenerateEmptyRow(IEntityType entityType, SeededRandom random) => [];

    private static string FormatKey(EntityEntry entry, IReadOnlyList<IProperty> properties) =>
        FormatKey([.. properties.Select(property => entry.Property(property.Name).CurrentValue)]);

    private static string FormatKey(IReadOnlyList<object?> values) =>
        string.Join('\u001f', values.Select(value => value?.ToString() ?? "\u0000"));

    private static int BoundScale(int rawScale) => (Math.Abs(rawScale) % 8) + 5;

    private static string UniqueDatabaseName() => Guid.NewGuid().ToString("N");

    private sealed class LinearRequiredChainContext(string databaseName) : DbContext
    {
        public DbSet<ChainRoot> Roots => Set<ChainRoot>();
        public DbSet<ChainMiddle> Middles => Set<ChainMiddle>();
        public DbSet<ChainLeaf> Leaves => Set<ChainLeaf>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(databaseName);
    }

    private sealed class ChainRoot
    {
        public int Id { get; set; }
    }

    private sealed class ChainMiddle
    {
        public int Id { get; set; }
        public int ChainRootId { get; set; }
        public ChainRoot ChainRoot { get; set; } = null!;
    }

    private sealed class ChainLeaf
    {
        public int Id { get; set; }
        public int ChainMiddleId { get; set; }
        public ChainMiddle ChainMiddle { get; set; } = null!;
    }

    private sealed class DiamondRequiredContext(string databaseName) : DbContext
    {
        public DbSet<DiamondRoot> Roots => Set<DiamondRoot>();
        public DbSet<DiamondLeft> Lefts => Set<DiamondLeft>();
        public DbSet<DiamondRight> Rights => Set<DiamondRight>();
        public DbSet<DiamondMerge> Merges => Set<DiamondMerge>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(databaseName);
    }

    private sealed class DiamondRoot
    {
        public int Id { get; set; }
    }

    private sealed class DiamondLeft
    {
        public int Id { get; set; }
        public int DiamondRootId { get; set; }
        public DiamondRoot DiamondRoot { get; set; } = null!;
    }

    private sealed class DiamondRight
    {
        public int Id { get; set; }
        public int DiamondRootId { get; set; }
        public DiamondRoot DiamondRoot { get; set; } = null!;
    }

    private sealed class DiamondMerge
    {
        public int Id { get; set; }
        public int DiamondLeftId { get; set; }
        public DiamondLeft DiamondLeft { get; set; } = null!;
        public int DiamondRightId { get; set; }
        public DiamondRight DiamondRight { get; set; } = null!;
    }

    private sealed class NullableSelfReferenceContext(string databaseName) : DbContext
    {
        public DbSet<OrgNode> Nodes => Set<OrgNode>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(databaseName);
    }

    private sealed class OrgNode
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public OrgNode? Parent { get; set; }
    }
}
