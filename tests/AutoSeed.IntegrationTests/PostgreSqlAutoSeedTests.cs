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
