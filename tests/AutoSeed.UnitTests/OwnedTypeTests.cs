using Microsoft.EntityFrameworkCore;

namespace EFCore.AutoSeed.UnitTests.Owned;

public sealed class OwnedTypeTests
{
    private static int _databaseCounter;

    [Fact]
    public async Task AutoSeedAsync_PopulatesAnOwnedTypesOwnProperties()
    {
        using CustomerWithAddressContext context = NewContext();

        await context.AutoSeedAsync(seed: 42, scale: 10);

        List<Customer> customers = await context.Customers.ToListAsync();
        Assert.NotEmpty(customers);
        Assert.All(customers, customer => Assert.NotNull(customer.Address));
        Assert.Contains(customers, customer => !string.IsNullOrEmpty(customer.Address.City));
        Assert.Contains(customers, customer => !string.IsNullOrEmpty(customer.Address.PostalCode));
    }

    [Fact]
    public async Task AutoSeedAsync_PopulatesANestedOwnedTypesOwnProperties()
    {
        using CustomerWithAddressContext context = NewContext();

        await context.AutoSeedAsync(seed: 42, scale: 10);

        List<Customer> customers = await context.Customers.ToListAsync();
        Assert.Contains(customers, customer => customer.Address.Coordinates is not null);
    }

    [Fact]
    public async Task AutoSeedAsync_WithTheSameSeed_ProducesTheSameOwnedValues()
    {
        using CustomerWithAddressContext first = NewContext();
        using CustomerWithAddressContext second = NewContext();

        await first.AutoSeedAsync(seed: 7, scale: 10);
        await second.AutoSeedAsync(seed: 7, scale: 10);

        List<string> firstCities = await first.Customers.OrderBy(customer => customer.Id).Select(customer => customer.Address.City).ToListAsync();
        List<string> secondCities = await second.Customers.OrderBy(customer => customer.Id).Select(customer => customer.Address.City).ToListAsync();

        Assert.Equal(firstCities, secondCities);
    }

    private static CustomerWithAddressContext NewContext()
    {
        int id = Interlocked.Increment(ref _databaseCounter);
        return new CustomerWithAddressContext($"{nameof(CustomerWithAddressContext)}_{id}");
    }
}

public sealed class CustomerWithAddressContext(string databaseName) : DbContext
{
    public DbSet<Customer> Customers => Set<Customer>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(databaseName);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().OwnsOne(customer => customer.Address, address =>
        {
            address.OwnsOne(a => a.Coordinates);
        });
    }
}

public sealed class Customer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public Address Address { get; set; } = null!;
}

public sealed class Address
{
    public string City { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public Coordinates? Coordinates { get; set; }
}

public sealed class Coordinates
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}
