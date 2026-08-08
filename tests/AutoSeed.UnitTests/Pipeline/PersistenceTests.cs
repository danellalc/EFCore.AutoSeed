using EFCore.AutoSeed.Exceptions;
using EFCore.AutoSeed.Pipeline;
using EFCore.AutoSeed.UnitTests.Pipeline.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.UnitTests.Pipeline;

public sealed class PersistenceTests
{
    [Fact]
    public async Task InsertAsync_WithLinearChain_InsertsRowsWithReferentialIntegrity()
    {
        using IsolatedLinearChainContext context = new(UniqueDatabaseName());
        ModelReadResult read = new ModelReader().Read(context.Model);
        CycleResolution resolution = new CycleResolver().Resolve(read.EntityTypes, read.Edges);
        IReadOnlyList<EntityGenerationPlan> plan = new GenerationPlan().Plan(resolution.Order, read.Edges, scale: 5, SeededRandom.FromRootSeed(42));

        IReadOnlyDictionary<string, int> result = await new Persistence().InsertAsync(
            context, plan, read.Edges, resolution.DeferredEdges, GenerateRow, SeededRandom.FromRootSeed(42), CancellationToken.None);

        List<Fixtures.Customer> customers = await context.Customers.ToListAsync();
        List<Fixtures.Order> orders = await context.Orders.ToListAsync();
        List<OrderItem> orderItems = await context.OrderItems.ToListAsync();

        Assert.Equal(RowCount(plan, "Customer"), customers.Count);
        Assert.Equal(RowCount(plan, "Order"), orders.Count);
        Assert.Equal(RowCount(plan, "OrderItem"), orderItems.Count);

        Assert.Equal(customers.Count, ResultValue(result, "Customer"));
        Assert.Equal(orders.Count, ResultValue(result, "Order"));
        Assert.Equal(orderItems.Count, ResultValue(result, "OrderItem"));

        HashSet<int> customerIds = [.. customers.Select(customer => customer.Id)];
        Assert.All(orders, order => Assert.Contains(order.CustomerId, customerIds));

        HashSet<int> orderIds = [.. orders.Select(order => order.Id)];
        Assert.All(orderItems, item => Assert.Contains(item.OrderId, orderIds));
    }

    [Fact]
    public async Task InsertAsync_WithDiamond_AssignsBothPrincipalsToAlreadyInsertedRows()
    {
        using IsolatedDiamondContext context = new(UniqueDatabaseName());
        ModelReadResult read = new ModelReader().Read(context.Model);
        CycleResolution resolution = new CycleResolver().Resolve(read.EntityTypes, read.Edges);
        IReadOnlyList<EntityGenerationPlan> plan = new GenerationPlan().Plan(resolution.Order, read.Edges, scale: 15, SeededRandom.FromRootSeed(7));

        await new Persistence().InsertAsync(
            context, plan, read.Edges, resolution.DeferredEdges, GenerateRow, SeededRandom.FromRootSeed(7), CancellationToken.None);

        List<Left> lefts = await context.Lefts.ToListAsync();
        List<Right> rights = await context.Rights.ToListAsync();
        List<Merge> merges = await context.Merges.ToListAsync();

        HashSet<int> leftIds = [.. lefts.Select(left => left.Id)];
        HashSet<int> rightIds = [.. rights.Select(right => right.Id)];

        Assert.NotEmpty(merges);
        Assert.All(merges, merge =>
        {
            Assert.Contains(merge.LeftId, leftIds);
            Assert.Contains(merge.RightId, rightIds);
        });
    }

    [Fact]
    public async Task InsertAsync_WithNullableSelfCycleAndManyEmployees_AssignsAManagerFromADifferentEmployee()
    {
        using IsolatedNullableSelfCycleContext context = new(UniqueDatabaseName());
        ModelReadResult read = new ModelReader().Read(context.Model);
        CycleResolution resolution = new CycleResolver().Resolve(read.EntityTypes, read.Edges);
        Assert.Single(resolution.DeferredEdges);

        IReadOnlyList<EntityGenerationPlan> plan = new GenerationPlan().Plan(resolution.Order, read.Edges, scale: 10, SeededRandom.FromRootSeed(3));

        await new Persistence().InsertAsync(
            context, plan, read.Edges, resolution.DeferredEdges, GenerateRow, SeededRandom.FromRootSeed(3), CancellationToken.None);

        List<Employee> employees = await context.Employees.ToListAsync();

        Assert.True(employees.Count > 1);
        Assert.All(employees, employee => Assert.True(employee.ManagerId is not null && employee.ManagerId != employee.Id));
    }

