using Microsoft.EntityFrameworkCore;

namespace EFCore.AutoSeed.UnitTests;

public sealed class AutoSeedAsyncTests
{
    private static int _databaseCounter;

    [Fact]
    public async Task AutoSeedAsync_ReturnsRowCountsMatchingTheDatabase()
    {
        using CustomerOrderContext context = NewContext();

        IReadOnlyDictionary<string, int> result = await context.AutoSeedAsync(seed: 42, scale: 10);

        string customerKey = typeof(Customer).FullName!;
        string orderKey = typeof(Order).FullName!;
        Assert.Equal([customerKey, orderKey], result.Keys.OrderBy(key => key, StringComparer.Ordinal));
        Assert.True(result[customerKey] > 0);
        Assert.True(result[orderKey] > 0);

        int customerCount = await context.Customers.CountAsync();
        int orderCount = await context.Orders.CountAsync();
        Assert.Equal(result[customerKey], customerCount);
        Assert.Equal(result[orderKey], orderCount);

        HashSet<int> customerIds = [.. await context.Customers.Select(customer => customer.Id).ToListAsync()];
        List<int> orderCustomerIds = await context.Orders.Select(order => order.CustomerId).ToListAsync();
        Assert.All(orderCustomerIds, customerId => Assert.Contains(customerId, customerIds));
    }

    [Fact]
    public async Task AutoSeedAsync_WithTheSameSeed_IsDeterministic()
    {
        using CustomerOrderContext first = NewContext();
        using CustomerOrderContext second = NewContext();

        IReadOnlyDictionary<string, int> firstResult = await first.AutoSeedAsync(seed: 7, scale: 10);
        IReadOnlyDictionary<string, int> secondResult = await second.AutoSeedAsync(seed: 7, scale: 10);

        string customerKey = typeof(Customer).FullName!;
        string orderKey = typeof(Order).FullName!;
        Assert.Equal(firstResult[customerKey], secondResult[customerKey]);
        Assert.Equal(firstResult[orderKey], secondResult[orderKey]);

        List<string> firstFirstNames = await first.Customers.OrderBy(customer => customer.Id).Select(customer => customer.FirstName).ToListAsync();
        List<string> secondFirstNames = await second.Customers.OrderBy(customer => customer.Id).Select(customer => customer.FirstName).ToListAsync();
        Assert.Equal(firstFirstNames, secondFirstNames);
    }

    [Fact]
    public async Task AutoSeedAsync_WithDifferentSeeds_ProducesDifferentData()
    {
        using CustomerOrderContext first = NewContext();
        using CustomerOrderContext second = NewContext();

        await first.AutoSeedAsync(seed: 1, scale: 10);
        await second.AutoSeedAsync(seed: 2, scale: 10);

        List<string> firstFirstNames = await first.Customers.OrderBy(customer => customer.Id).Select(customer => customer.FirstName).ToListAsync();
        List<string> secondFirstNames = await second.Customers.OrderBy(customer => customer.Id).Select(customer => customer.FirstName).ToListAsync();

        Assert.NotEqual(firstFirstNames, secondFirstNames);
    }

    [Fact]
    public async Task AutoSeedAsync_WithNullContext_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => DbContextAutoSeedExtensions.AutoSeedAsync(null!, seed: 1, scale: 10));
    }

    [Fact]
    public async Task AutoSeedAsync_WithNonPositiveScale_ThrowsArgumentOutOfRangeException()
    {
        using CustomerOrderContext context = NewContext();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => context.AutoSeedAsync(seed: 1, scale: 0));
    }

    private static CustomerOrderContext NewContext()
    {
        int id = Interlocked.Increment(ref _databaseCounter);
        return new CustomerOrderContext($"{nameof(CustomerOrderContext)}_{id}");
    }
}

public sealed class CustomerOrderContext(string databaseName) : DbContext
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(databaseName);
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
