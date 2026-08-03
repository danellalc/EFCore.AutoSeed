using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace EFCore.AutoSeed.IntegrationTests.Postgres;

[Trait("Category", "Integration")]
public sealed class PostgreSqlAutoSeedTests : IAsyncLifetime
{
    private readonly PostgreSqlBuilder _builder = new PostgreSqlBuilder()
        .WithDatabase("autoseed")
        .WithUsername("autoseed")
        .WithPassword("autoseed");

    private PostgreSqlContainer _postgres = null!;

    public async Task InitializeAsync()
    {
        _postgres = _builder.Build();
        await _postgres.StartAsync().ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task AutoSeedAsync_InsertsReferentiallyValidRows()
    {
        await using OrderStoreContext context = CreateContext(_postgres.GetConnectionString());
        await context.Database.EnsureCreatedAsync();

        IReadOnlyDictionary<string, int> counts = await context.AutoSeedAsync(seed: 42, scale: 100);

        string customerKey = EntityTypeName<Customer>(context);
        string orderKey = EntityTypeName<Order>(context);
        string orderItemKey = EntityTypeName<OrderItem>(context);

        Assert.Equal(100, counts[customerKey]);
        Assert.True(counts[orderKey] > 0);
        Assert.True(counts[orderItemKey] > 0);

        List<Customer> customers = await context.Customers.AsNoTracking().ToListAsync();
        List<Order> orders = await context.Orders.AsNoTracking().ToListAsync();
        List<OrderItem> orderItems = await context.OrderItems.AsNoTracking().ToListAsync();

        Assert.Equal(counts[customerKey], customers.Count);
        Assert.Equal(counts[orderKey], orders.Count);
        Assert.Equal(counts[orderItemKey], orderItems.Count);

        HashSet<int> customerIds = [.. customers.Select(customer => customer.Id)];
        HashSet<int> orderIds = [.. orders.Select(order => order.Id)];

        Assert.All(orders, order => Assert.Contains(order.CustomerId, customerIds));
        Assert.All(orderItems, orderItem => Assert.Contains(orderItem.OrderId, orderIds));
    }

    [Fact]
    public async Task AutoSeedAsync_SameSeedProducesSameData()
    {
        string connectionString = _postgres.GetConnectionString();
        await CreateDatabaseAsync(connectionString, "determinism_a");
        await CreateDatabaseAsync(connectionString, "determinism_b");

        await using OrderStoreContext contextA = CreateContext(WithDatabase(connectionString, "determinism_a"));
        await using OrderStoreContext contextB = CreateContext(WithDatabase(connectionString, "determinism_b"));

        await contextA.Database.EnsureCreatedAsync();
        await contextB.Database.EnsureCreatedAsync();

        IReadOnlyDictionary<string, int> countsA = await contextA.AutoSeedAsync(seed: 42, scale: 20);
        IReadOnlyDictionary<string, int> countsB = await contextB.AutoSeedAsync(seed: 42, scale: 20);

        Assert.Equal(countsA, countsB);

        Customer firstCustomerA = await contextA.Customers.AsNoTracking()
            .OrderBy(customer => customer.Id).FirstAsync();
        Customer firstCustomerB = await contextB.Customers.AsNoTracking()
            .OrderBy(customer => customer.Id).FirstAsync();

        Assert.Equal(firstCustomerA.FirstName, firstCustomerB.FirstName);
        Assert.Equal(firstCustomerA.LastName, firstCustomerB.LastName);
        Assert.Equal(firstCustomerA.Email, firstCustomerB.Email);

        Order firstOrderA = await contextA.Orders.AsNoTracking()
            .OrderBy(order => order.Id).FirstAsync();
        Order firstOrderB = await contextB.Orders.AsNoTracking()
            .OrderBy(order => order.Id).FirstAsync();

        Assert.Equal(firstOrderA.Total, firstOrderB.Total);
        Assert.Equal(firstOrderA.CustomerId, firstOrderB.CustomerId);
    }

    [Fact]
    public async Task AutoSeedFastAsync_InsertsReferentiallyValidRows()
    {
        await using OrderStoreContext context = CreateContext(_postgres.GetConnectionString());
        await context.Database.EnsureCreatedAsync();

        IReadOnlyDictionary<string, int> counts = await context.AutoSeedFastAsync(seed: 42, scale: 100);

        string customerKey = EntityTypeName<Customer>(context);
        string orderKey = EntityTypeName<Order>(context);
        string orderItemKey = EntityTypeName<OrderItem>(context);

        Assert.Equal(100, counts[customerKey]);
        Assert.True(counts[orderKey] > 0);
        Assert.True(counts[orderItemKey] > 0);

        List<Customer> customers = await context.Customers.AsNoTracking().ToListAsync();
        List<Order> orders = await context.Orders.AsNoTracking().ToListAsync();
        List<OrderItem> orderItems = await context.OrderItems.AsNoTracking().ToListAsync();

        Assert.Equal(counts[customerKey], customers.Count);
        Assert.Equal(counts[orderKey], orders.Count);
        Assert.Equal(counts[orderItemKey], orderItems.Count);

        HashSet<int> customerIds = [.. customers.Select(customer => customer.Id)];
        HashSet<int> orderIds = [.. orders.Select(order => order.Id)];

        Assert.All(orders, order => Assert.Contains(order.CustomerId, customerIds));
        Assert.All(orderItems, orderItem => Assert.Contains(orderItem.OrderId, orderIds));
    }

    [Fact]
    public async Task AutoSeedFastAsync_ProducesTheSameDataAsAutoSeedAsyncForTheSameSeed()
    {
        string connectionString = _postgres.GetConnectionString();
        await CreateDatabaseAsync(connectionString, "equivalence_fidelity");
        await CreateDatabaseAsync(connectionString, "equivalence_fast");

        await using OrderStoreContext fidelityContext = CreateContext(WithDatabase(connectionString, "equivalence_fidelity"));
        await using OrderStoreContext fastContext = CreateContext(WithDatabase(connectionString, "equivalence_fast"));

        await fidelityContext.Database.EnsureCreatedAsync();
        await fastContext.Database.EnsureCreatedAsync();

        IReadOnlyDictionary<string, int> fidelityCounts = await fidelityContext.AutoSeedAsync(seed: 55, scale: 30);
        IReadOnlyDictionary<string, int> fastCounts = await fastContext.AutoSeedFastAsync(seed: 55, scale: 30);

        Assert.Equal(fidelityCounts, fastCounts);

        List<Customer> fidelityCustomers = await fidelityContext.Customers.AsNoTracking().OrderBy(customer => customer.Id).ToListAsync();
        List<Customer> fastCustomers = await fastContext.Customers.AsNoTracking().OrderBy(customer => customer.Id).ToListAsync();

        Assert.Equal(fidelityCustomers.Count, fastCustomers.Count);
        for (int index = 0; index < fidelityCustomers.Count; index++)
        {
            Assert.Equal(fidelityCustomers[index].Id, fastCustomers[index].Id);
            Assert.Equal(fidelityCustomers[index].FirstName, fastCustomers[index].FirstName);
            Assert.Equal(fidelityCustomers[index].LastName, fastCustomers[index].LastName);
            Assert.Equal(fidelityCustomers[index].Email, fastCustomers[index].Email);
        }

        List<Order> fidelityOrders = await fidelityContext.Orders.AsNoTracking().OrderBy(order => order.Id).ToListAsync();
        List<Order> fastOrders = await fastContext.Orders.AsNoTracking().OrderBy(order => order.Id).ToListAsync();

        Assert.Equal(fidelityOrders.Count, fastOrders.Count);
        for (int index = 0; index < fidelityOrders.Count; index++)
        {
            Assert.Equal(fidelityOrders[index].Id, fastOrders[index].Id);
            Assert.Equal(fidelityOrders[index].CustomerId, fastOrders[index].CustomerId);
            Assert.Equal(fidelityOrders[index].Total, fastOrders[index].Total);
        }

        List<OrderItem> fidelityItems = await fidelityContext.OrderItems.AsNoTracking().OrderBy(item => item.Id).ToListAsync();
        List<OrderItem> fastItems = await fastContext.OrderItems.AsNoTracking().OrderBy(item => item.Id).ToListAsync();

        Assert.Equal(fidelityItems.Count, fastItems.Count);
        for (int index = 0; index < fidelityItems.Count; index++)
        {
            Assert.Equal(fidelityItems[index].Id, fastItems[index].Id);
            Assert.Equal(fidelityItems[index].OrderId, fastItems[index].OrderId);
            Assert.Equal(fidelityItems[index].Price, fastItems[index].Price);
        }
    }

    private static async Task CreateDatabaseAsync(string connectionString, string databaseName)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await using NpgsqlCommand command = new($"CREATE DATABASE {databaseName}", connection);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static string WithDatabase(string connectionString, string databaseName)
    {
        NpgsqlConnectionStringBuilder builder = new(connectionString)
        {
            Database = databaseName,
        };

        return builder.ConnectionString;
    }

    private static string EntityTypeName<TEntity>(DbContext context) =>
        context.Model.FindEntityType(typeof(TEntity))!.Name;

    private static OrderStoreContext CreateContext(string connectionString)
    {
        DbContextOptions<OrderStoreContext> options = new DbContextOptionsBuilder<OrderStoreContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new OrderStoreContext(options);
    }
}

public sealed class OrderStoreContext(DbContextOptions<OrderStoreContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
}

public sealed class Customer
{
    public int Id { get; set; }

    public string FirstName { get; set; } = "";

    public string LastName { get; set; } = "";

    public string Email { get; set; } = "";
}

public sealed class Order
{
    public int Id { get; set; }

    public int CustomerId { get; set; }

    public Customer Customer { get; set; } = null!;

    public decimal Total { get; set; }
}

public sealed class OrderItem
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public Order Order { get; set; } = null!;

    public decimal Price { get; set; }
}
