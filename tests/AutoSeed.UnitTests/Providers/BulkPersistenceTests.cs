using EFCore.AutoSeed.Exceptions;
using EFCore.AutoSeed.Pipeline;
using EFCore.AutoSeed.Providers;
using EFCore.AutoSeed.UnitTests.Pipeline.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.UnitTests.Providers;

public sealed class BulkPersistenceTests
{
    [Fact]
    public async Task InsertAsync_WithLinearChain_AssignsSequentialIdentityKeysAndWiresForeignKeys()
    {
        using LinearChainContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);
        CycleResolution resolution = new CycleResolver().Resolve(read.EntityTypes, read.Edges);
        IReadOnlyList<EntityGenerationPlan> plan = new GenerationPlan().Plan(resolution.Order, read.Edges, scale: 5, SeededRandom.FromRootSeed(42));

        FakeBulkInsertProvider provider = new();
        IReadOnlyDictionary<string, int> result = await new BulkPersistence(provider).InsertAsync(
            context, plan, read.Edges, resolution.DeferredEdges, GenerateRow, SeededRandom.FromRootSeed(42), CancellationToken.None);

        IReadOnlyList<IReadOnlyDictionary<string, object>> customers = provider.Inserted("Customer");
        IReadOnlyList<IReadOnlyDictionary<string, object>> orders = provider.Inserted("Order");

        Assert.Equal(ResultValue(result, "Customer"), customers.Count);
        Assert.Equal([.. Enumerable.Range(1, customers.Count)], customers.Select(row => (int)row["Id"]));

        HashSet<int> customerIds = [.. customers.Select(row => (int)row["Id"])];
        Assert.NotEmpty(orders);
        Assert.All(orders, order => Assert.Contains((int)order["CustomerId"], customerIds));

