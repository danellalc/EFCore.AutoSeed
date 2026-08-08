using EFCore.AutoSeed.Exceptions;
using EFCore.AutoSeed.Inference;
using EFCore.AutoSeed.UnitTests.Pipeline;
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
    public async Task AutoSeedAsync_WithACustomLocale_ProducesDifferentNamesThanTheDefaultLocale()
    {
        using CustomerOrderContext defaultLocale = NewContext();
        using CustomerOrderContext customLocale = NewContext();

        await defaultLocale.AutoSeedAsync(seed: 42, scale: 10);
        await customLocale.AutoSeedAsync(seed: 42, scale: 10, options: new AutoSeedOptions(Locale: "pt_BR"));

        List<string> defaultNames = await defaultLocale.Customers.OrderBy(customer => customer.Id).Select(customer => customer.FirstName).ToListAsync();
        List<string> customNames = await customLocale.Customers.OrderBy(customer => customer.Id).Select(customer => customer.FirstName).ToListAsync();
        List<string> defaultEmails = await defaultLocale.Customers.OrderBy(customer => customer.Id).Select(customer => customer.Email).ToListAsync();
        List<string> customEmails = await customLocale.Customers.OrderBy(customer => customer.Id).Select(customer => customer.Email).ToListAsync();

        Assert.NotEqual(defaultNames, customNames);
        Assert.NotEqual(defaultEmails, customEmails);
    }

    [Fact]
    public async Task AutoSeedAsync_WithACustomLocale_IsStillDeterministicForTheSameSeed()
    {
        using CustomerOrderContext first = NewContext();
        using CustomerOrderContext second = NewContext();
        AutoSeedOptions options = new(Locale: "pt_BR");

        await first.AutoSeedAsync(seed: 42, scale: 10, options: options);
        await second.AutoSeedAsync(seed: 42, scale: 10, options: options);

        List<string> firstNames = await first.Customers.OrderBy(customer => customer.Id).Select(customer => customer.FirstName).ToListAsync();
        List<string> secondNames = await second.Customers.OrderBy(customer => customer.Id).Select(customer => customer.FirstName).ToListAsync();

        Assert.Equal(firstNames, secondNames);
    }

    [Fact]
    public async Task AutoSeedAsync_WithAnOptionalForeignKey_PopulatesItForSomeRowsAndLeavesOthersNull()
    {
        using OptionalForeignKeyContext context = NewOptionalForeignKeyContext();

        await context.AutoSeedAsync(seed: 42, scale: 30, options: new AutoSeedOptions(NullRate: 0.3));

        List<PromoCode> promoCodes = await context.PromoCodes.ToListAsync();
        List<DiscountedOrder> orders = await context.DiscountedOrders.ToListAsync();
        HashSet<int> promoCodeIds = [.. promoCodes.Select(promoCode => promoCode.Id)];

        Assert.Contains(orders, order => order.PromoCodeId is not null);
        Assert.Contains(orders, order => order.PromoCodeId is null);
        Assert.All(orders, order => Assert.True(order.PromoCodeId is null || promoCodeIds.Contains(order.PromoCodeId.Value)));
    }

    [Fact]
    public async Task AutoSeedAsync_WithTableSplitEntityTypes_ThrowsUnsupportedEntityTypeException()
    {
        using TableSplitContext context = new();

        UnsupportedEntityTypeException exception = await Assert.ThrowsAsync<UnsupportedEntityTypeException>(
            () => context.AutoSeedAsync(seed: 42, scale: 10));

        Assert.Contains("SplitPersonDetail", exception.EntityTypeName);
        Assert.Equal(0, await context.People.CountAsync());
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

    [Fact]
    public async Task AutoSeedExplainAsync_WritesNothingToTheDatabase()
    {
        using CustomerOrderContext context = NewContext();

        AutoSeedExplainResult result = await context.AutoSeedExplainAsync(seed: 42, scale: 10);

        Assert.Equal(2, result.Order.Count);
        Assert.Equal(0, await context.Customers.CountAsync());
        Assert.Equal(0, await context.Orders.CountAsync());
    }

    [Fact]
    public async Task AutoSeedExplainAsync_RowCountsMatchWhatAutoSeedAsyncWouldActuallyWrite()
    {
        using CustomerOrderContext explainContext = NewContext();
        using CustomerOrderContext seedContext = NewContext();

        AutoSeedExplainResult explainResult = await explainContext.AutoSeedExplainAsync(seed: 42, scale: 10);
        IReadOnlyDictionary<string, int> seedResult = await seedContext.AutoSeedAsync(seed: 42, scale: 10);

        Assert.Equal(seedResult.Count, explainResult.RowCounts.Count);
        foreach (KeyValuePair<string, int> entry in seedResult)
        {
            Assert.Equal(entry.Value, explainResult.RowCounts[entry.Key]);
        }
    }

    [Fact]
    public async Task AutoSeedExplainAsync_ReportsALongTailChildCountForOrder()
    {
        using CustomerOrderContext context = NewContext();

        AutoSeedExplainResult result = await context.AutoSeedExplainAsync(seed: 42, scale: 10);

        string orderKey = typeof(Order).FullName!;
        Assert.True(result.ChildCountsByEntityType.ContainsKey(orderKey));
        Assert.Equal(10, result.ChildCountsByEntityType[orderKey].Count);
    }

    [Fact]
    public async Task ToReport_MentionsEveryEntityTypeAndItsRowCount()
    {
        using CustomerOrderContext context = NewContext();

        AutoSeedExplainResult result = await context.AutoSeedExplainAsync(seed: 42, scale: 10);
        string report = result.ToReport();

        Assert.Contains("Customer", report);
        Assert.Contains("Order", report);
        Assert.Contains("rows", report);
    }

    [Fact]
    public async Task AutoSeedExplainAsync_WithNullContext_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => DbContextAutoSeedExtensions.AutoSeedExplainAsync(null!, seed: 1, scale: 10));
    }

    [Fact]
    public async Task AutoSeedExplainAsync_WithNonPositiveScale_ThrowsArgumentOutOfRangeException()
    {
        using CustomerOrderContext context = NewContext();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => context.AutoSeedExplainAsync(seed: 1, scale: 0));
    }

    private static CustomerOrderContext NewContext()
    {
        int id = Interlocked.Increment(ref _databaseCounter);
        return new CustomerOrderContext($"{nameof(CustomerOrderContext)}_{id}");
    }

    private static OptionalForeignKeyContext NewOptionalForeignKeyContext()
    {
        int id = Interlocked.Increment(ref _databaseCounter);
        return new OptionalForeignKeyContext($"{nameof(OptionalForeignKeyContext)}_{id}");
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

public sealed class OptionalForeignKeyContext(string databaseName) : DbContext
{
    public DbSet<PromoCode> PromoCodes => Set<PromoCode>();
    public DbSet<DiscountedOrder> DiscountedOrders => Set<DiscountedOrder>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(databaseName);
}

public sealed class PromoCode
{
    public int Id { get; set; }
}

public sealed class DiscountedOrder
{
    public int Id { get; set; }
    public int? PromoCodeId { get; set; }
    public PromoCode? PromoCode { get; set; }
}
