using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace EFCore.AutoSeed.IntegrationTests.SqlServer;

[Trait("Category", "Integration")]
public sealed class SqlServerAutoSeedTests : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder().Build();
    private string _connectionString = "";

    public async Task InitializeAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);
        _connectionString = _container.GetConnectionString();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);

    [Fact]
    public async Task AutoSeedAsync_against_sql_server_produces_referentially_valid_rows()
    {
        DbContextOptions<StoreContext> options = new DbContextOptionsBuilder<StoreContext>()
            .UseSqlServer(_connectionString)
            .Options;

        await using StoreContext context = new(options);
        await context.Database.EnsureCreatedAsync();

        IReadOnlyDictionary<string, int> result = await context.AutoSeedAsync(seed: 42, scale: 100);

        string customerKey = typeof(Customer).FullName!;
        string orderKey = typeof(Order).FullName!;
        string orderItemKey = typeof(OrderItem).FullName!;

        Assert.True(result.ContainsKey(customerKey));
        Assert.True(result.ContainsKey(orderKey));
        Assert.True(result.ContainsKey(orderItemKey));
        Assert.True(result[customerKey] > 0);
        Assert.True(result[orderKey] > 0);
        Assert.True(result[orderItemKey] > 0);

        int actualCustomerCount = await context.Customers.CountAsync();
        int actualOrderCount = await context.Orders.CountAsync();
        int actualOrderItemCount = await context.OrderItems.CountAsync();

        Assert.Equal(result[customerKey], actualCustomerCount);
        Assert.Equal(result[orderKey], actualOrderCount);
        Assert.Equal(result[orderItemKey], actualOrderItemCount);

        List<int> customerIds = await context.Customers.Select(customer => customer.Id).ToListAsync();
        List<int> orderIds = await context.Orders.Select(order => order.Id).ToListAsync();

        List<int> orderCustomerIds = await context.Orders.Select(order => order.CustomerId).ToListAsync();
        List<int> orderItemOrderIds = await context.OrderItems.Select(item => item.OrderId).ToListAsync();

        HashSet<int> customerIdSet = [.. customerIds];
        HashSet<int> orderIdSet = [.. orderIds];

        Assert.All(orderCustomerIds, customerId => Assert.Contains(customerId, customerIdSet));
        Assert.All(orderItemOrderIds, orderId => Assert.Contains(orderId, orderIdSet));
    }

    [Fact]
    public async Task AutoSeedAsync_WithTheSameSeed_ProducesTheSameDataOnSqlServer()
    {
        await CreateDatabaseAsync(_connectionString, "determinism_a");
        await CreateDatabaseAsync(_connectionString, "determinism_b");

        await using StoreContext contextA = CreateContext(WithDatabase(_connectionString, "determinism_a"));
        await using StoreContext contextB = CreateContext(WithDatabase(_connectionString, "determinism_b"));

        await contextA.Database.EnsureCreatedAsync();
        await contextB.Database.EnsureCreatedAsync();

        IReadOnlyDictionary<string, int> countsA = await contextA.AutoSeedAsync(seed: 42, scale: 20);
        IReadOnlyDictionary<string, int> countsB = await contextB.AutoSeedAsync(seed: 42, scale: 20);

        Assert.Equal(countsA, countsB);

        Customer firstCustomerA = await contextA.Customers.AsNoTracking().OrderBy(customer => customer.Id).FirstAsync();
        Customer firstCustomerB = await contextB.Customers.AsNoTracking().OrderBy(customer => customer.Id).FirstAsync();

        Assert.Equal(firstCustomerA.FirstName, firstCustomerB.FirstName);
        Assert.Equal(firstCustomerA.LastName, firstCustomerB.LastName);
        Assert.Equal(firstCustomerA.Email, firstCustomerB.Email);

        Order firstOrderA = await contextA.Orders.AsNoTracking().OrderBy(order => order.Id).FirstAsync();
        Order firstOrderB = await contextB.Orders.AsNoTracking().OrderBy(order => order.Id).FirstAsync();

        Assert.Equal(firstOrderA.Total, firstOrderB.Total);
        Assert.Equal(firstOrderA.CustomerId, firstOrderB.CustomerId);
    }

    private static async Task CreateDatabaseAsync(string connectionString, string databaseName)
    {
        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await using SqlCommand command = new($"CREATE DATABASE {databaseName}", connection);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static string WithDatabase(string connectionString, string databaseName)
    {
        SqlConnectionStringBuilder builder = new(connectionString)
        {
            InitialCatalog = databaseName,
        };

        return builder.ConnectionString;
    }

    private static StoreContext CreateContext(string connectionString)
    {
        DbContextOptions<StoreContext> options = new DbContextOptionsBuilder<StoreContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new StoreContext(options);
    }
}

public sealed class StoreContext(DbContextOptions<StoreContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().Property(order => order.Total).HasPrecision(10, 2);
        modelBuilder.Entity<OrderItem>().Property(item => item.UnitPrice).HasPrecision(10, 2);
    }
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
    public DateTime CreatedAt { get; set; }
    public decimal Total { get; set; }
}

public sealed class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public decimal UnitPrice { get; set; }
}
