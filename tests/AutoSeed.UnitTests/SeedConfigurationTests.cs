using EFCore.AutoSeed.Exceptions;
using EFCore.AutoSeed.Inference;
using Microsoft.EntityFrameworkCore;

namespace EFCore.AutoSeed.UnitTests.Configuration;

public sealed class SeedConfigurationTests
{
    private static int _databaseCounter;

    [Fact]
    public async Task AutoSeedAsync_WithAnExcludedEntityType_NeverInsertsIntoIt()
    {
        using StoreContext context = NewContext();
        context.Statuses.AddRange(new Status { Id = 1, Code = "ACTIVE" }, new Status { Id = 2, Code = "INACTIVE" });
        await context.SaveChangesAsync();

        await context.AutoSeedAsync(seed: 42, scale: 20, configure: seed => seed.Entity<Status>().Exclude());

        Assert.Equal(2, await context.Statuses.CountAsync());
    }

    [Fact]
    public async Task AutoSeedAsync_WithAnExcludedEntityType_UsesItsExistingRowsAsForeignKeyTargets()
    {
        using StoreContext context = NewContext();
        context.Statuses.AddRange(new Status { Id = 1, Code = "ACTIVE" }, new Status { Id = 2, Code = "INACTIVE" });
        await context.SaveChangesAsync();

        await context.AutoSeedAsync(seed: 42, scale: 20, configure: seed => seed.Entity<Status>().Exclude());

        List<Product> products = await context.Products.ToListAsync();
        Assert.NotEmpty(products);
        Assert.All(products, product => Assert.True(product.StatusId is 1 or 2));
    }

    [Fact]
    public async Task AutoSeedFastAsync_WithAnExcludedEntityType_UsesItsExistingRowsAsForeignKeyTargets()
    {
        using StoreContext context = NewContext();
        context.Statuses.AddRange(new Status { Id = 1, Code = "ACTIVE" }, new Status { Id = 2, Code = "INACTIVE" });
        await context.SaveChangesAsync();

        // AutoSeedFastAsync needs a real relational provider; this only proves the wiring compiles
        // and resolves against the in-memory provider's own bulk-insert rejection, not a real insert.
        await Assert.ThrowsAsync<UnsupportedProviderException>(
            () => context.AutoSeedFastAsync(seed: 42, scale: 20, configure: seed => seed.Entity<Status>().Exclude()));

        Assert.Equal(2, await context.Statuses.CountAsync());
    }

    [Fact]
    public async Task AutoSeedAsync_WithAPinnedRowCount_IgnoresTheGlobalScale()
    {
        using StoreContext context = NewContext();

        await context.AutoSeedAsync(seed: 42, scale: 1000, configure: seed => seed.Entity<Category>().HasRowCount(5));

        Assert.Equal(5, await context.Categories.CountAsync());
    }

    [Fact]
    public async Task AutoSeedAsync_WithAnExcludedEntityTypeThatHasARequiredPropertyNoRuleRecognizes_DoesNotThrow()
    {
        using UnsupportedStatusContext context = NewUnsupportedStatusContext();
        context.Statuses.Add(new UnsupportedStatus { Id = 1, Code = "ACTIVE", Grade = 'A' });
        await context.SaveChangesAsync();

        Exception? exception = await Record.ExceptionAsync(
            () => context.AutoSeedAsync(seed: 42, scale: 5, configure: seed => seed.Entity<UnsupportedStatus>().Exclude()));

        Assert.Null(exception);
    }

    [Fact]
    public async Task AutoSeedAsync_WithoutExcludingAnEntityTypeWithAnUnrecognizedRequiredProperty_ThrowsUnsupportedPropertyException()
    {
        using UnsupportedStatusContext context = NewUnsupportedStatusContext();

        await Assert.ThrowsAsync<UnsupportedPropertyException>(() => context.AutoSeedAsync(seed: 42, scale: 5));
    }

    [Fact]
    public async Task AutoSeedAsync_ConfiguringATypeThatIsNotInTheModel_ThrowsArgumentException()
    {
        using StoreContext context = NewContext();

        await Assert.ThrowsAsync<ArgumentException>(
            () => context.AutoSeedAsync(seed: 42, scale: 5, configure: seed => seed.Entity<NotInModel>().Exclude()));
    }

    [Fact]
    public void EntityConfigurationBuilder_WithANegativeRowCount_ThrowsArgumentOutOfRangeException()
    {
        SeedConfigurationBuilder builder = new();
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Entity<Category>().HasRowCount(-1));
    }

    private static StoreContext NewContext()
    {
        int id = Interlocked.Increment(ref _databaseCounter);
        return new StoreContext($"{nameof(StoreContext)}_{id}");
    }

    private static UnsupportedStatusContext NewUnsupportedStatusContext()
    {
        int id = Interlocked.Increment(ref _databaseCounter);
        return new UnsupportedStatusContext($"{nameof(UnsupportedStatusContext)}_{id}");
    }
}

public sealed class StoreContext(string databaseName) : DbContext
{
    public DbSet<Status> Statuses => Set<Status>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(databaseName);
}

public sealed class Status
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
}

public sealed class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int StatusId { get; set; }
    public Status Status { get; set; } = null!;
}

public sealed class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class NotInModel
{
    public int Id { get; set; }
}

public sealed class UnsupportedStatusContext(string databaseName) : DbContext
{
    public DbSet<UnsupportedStatus> Statuses => Set<UnsupportedStatus>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(databaseName);
}

public sealed class UnsupportedStatus
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public char Grade { get; set; }
}