    [Fact]
    public async Task InsertAsync_WithNullableSelfCycleAndOneEmployee_LeavesManagerIdNull()
    {
        using IsolatedNullableSelfCycleContext context = new(UniqueDatabaseName());
        ModelReadResult read = new ModelReader().Read(context.Model);
        CycleResolution resolution = new CycleResolver().Resolve(read.EntityTypes, read.Edges);

        IReadOnlyList<EntityGenerationPlan> plan = new GenerationPlan().Plan(resolution.Order, read.Edges, scale: 1, SeededRandom.FromRootSeed(3));

        await new Persistence().InsertAsync(
            context, plan, read.Edges, resolution.DeferredEdges, GenerateRow, SeededRandom.FromRootSeed(3), CancellationToken.None);

        Employee employee = Assert.Single(await context.Employees.ToListAsync());
        Assert.Null(employee.ManagerId);
    }

    [Fact]
    public async Task InsertAsync_WithTheSameSeed_ProducesIdenticalRowCountsAndForeignKeys()
    {
        (IReadOnlyDictionary<string, int> firstCounts, List<Fixtures.Order> firstOrders) = await RunLinearChainPipelineAsync(UniqueDatabaseName());
        (IReadOnlyDictionary<string, int> secondCounts, List<Fixtures.Order> secondOrders) = await RunLinearChainPipelineAsync(UniqueDatabaseName());

        Assert.Equal(firstCounts.Count, secondCounts.Count);
        foreach (KeyValuePair<string, int> entry in firstCounts)
        {
            Assert.Equal(entry.Value, secondCounts[entry.Key]);
        }

        Assert.Equal(firstOrders.Count, secondOrders.Count);
        Assert.Equal(
            firstOrders.Select(order => order.CustomerId),
            secondOrders.Select(order => order.CustomerId));
    }

    [Fact]
    public async Task InsertAsync_WithAnEntityTypeWithNoParameterlessConstructor_ThrowsUnsupportedEntityTypeException()
    {
        using IsolatedNoParameterlessConstructorContext context = new(UniqueDatabaseName());
        ModelReadResult read = new ModelReader().Read(context.Model);
        CycleResolution resolution = new CycleResolver().Resolve(read.EntityTypes, read.Edges);
        IReadOnlyList<EntityGenerationPlan> plan = new GenerationPlan().Plan(resolution.Order, read.Edges, scale: 3, SeededRandom.FromRootSeed(1));

        UnsupportedEntityTypeException exception = await Assert.ThrowsAsync<UnsupportedEntityTypeException>(() => new Persistence().InsertAsync(
            context, plan, read.Edges, resolution.DeferredEdges, GenerateRow, SeededRandom.FromRootSeed(1), CancellationToken.None));

        Assert.Contains("NoParameterlessConstructorEntity", exception.EntityTypeName);
    }

    [Fact]
    public async Task InsertAsync_WithARequiredPrincipalThatHasZeroRows_ThrowsUnsupportedEntityTypeException()
    {
        using IsolatedLinearChainContext context = new(UniqueDatabaseName());
        ModelReadResult read = new ModelReader().Read(context.Model);
        CycleResolution resolution = new CycleResolver().Resolve(read.EntityTypes, read.Edges);

        List<EntityGenerationPlan> plan = [.. resolution.Order.Select(entityType => entityType.Name.EndsWith("Customer", StringComparison.Ordinal)
            ? new EntityGenerationPlan(entityType, 0, null, null)
            : new EntityGenerationPlan(entityType, 3, null, null))];

        UnsupportedEntityTypeException exception = await Assert.ThrowsAsync<UnsupportedEntityTypeException>(() => new Persistence().InsertAsync(
            context, plan, read.Edges, resolution.DeferredEdges, GenerateRow, SeededRandom.FromRootSeed(1), CancellationToken.None));

        Assert.Contains("Customer", exception.Message);
    }

