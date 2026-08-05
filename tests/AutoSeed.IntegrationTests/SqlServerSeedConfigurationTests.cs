using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace EFCore.AutoSeed.IntegrationTests.SqlServer;

[Trait("Category", "Integration")]
public sealed class SqlServerSeedConfigurationTests : IAsyncLifetime
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
    public async Task AutoSeedAsync_WithAnExcludedLookupTable_NeverInsertsIntoItAndReferencesItsExistingRows()
    {
        DbContextOptions<LookupStoreContext> options = new DbContextOptionsBuilder<LookupStoreContext>().UseSqlServer(_connectionString).Options;

        await using LookupStoreContext context = new(options);
        await context.Database.EnsureCreatedAsync();
        context.Statuses.AddRange(new Status { Id = 1, Code = "ACTIVE" }, new Status { Id = 2, Code = "INACTIVE" });
        await context.SaveChangesAsync();

        await context.AutoSeedAsync(seed: 42, scale: 50, configure: seed => seed.Entity<Status>().Exclude());

        Assert.Equal(2, await context.Statuses.CountAsync());

        List<int> productStatusIds = await context.Products.Select(product => product.StatusId).ToListAsync();
        Assert.NotEmpty(productStatusIds);
        Assert.All(productStatusIds, statusId => Assert.True(statusId is 1 or 2));
    }

    [Fact]
    public async Task AutoSeedFastAsync_WithAnExcludedLookupTable_NeverInsertsIntoItAndReferencesItsExistingRows()
    {
        DbContextOptions<LookupStoreContext> options = new DbContextOptionsBuilder<LookupStoreContext>().UseSqlServer(_connectionString).Options;

        await using LookupStoreContext context = new(options);
        await context.Database.EnsureCreatedAsync();
        context.Statuses.AddRange(new Status { Id = 1, Code = "ACTIVE" }, new Status { Id = 2, Code = "INACTIVE" });
        await context.SaveChangesAsync();

        await context.AutoSeedFastAsync(seed: 42, scale: 50, configure: seed => seed.Entity<Status>().Exclude());

        Assert.Equal(2, await context.Statuses.CountAsync());

        List<int> productStatusIds = await context.Products.Select(product => product.StatusId).ToListAsync();
        Assert.NotEmpty(productStatusIds);
        Assert.All(productStatusIds, statusId => Assert.True(statusId is 1 or 2));
    }

    [Fact]
    public async Task AutoSeedAsync_WithAPinnedRowCount_IgnoresTheGlobalScaleOnSqlServer()
    {
        DbContextOptions<LookupStoreContext> options = new DbContextOptionsBuilder<LookupStoreContext>().UseSqlServer(_connectionString).Options;

        await using LookupStoreContext context = new(options);
        await context.Database.EnsureCreatedAsync();

        await context.AutoSeedAsync(seed: 42, scale: 500, configure: seed =>
        {
            seed.Entity<Category>().HasRowCount(7);
            seed.Entity<Status>().HasRowCount(1);
        });

        Assert.Equal(7, await context.Categories.CountAsync());
    }
}

public sealed class LookupStoreContext(DbContextOptions<LookupStoreContext> options) : DbContext(options)
{
    public DbSet<Status> Statuses => Set<Status>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Status>().Property(status => status.Id).ValueGeneratedNever();
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
