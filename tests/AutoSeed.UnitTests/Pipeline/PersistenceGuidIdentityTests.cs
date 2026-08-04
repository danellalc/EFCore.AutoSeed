using EFCore.AutoSeed.Pipeline;
using EFCore.AutoSeed.Providers;
using EFCore.AutoSeed.UnitTests.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.UnitTests.Pipeline;

public sealed class PersistenceGuidIdentityTests
{
    [Fact]
    public async Task InsertAsync_WithAnUnconfiguredGuidPrimaryKey_NeverLeavesItAtGuidEmpty()
    {
        List<Guid> ids = await RunGuidIdentityPipelineAsync(UniqueDatabaseName(), seed: 42);

        Assert.NotEmpty(ids);
        Assert.All(ids, id => Assert.NotEqual(Guid.Empty, id));
    }

    [Fact]
    public async Task InsertAsync_WithTheSameSeedTwice_ProducesIdenticalGuidPrimaryKeys()
    {
        List<Guid> firstIds = await RunGuidIdentityPipelineAsync(UniqueDatabaseName(), seed: 42);
        List<Guid> secondIds = await RunGuidIdentityPipelineAsync(UniqueDatabaseName(), seed: 42);

        Assert.NotEmpty(firstIds);
        Assert.Equal(firstIds.Count, firstIds.Distinct().Count());
        Assert.Equal(firstIds, secondIds);
    }

    [Fact]
    public async Task InsertAsync_WithDifferentSeeds_ProducesDifferentGuidPrimaryKeys()
    {
        List<Guid> firstIds = await RunGuidIdentityPipelineAsync(UniqueDatabaseName(), seed: 42);
        List<Guid> secondIds = await RunGuidIdentityPipelineAsync(UniqueDatabaseName(), seed: 99);

        Assert.NotEqual(firstIds, secondIds);
    }

    [Fact]
    public async Task InsertAsync_AndBulkPersistence_ProduceIdenticalGuidPrimaryKeysForTheSameSeed()
    {
        using IsolatedGuidIdentityContext fidelityContext = new(UniqueDatabaseName());
        ModelReadResult fidelityRead = new ModelReader().Read(fidelityContext.Model);
        CycleResolution fidelityResolution = new CycleResolver().Resolve(fidelityRead.EntityTypes, fidelityRead.Edges);
        IReadOnlyList<EntityGenerationPlan> fidelityPlan =
            new GenerationPlan().Plan(fidelityResolution.Order, fidelityRead.Edges, scale: 15, SeededRandom.FromRootSeed(5));

        await new Persistence().InsertAsync(
            fidelityContext, fidelityPlan, fidelityRead.Edges, fidelityResolution.DeferredEdges,
            GenerateRow, SeededRandom.FromRootSeed(5), CancellationToken.None);

        using IsolatedGuidIdentityContext fastContext = new(UniqueDatabaseName());
        ModelReadResult fastRead = new ModelReader().Read(fastContext.Model);
        CycleResolution fastResolution = new CycleResolver().Resolve(fastRead.EntityTypes, fastRead.Edges);
        IReadOnlyList<EntityGenerationPlan> fastPlan =
            new GenerationPlan().Plan(fastResolution.Order, fastRead.Edges, scale: 15, SeededRandom.FromRootSeed(5));

        FakeBulkInsertProvider provider = new();
        await new BulkPersistence(provider).InsertAsync(
            fastContext, fastPlan, fastRead.Edges, fastResolution.DeferredEdges,
            GenerateRow, SeededRandom.FromRootSeed(5), CancellationToken.None);

        List<Guid> fidelityIds = [.. (await fidelityContext.Widgets.ToListAsync()).Select(widget => widget.Id).OrderBy(id => id)];
        List<Guid> fastIds = [.. provider.Inserted("Widget").Select(row => (Guid)row["Id"]).OrderBy(id => id)];

        Assert.NotEmpty(fidelityIds);
        Assert.Equal(fidelityIds, fastIds);
    }

    private static async Task<List<Guid>> RunGuidIdentityPipelineAsync(string databaseName, long seed)
    {
        using IsolatedGuidIdentityContext context = new(databaseName);
        ModelReadResult read = new ModelReader().Read(context.Model);
        CycleResolution resolution = new CycleResolver().Resolve(read.EntityTypes, read.Edges);
        IReadOnlyList<EntityGenerationPlan> plan = new GenerationPlan().Plan(resolution.Order, read.Edges, scale: 25, SeededRandom.FromRootSeed(seed));

        await new Persistence().InsertAsync(
            context, plan, read.Edges, resolution.DeferredEdges, GenerateRow, SeededRandom.FromRootSeed(seed), CancellationToken.None);

        List<Widget> widgets = await context.Widgets.ToListAsync();
        return [.. widgets.Select(widget => widget.Id).OrderBy(id => id)];
    }

    private static Dictionary<string, object> GenerateRow(IEntityType entityType, SeededRandom random) => [];

    private static string UniqueDatabaseName() => Guid.NewGuid().ToString("N");

    private sealed class IsolatedGuidIdentityContext(string databaseName) : DbContext
    {
        public DbSet<Widget> Widgets => Set<Widget>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(databaseName);
    }

    private sealed class Widget
    {
        public Guid Id { get; set; }
    }
}