        List<int> orderIds = [.. orders.Select(row => (int)row["Id"])];
        Assert.Equal([.. Enumerable.Range(1, orderIds.Count)], orderIds);
    }

    [Fact]
    public async Task InsertAsync_WithSharedPrimaryKey_CopiesThePrincipalsKeyIntoTheDependent()
    {
        using SharedPrimaryKeyContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);
        CycleResolution resolution = new CycleResolver().Resolve(read.EntityTypes, read.Edges);
        IReadOnlyList<EntityGenerationPlan> plan = new GenerationPlan().Plan(resolution.Order, read.Edges, scale: 10, SeededRandom.FromRootSeed(7));

        FakeBulkInsertProvider provider = new();
        await new BulkPersistence(provider).InsertAsync(
            context, plan, read.Edges, resolution.DeferredEdges, GenerateRow, SeededRandom.FromRootSeed(7), CancellationToken.None);

        IReadOnlyList<IReadOnlyDictionary<string, object>> instructors = provider.Inserted("Instructor");
        IReadOnlyList<IReadOnlyDictionary<string, object>> officeAssignments = provider.Inserted("OfficeAssignment");

        Assert.Equal(instructors.Count, officeAssignments.Count);
        HashSet<int> instructorIds = [.. instructors.Select(row => (int)row["Id"])];
        Assert.All(officeAssignments, office => Assert.Contains((int)office["InstructorId"], instructorIds));
    }

    [Fact]
    public async Task InsertAsync_WithAnOwnedType_ThrowsUnsupportedEntityTypeException()
    {
        using KeylessAndOwnedContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);
        CycleResolution resolution = new CycleResolver().Resolve(read.EntityTypes, read.Edges);
        IReadOnlyList<EntityGenerationPlan> plan = new GenerationPlan().Plan(resolution.Order, read.Edges, scale: 3, SeededRandom.FromRootSeed(1));

        FakeBulkInsertProvider provider = new();
        UnsupportedEntityTypeException exception = await Assert.ThrowsAsync<UnsupportedEntityTypeException>(() => new BulkPersistence(provider).InsertAsync(
            context, plan, read.Edges, resolution.DeferredEdges, GenerateRow, SeededRandom.FromRootSeed(1), CancellationToken.None));

        Assert.Contains("Invoice", exception.EntityTypeName);
        Assert.False(provider.AnyInsertsHappened);
    }

    [Fact]
    public async Task InsertAsync_WithADeferredCycle_ThrowsUnsupportedEntityTypeException()
    {
        using NullableSelfCycleContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);
        CycleResolution resolution = new CycleResolver().Resolve(read.EntityTypes, read.Edges);
        Assert.Single(resolution.DeferredEdges);
        IReadOnlyList<EntityGenerationPlan> plan = new GenerationPlan().Plan(resolution.Order, read.Edges, scale: 3, SeededRandom.FromRootSeed(1));

        FakeBulkInsertProvider provider = new();
        await Assert.ThrowsAsync<UnsupportedEntityTypeException>(() => new BulkPersistence(provider).InsertAsync(
            context, plan, read.Edges, resolution.DeferredEdges, GenerateRow, SeededRandom.FromRootSeed(1), CancellationToken.None));

        Assert.False(provider.AnyInsertsHappened);
    }

    [Fact]
    public async Task InsertAsync_WithAnInheritedEntityType_ThrowsUnsupportedEntityTypeException()
    {
        using TphContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);
        CycleResolution resolution = new CycleResolver().Resolve(read.EntityTypes, read.Edges);
        IReadOnlyList<EntityGenerationPlan> plan = new GenerationPlan().Plan(resolution.Order, read.Edges, scale: 3, SeededRandom.FromRootSeed(1));

        FakeBulkInsertProvider provider = new();
        await Assert.ThrowsAsync<UnsupportedEntityTypeException>(() => new BulkPersistence(provider).InsertAsync(
            context, plan, read.Edges, resolution.DeferredEdges, GenerateRow, SeededRandom.FromRootSeed(1), CancellationToken.None));

        Assert.False(provider.AnyInsertsHappened);
    }

    [Fact]
    public async Task InsertAsync_WithANonIntIdentityPrimaryKey_ThrowsUnsupportedEntityTypeException()
    {
        using GuidKeyedContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);
        CycleResolution resolution = new CycleResolver().Resolve(read.EntityTypes, read.Edges);
        IReadOnlyList<EntityGenerationPlan> plan = new GenerationPlan().Plan(resolution.Order, read.Edges, scale: 3, SeededRandom.FromRootSeed(1));

        FakeBulkInsertProvider provider = new();
        UnsupportedEntityTypeException exception = await Assert.ThrowsAsync<UnsupportedEntityTypeException>(() => new BulkPersistence(provider).InsertAsync(
            context, plan, read.Edges, resolution.DeferredEdges, GenerateRow, SeededRandom.FromRootSeed(1), CancellationToken.None));

        Assert.Contains("Widget", exception.EntityTypeName);
        Assert.False(provider.AnyInsertsHappened);
    }

    [Fact]
    public async Task InsertAsync_WithNullArguments_ThrowsArgumentNullException()
    {
        using LinearChainContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);
        CycleResolution resolution = new CycleResolver().Resolve(read.EntityTypes, read.Edges);
        IReadOnlyList<EntityGenerationPlan> plan = new GenerationPlan().Plan(resolution.Order, read.Edges, scale: 1, SeededRandom.FromRootSeed(1));
        SeededRandom random = SeededRandom.FromRootSeed(1);
        BulkPersistence persistence = new(new FakeBulkInsertProvider());

        Assert.Throws<ArgumentNullException>(() => new BulkPersistence(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => persistence.InsertAsync(
            null!, plan, read.Edges, resolution.DeferredEdges, GenerateRow, random, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => persistence.InsertAsync(
            context, null!, read.Edges, resolution.DeferredEdges, GenerateRow, random, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => persistence.InsertAsync(
            context, plan, null!, resolution.DeferredEdges, GenerateRow, random, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => persistence.InsertAsync(
            context, plan, read.Edges, null!, GenerateRow, random, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => persistence.InsertAsync(
            context, plan, read.Edges, resolution.DeferredEdges, null!, random, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => persistence.InsertAsync(
            context, plan, read.Edges, resolution.DeferredEdges, GenerateRow, null!, CancellationToken.None));
    }

    private static Dictionary<string, object> GenerateRow(IEntityType entityType, SeededRandom random) => [];

    private static int ResultValue(IReadOnlyDictionary<string, int> result, string shortName) =>
        Assert.Single(result, entry => entry.Key.EndsWith(shortName, StringComparison.Ordinal)).Value;

    private sealed class TphContext : DbContext
    {
        public DbSet<TphEmployee> Employees => Set<TphEmployee>();
        public DbSet<TphManager> Managers => Set<TphManager>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(nameof(TphContext));
    }

    private class TphEmployee
    {
        public int Id { get; set; }
    }

    private sealed class TphManager : TphEmployee
    {
        public int Budget { get; set; }
    }

    private sealed class GuidKeyedContext : DbContext
    {
        public DbSet<Widget> Widgets => Set<Widget>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(nameof(GuidKeyedContext));
    }

    private sealed class Widget
    {
        public Guid Id { get; set; }
    }
}