    [Fact]
    public async Task InsertAsync_WithACompositeForeignKeyIntoACompositePrimaryKey_CopiesBothColumnsFromTheSameParentRow()
    {
        using IsolatedCompositeForeignKeyContext context = new(UniqueDatabaseName());
        ModelReadResult read = new ModelReader().Read(context.Model);
        CycleResolution resolution = new CycleResolver().Resolve(read.EntityTypes, read.Edges);
        IReadOnlyList<EntityGenerationPlan> plan = new GenerationPlan().Plan(resolution.Order, read.Edges, scale: 20, SeededRandom.FromRootSeed(11));

        Dictionary<string, object> GenerateOfficeNumber(IEntityType entityType, SeededRandom random) =>
            entityType.ClrType == typeof(Office)
                ? new Dictionary<string, object> { ["Number"] = random.Next(0, 1_000_000) }
                : [];

        await new Persistence().InsertAsync(
            context, plan, read.Edges, resolution.DeferredEdges, GenerateOfficeNumber, SeededRandom.FromRootSeed(11), CancellationToken.None);

        HashSet<(int RegionId, int Number)> officeKeys = [.. (await context.Offices.ToListAsync())
            .Select(office => (office.RegionId, office.Number))];

        List<OfficeEmployee> employees = await context.Employees.ToListAsync();
        Assert.NotEmpty(employees);
        Assert.All(employees, employee => Assert.Contains((employee.OfficeRegionId, employee.OfficeNumber), officeKeys));
    }

