using EFCore.AutoSeed.Exceptions;
using EFCore.AutoSeed.Pipeline;
using EFCore.AutoSeed.Providers;
using EFCore.AutoSeed.UnitTests.Pipeline.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using OwnedAddress = EFCore.AutoSeed.UnitTests.Owned.Address;
using OwnedCoordinates = EFCore.AutoSeed.UnitTests.Owned.Coordinates;
using OwnedCustomer = EFCore.AutoSeed.UnitTests.Owned.Customer;
using OwnedCustomerWithAddressContext = EFCore.AutoSeed.UnitTests.Owned.CustomerWithAddressContext;

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
    public async Task InsertAsync_WithAnOwnedType_WritesOwnedColumnsWithRealisticDeterministicValues()
    {
        using KeylessAndOwnedContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);
        CycleResolution resolution = new CycleResolver().Resolve(read.EntityTypes, read.Edges);
        IReadOnlyList<EntityGenerationPlan> plan = new GenerationPlan().Plan(resolution.Order, read.Edges, scale: 5, SeededRandom.FromRootSeed(1));

        FakeBulkInsertProvider provider = new();
        await new BulkPersistence(provider).InsertAsync(
            context, plan, read.Edges, resolution.DeferredEdges, GenerateAddressRow, SeededRandom.FromRootSeed(1), CancellationToken.None);

        IEntityType invoiceType = context.Model.FindEntityType(typeof(Invoice))!;
        IEntityType addressType = invoiceType.FindNavigation(nameof(Invoice.BillingAddress))!.TargetEntityType;
        string streetColumn = addressType.FindProperty(nameof(Address.Street))!.GetColumnName();
        string cityColumn = addressType.FindProperty(nameof(Address.City))!.GetColumnName();

        IReadOnlyList<IReadOnlyDictionary<string, object>> invoices = provider.Inserted("Invoice");
        Assert.NotEmpty(invoices);
        Assert.All(invoices, invoice =>
        {
            Assert.StartsWith("Street-", (string)invoice[streetColumn], StringComparison.Ordinal);
            Assert.StartsWith("City-", (string)invoice[cityColumn], StringComparison.Ordinal);
        });

        HashSet<string> distinctStreets = [.. invoices.Select(invoice => (string)invoice[streetColumn])];
        Assert.True(distinctStreets.Count > 1);

        using KeylessAndOwnedContext secondContext = new();
        ModelReadResult secondRead = new ModelReader().Read(secondContext.Model);
        CycleResolution secondResolution = new CycleResolver().Resolve(secondRead.EntityTypes, secondRead.Edges);
        IReadOnlyList<EntityGenerationPlan> secondPlan =
            new GenerationPlan().Plan(secondResolution.Order, secondRead.Edges, scale: 5, SeededRandom.FromRootSeed(1));

        FakeBulkInsertProvider secondProvider = new();
        await new BulkPersistence(secondProvider).InsertAsync(
            secondContext, secondPlan, secondRead.Edges, secondResolution.DeferredEdges,
            GenerateAddressRow, SeededRandom.FromRootSeed(1), CancellationToken.None);

        IReadOnlyList<IReadOnlyDictionary<string, object>> secondInvoices = secondProvider.Inserted("Invoice");
        Assert.Equal(invoices.Select(invoice => invoice[streetColumn]), secondInvoices.Select(invoice => invoice[streetColumn]));
        Assert.Equal(invoices.Select(invoice => invoice[cityColumn]), secondInvoices.Select(invoice => invoice[cityColumn]));
    }

    [Fact]
    public async Task InsertAsync_AndPersistence_ProduceIdenticalOwnedTypeValuesForTheSameSeed()
    {
        using OwnedCustomerWithAddressContext fidelityContext = new("BulkPersistenceEquivalence_Fidelity");
        using OwnedCustomerWithAddressContext fastContext = new("BulkPersistenceEquivalence_Fast");

        ModelReadResult fidelityRead = new ModelReader().Read(fidelityContext.Model);
        CycleResolution fidelityResolution = new CycleResolver().Resolve(fidelityRead.EntityTypes, fidelityRead.Edges);
        IReadOnlyList<EntityGenerationPlan> fidelityPlan =
            new GenerationPlan().Plan(fidelityResolution.Order, fidelityRead.Edges, scale: 8, SeededRandom.FromRootSeed(11));

        await new Persistence().InsertAsync(
            fidelityContext, fidelityPlan, fidelityRead.Edges, fidelityResolution.DeferredEdges,
            GenerateDeterministicRow, SeededRandom.FromRootSeed(11), CancellationToken.None);

        ModelReadResult fastRead = new ModelReader().Read(fastContext.Model);
        CycleResolution fastResolution = new CycleResolver().Resolve(fastRead.EntityTypes, fastRead.Edges);
        IReadOnlyList<EntityGenerationPlan> fastPlan =
            new GenerationPlan().Plan(fastResolution.Order, fastRead.Edges, scale: 8, SeededRandom.FromRootSeed(11));

        FakeBulkInsertProvider fastProvider = new();
        await new BulkPersistence(fastProvider).InsertAsync(
            fastContext, fastPlan, fastRead.Edges, fastResolution.DeferredEdges,
            GenerateDeterministicRow, SeededRandom.FromRootSeed(11), CancellationToken.None);

        List<OwnedCustomer> fidelityCustomers = await fidelityContext.Customers.OrderBy(customer => customer.Id).ToListAsync();
        IReadOnlyList<IReadOnlyDictionary<string, object>> fastRows = fastProvider.Inserted("Customer");

        IEntityType customerType = fastContext.Model.FindEntityType(typeof(OwnedCustomer))!;
        IEntityType addressType = customerType.FindNavigation(nameof(OwnedCustomer.Address))!.TargetEntityType;
        IEntityType coordinatesType = addressType.FindNavigation(nameof(OwnedAddress.Coordinates))!.TargetEntityType;

        string cityColumn = addressType.FindProperty(nameof(OwnedAddress.City))!.GetColumnName();
        string postalCodeColumn = addressType.FindProperty(nameof(OwnedAddress.PostalCode))!.GetColumnName();
        string latitudeColumn = coordinatesType.FindProperty(nameof(OwnedCoordinates.Latitude))!.GetColumnName();
        string longitudeColumn = coordinatesType.FindProperty(nameof(OwnedCoordinates.Longitude))!.GetColumnName();

        Assert.NotEmpty(fidelityCustomers);
        Assert.Equal(fidelityCustomers.Count, fastRows.Count);

        for (int index = 0; index < fidelityCustomers.Count; index++)
        {
            OwnedCustomer fidelityCustomer = fidelityCustomers[index];
            IReadOnlyDictionary<string, object> fastRow = fastRows[index];

            Assert.Equal(fidelityCustomer.Address.City, fastRow[cityColumn]);
            Assert.Equal(fidelityCustomer.Address.PostalCode, fastRow[postalCodeColumn]);
            Assert.NotNull(fidelityCustomer.Address.Coordinates);
            Assert.Equal(fidelityCustomer.Address.Coordinates!.Latitude, (double)fastRow[latitudeColumn]);
            Assert.Equal(fidelityCustomer.Address.Coordinates!.Longitude, (double)fastRow[longitudeColumn]);
        }
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
    public async Task InsertAsync_WithAGuidIdentityPrimaryKey_AssignsDeterministicNonDefaultValues()
    {
        using GuidKeyedContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);
        CycleResolution resolution = new CycleResolver().Resolve(read.EntityTypes, read.Edges);
        IReadOnlyList<EntityGenerationPlan> plan = new GenerationPlan().Plan(resolution.Order, read.Edges, scale: 20, SeededRandom.FromRootSeed(3));

        FakeBulkInsertProvider firstProvider = new();
        await new BulkPersistence(firstProvider).InsertAsync(
            context, plan, read.Edges, resolution.DeferredEdges, GenerateRow, SeededRandom.FromRootSeed(3), CancellationToken.None);

        FakeBulkInsertProvider secondProvider = new();
        await new BulkPersistence(secondProvider).InsertAsync(
            context, plan, read.Edges, resolution.DeferredEdges, GenerateRow, SeededRandom.FromRootSeed(3), CancellationToken.None);

        List<Guid> firstIds = [.. firstProvider.Inserted("Widget").Select(row => (Guid)row["Id"])];
        List<Guid> secondIds = [.. secondProvider.Inserted("Widget").Select(row => (Guid)row["Id"])];

        Assert.Equal(20, firstIds.Count);
        Assert.All(firstIds, id => Assert.NotEqual(Guid.Empty, id));
        Assert.Equal(firstIds.Count, firstIds.Distinct().Count());
        Assert.Equal(firstIds, secondIds);
    }

    [Fact]
    public async Task InsertAsync_WithALongIdentityPrimaryKey_AssignsSequentialValues()
    {
        using LongKeyedContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);
        CycleResolution resolution = new CycleResolver().Resolve(read.EntityTypes, read.Edges);
        IReadOnlyList<EntityGenerationPlan> plan = new GenerationPlan().Plan(resolution.Order, read.Edges, scale: 5, SeededRandom.FromRootSeed(9));

        FakeBulkInsertProvider provider = new();
        await new BulkPersistence(provider).InsertAsync(
            context, plan, read.Edges, resolution.DeferredEdges, GenerateRow, SeededRandom.FromRootSeed(9), CancellationToken.None);

        List<long> machineIds = [.. provider.Inserted("Machine").Select(row => (long)row["Id"])];
        Assert.Equal([.. Enumerable.Range(1, machineIds.Count).Select(value => (long)value)], machineIds);
    }

    [Fact]
    public async Task InsertAsync_WithAnUnsupportedIdentityPrimaryKeyType_ThrowsUnsupportedEntityTypeException()
    {
        using ShortKeyedContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);
        CycleResolution resolution = new CycleResolver().Resolve(read.EntityTypes, read.Edges);
        IReadOnlyList<EntityGenerationPlan> plan = new GenerationPlan().Plan(resolution.Order, read.Edges, scale: 3, SeededRandom.FromRootSeed(1));

        FakeBulkInsertProvider provider = new();
        UnsupportedEntityTypeException exception = await Assert.ThrowsAsync<UnsupportedEntityTypeException>(() => new BulkPersistence(provider).InsertAsync(
            context, plan, read.Edges, resolution.DeferredEdges, GenerateRow, SeededRandom.FromRootSeed(1), CancellationToken.None));

        Assert.Contains("Gadget", exception.EntityTypeName);
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

    private static Dictionary<string, object> GenerateAddressRow(IEntityType entityType, SeededRandom random)
    {
        if (entityType.ClrType != typeof(Address))
        {
            return [];
        }

        return new Dictionary<string, object>
        {
            ["Street"] = $"Street-{random.Derive("Street").Next(0, 1_000_000)}",
            ["City"] = $"City-{random.Derive("City").Next(0, 1_000_000)}",
        };
    }

    private static Dictionary<string, object> GenerateDeterministicRow(IEntityType entityType, SeededRandom random)
    {
        Dictionary<string, object> values = [];
        foreach (IProperty property in entityType.GetProperties())
        {
            if (property.ValueGenerated == ValueGenerated.OnAdd || property.IsForeignKey())
            {
                continue;
            }

            values[property.Name] = GenerateDeterministicValue(property, random.Derive(property.Name));
        }

        return values;
    }

    private static object GenerateDeterministicValue(IProperty property, SeededRandom random)
    {
        if (property.ClrType == typeof(double))
        {
            return random.NextDouble();
        }

        if (property.ClrType == typeof(int))
        {
            return random.Next(0, 1_000_000);
        }

        return $"{property.Name}-{random.Next(0, 1_000_000)}";
    }

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

    private sealed class LongKeyedContext : DbContext
    {
        public DbSet<Machine> Machines => Set<Machine>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(nameof(LongKeyedContext));
    }

    private sealed class Machine
    {
        public long Id { get; set; }
    }

    private sealed class ShortKeyedContext : DbContext
    {
        public DbSet<Gadget> Gadgets => Set<Gadget>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(nameof(ShortKeyedContext));
    }

    private sealed class Gadget
    {
        public short Id { get; set; }
    }
}