    [Fact]
    public async Task InsertAsync_WithNullArguments_ThrowsArgumentNullException()
    {
        using IsolatedLinearChainContext context = new(UniqueDatabaseName());
        ModelReadResult read = new ModelReader().Read(context.Model);
        CycleResolution resolution = new CycleResolver().Resolve(read.EntityTypes, read.Edges);
        IReadOnlyList<EntityGenerationPlan> plan = new GenerationPlan().Plan(resolution.Order, read.Edges, scale: 1, SeededRandom.FromRootSeed(1));
        SeededRandom random = SeededRandom.FromRootSeed(1);

        await Assert.ThrowsAsync<ArgumentNullException>(() => new Persistence().InsertAsync(
            null!, plan, read.Edges, resolution.DeferredEdges, GenerateRow, random, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => new Persistence().InsertAsync(
            context, null!, read.Edges, resolution.DeferredEdges, GenerateRow, random, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => new Persistence().InsertAsync(
            context, plan, null!, resolution.DeferredEdges, GenerateRow, random, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => new Persistence().InsertAsync(
            context, plan, read.Edges, null!, GenerateRow, random, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => new Persistence().InsertAsync(
            context, plan, read.Edges, resolution.DeferredEdges, null!, random, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => new Persistence().InsertAsync(
            context, plan, read.Edges, resolution.DeferredEdges, GenerateRow, null!, CancellationToken.None));
    }

    [Fact]
    public async Task InsertAsync_WithAnOptionalForeignKeyAndTheDefaultNullRate_LeavesItNull()
    {
        using IsolatedOptionalForeignKeyContext context = new(UniqueDatabaseName());
        ModelReadResult read = new ModelReader().Read(context.Model);
        CycleResolution resolution = new CycleResolver().Resolve(read.EntityTypes, read.Edges);
        IReadOnlyList<EntityGenerationPlan> plan = new GenerationPlan().Plan(resolution.Order, read.Edges, scale: 10, SeededRandom.FromRootSeed(1));

        await new Persistence().InsertAsync(
            context, plan, read.Edges, resolution.DeferredEdges, GenerateRow, SeededRandom.FromRootSeed(1), CancellationToken.None);

        List<PurchaseOrder> orders = await context.PurchaseOrders.ToListAsync();
        Assert.NotEmpty(orders);
        Assert.All(orders, order => Assert.Null(order.PromoCodeId));
    }

    [Fact]
    public async Task InsertAsync_WithAnOptionalForeignKeyAndAPositiveNullRate_PopulatesSomeRowsAndLeavesOthersNull()
    {
        using IsolatedOptionalForeignKeyContext context = new(UniqueDatabaseName());
        ModelReadResult read = new ModelReader().Read(context.Model);
        CycleResolution resolution = new CycleResolver().Resolve(read.EntityTypes, read.Edges);
        IReadOnlyList<EntityGenerationPlan> plan = new GenerationPlan().Plan(resolution.Order, read.Edges, scale: 30, SeededRandom.FromRootSeed(1));

        await new Persistence().InsertAsync(
            context, plan, read.Edges, resolution.DeferredEdges, GenerateRow, SeededRandom.FromRootSeed(1), CancellationToken.None,
            existingRowsByEntityType: null, nullRate: 0.3);

        List<PromoCode> promoCodes = await context.PromoCodes.ToListAsync();
        List<PurchaseOrder> orders = await context.PurchaseOrders.ToListAsync();

        HashSet<int> promoCodeIds = [.. promoCodes.Select(promoCode => promoCode.Id)];
        Assert.Contains(orders, order => order.PromoCodeId is not null);
        Assert.Contains(orders, order => order.PromoCodeId is null);
        Assert.All(orders, order => Assert.True(order.PromoCodeId is null || promoCodeIds.Contains(order.PromoCodeId.Value)));
    }

    [Fact]
    public async Task InsertAsync_WithAnOptionalForeignKeyWhosePrincipalHasZeroRows_LeavesItNullWithoutThrowing()
    {
        using IsolatedOptionalForeignKeyContext context = new(UniqueDatabaseName());
        ModelReadResult read = new ModelReader().Read(context.Model);
        CycleResolution resolution = new CycleResolver().Resolve(read.EntityTypes, read.Edges);

        List<EntityGenerationPlan> plan = [.. resolution.Order.Select(entityType => entityType.Name.EndsWith("PromoCode", StringComparison.Ordinal)
            ? new EntityGenerationPlan(entityType, 0, null, null)
            : new EntityGenerationPlan(entityType, 10, null, null))];

        await new Persistence().InsertAsync(
            context, plan, read.Edges, resolution.DeferredEdges, GenerateRow, SeededRandom.FromRootSeed(1), CancellationToken.None,
            existingRowsByEntityType: null, nullRate: 1);

        List<PurchaseOrder> orders = await context.PurchaseOrders.ToListAsync();
        Assert.NotEmpty(orders);
        Assert.All(orders, order => Assert.Null(order.PromoCodeId));
    }

    [Fact]
    public async Task InsertAsync_WithAnOptionalForeignKeyAndTheSameSeed_ProducesTheSamePattern()
    {
        using IsolatedOptionalForeignKeyContext firstContext = new(UniqueDatabaseName());
        using IsolatedOptionalForeignKeyContext secondContext = new(UniqueDatabaseName());
        ModelReadResult firstRead = new ModelReader().Read(firstContext.Model);
        ModelReadResult secondRead = new ModelReader().Read(secondContext.Model);
        CycleResolution firstResolution = new CycleResolver().Resolve(firstRead.EntityTypes, firstRead.Edges);
        CycleResolution secondResolution = new CycleResolver().Resolve(secondRead.EntityTypes, secondRead.Edges);
        IReadOnlyList<EntityGenerationPlan> firstPlan =
            new GenerationPlan().Plan(firstResolution.Order, firstRead.Edges, scale: 30, SeededRandom.FromRootSeed(5));
        IReadOnlyList<EntityGenerationPlan> secondPlan =
            new GenerationPlan().Plan(secondResolution.Order, secondRead.Edges, scale: 30, SeededRandom.FromRootSeed(5));

        await new Persistence().InsertAsync(
            firstContext, firstPlan, firstRead.Edges, firstResolution.DeferredEdges, GenerateRow, SeededRandom.FromRootSeed(5),
            CancellationToken.None, existingRowsByEntityType: null, nullRate: 0.3);
        await new Persistence().InsertAsync(
            secondContext, secondPlan, secondRead.Edges, secondResolution.DeferredEdges, GenerateRow, SeededRandom.FromRootSeed(5),
            CancellationToken.None, existingRowsByEntityType: null, nullRate: 0.3);

        List<bool> firstPattern = await firstContext.PurchaseOrders.OrderBy(order => order.Id).Select(order => order.PromoCodeId != null).ToListAsync();
        List<bool> secondPattern = await secondContext.PurchaseOrders.OrderBy(order => order.Id).Select(order => order.PromoCodeId != null).ToListAsync();

        Assert.Equal(firstPattern, secondPattern);
    }

    private static async Task<(IReadOnlyDictionary<string, int> Counts, List<Fixtures.Order> Orders)> RunLinearChainPipelineAsync(string databaseName)
    {
        using IsolatedLinearChainContext context = new(databaseName);
        ModelReadResult read = new ModelReader().Read(context.Model);
        CycleResolution resolution = new CycleResolver().Resolve(read.EntityTypes, read.Edges);
        IReadOnlyList<EntityGenerationPlan> plan = new GenerationPlan().Plan(resolution.Order, read.Edges, scale: 8, SeededRandom.FromRootSeed(99));

        IReadOnlyDictionary<string, int> counts = await new Persistence().InsertAsync(
            context, plan, read.Edges, resolution.DeferredEdges, GenerateRow, SeededRandom.FromRootSeed(99), CancellationToken.None);

        List<Fixtures.Order> orders = await context.Orders.OrderBy(order => order.Id).ToListAsync();
        return (counts, orders);
    }

    private static Dictionary<string, object> GenerateRow(IEntityType entityType, SeededRandom random) => [];

    private static string UniqueDatabaseName() => Guid.NewGuid().ToString("N");

    private static int RowCount(IReadOnlyList<EntityGenerationPlan> plan, string shortName) =>
        Assert.Single(plan, entry => entry.EntityType.Name.EndsWith(shortName, StringComparison.Ordinal)).RowCount;

    private static int ResultValue(IReadOnlyDictionary<string, int> result, string shortName) =>
        Assert.Single(result, entry => entry.Key.EndsWith(shortName, StringComparison.Ordinal)).Value;

    private sealed class IsolatedLinearChainContext(string databaseName) : DbContext
    {
        public DbSet<Fixtures.Customer> Customers => Set<Fixtures.Customer>();
        public DbSet<Fixtures.Order> Orders => Set<Fixtures.Order>();
        public DbSet<OrderItem> OrderItems => Set<OrderItem>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(databaseName);
    }

    private sealed class IsolatedDiamondContext(string databaseName) : DbContext
    {
        public DbSet<Root> Roots => Set<Root>();
        public DbSet<Left> Lefts => Set<Left>();
        public DbSet<Right> Rights => Set<Right>();
        public DbSet<Merge> Merges => Set<Merge>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(databaseName);
    }

    private sealed class IsolatedNullableSelfCycleContext(string databaseName) : DbContext
    {
        public DbSet<Employee> Employees => Set<Employee>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(databaseName);
    }

    private sealed class IsolatedNoParameterlessConstructorContext(string databaseName) : DbContext
    {
        public DbSet<NoParameterlessConstructorEntity> Entities => Set<NoParameterlessConstructorEntity>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(databaseName);
    }

    private sealed class IsolatedOptionalForeignKeyContext(string databaseName) : DbContext
    {
        public DbSet<PromoCode> PromoCodes => Set<PromoCode>();
        public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(databaseName);
    }

    private sealed class PromoCode
    {
        public int Id { get; set; }
    }

    private sealed class PurchaseOrder
    {
        public int Id { get; set; }
        public int? PromoCodeId { get; set; }
        public PromoCode? PromoCode { get; set; }
    }

    private sealed class IsolatedCompositeForeignKeyContext(string databaseName) : DbContext
    {
        public DbSet<Region> Regions => Set<Region>();
        public DbSet<Office> Offices => Set<Office>();
        public DbSet<OfficeEmployee> Employees => Set<OfficeEmployee>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(databaseName);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Office>().HasKey(office => new { office.RegionId, office.Number });

            modelBuilder.Entity<OfficeEmployee>()
                .HasOne(employee => employee.Office)
                .WithMany()
                .HasForeignKey(employee => new { employee.OfficeRegionId, employee.OfficeNumber })
                .HasPrincipalKey(office => new { office.RegionId, office.Number });
        }
    }

    private sealed class Region
    {
        public int Id { get; set; }
        public List<Office> Offices { get; set; } = [];
    }

    private sealed class Office
    {
        public int RegionId { get; set; }
        public Region Region { get; set; } = null!;
        public int Number { get; set; }
    }

    private sealed class OfficeEmployee
    {
        public int Id { get; set; }
        public int OfficeRegionId { get; set; }
        public int OfficeNumber { get; set; }
        public Office Office { get; set; } = null!;
    }

    private sealed class NoParameterlessConstructorEntity
    {
        public NoParameterlessConstructorEntity(string name)
        {
            Name = name;
        }

        public int Id { get; set; }

        public string Name { get; private set; }
    }
}
