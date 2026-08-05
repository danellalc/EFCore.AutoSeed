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

    [Fact]
    public async Task AutoSeedAsync_WithACustomGenerator_UsesItInsteadOfTheBuiltInRuleOnSqlServer()
    {
        DbContextOptions<LookupStoreContext> options = new DbContextOptionsBuilder<LookupStoreContext>().UseSqlServer(_connectionString).Options;

        await using LookupStoreContext context = new(options);
        await context.Database.EnsureCreatedAsync();

        await context.AutoSeedAsync(seed: 42, scale: 10, configure: seed =>
        {
            seed.Entity<Status>().HasRowCount(1);
            seed.Entity<Product>().Property(product => product.Name).GenerateWith((random, values) => $"TST{random.Next(0, 10_000):D4}");
        });

        List<string> names = await context.Products.Select(product => product.Name).ToListAsync();
        Assert.NotEmpty(names);
        Assert.All(names, name => Assert.Matches("^TST[0-9]{4}$", name));
    }

    [Fact]
    public async Task AutoSeedAsync_WithACustomGeneratorForAConvertedPropertyType_ProducesItsValuesOnSqlServer()
    {
        DbContextOptions<LocationContext> options = new DbContextOptionsBuilder<LocationContext>().UseSqlServer(_connectionString).Options;

        await using LocationContext context = new(options);
        await context.Database.EnsureCreatedAsync();

        await context.AutoSeedAsync(seed: 42, scale: 10, configure: seed =>
            seed.Entity<Place>().Property(place => place.Coordinates)
                .GenerateWith((random, values) => new Coordinates(random.Next(-90, 90), random.Next(-180, 180))));

        List<Coordinates> coordinates = await context.Places.Select(place => place.Coordinates).ToListAsync();
        Assert.NotEmpty(coordinates);
        Assert.All(coordinates, coordinate => Assert.True(coordinate.Latitude is >= -90 and < 90));
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

public sealed class LocationContext(DbContextOptions<LocationContext> options) : DbContext(options)
{
    public DbSet<Place> Places => Set<Place>();

    // A scalar property backed by a value converter, the same shape a PostGIS geography column
    // (NetTopologySuite's Point, say) takes: a custom CLR type no built-in rule recognizes.
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Place>().Property(place => place.Coordinates).HasConversion(
            coordinates => $"{coordinates.Latitude}|{coordinates.Longitude}",
            text => ParseCoordinates(text));

    private static Coordinates ParseCoordinates(string text)
    {
        string[] parts = text.Split('|');
        return new Coordinates(double.Parse(parts[0]), double.Parse(parts[1]));
    }
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

public sealed class Place
{
    public int Id { get; set; }
    public Coordinates Coordinates { get; set; } = null!;
}

public sealed class Coordinates
{
    public Coordinates()
    {
    }

    public Coordinates(double latitude, double longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    public double Latitude { get; set; }
    public double Longitude { get; set; }
}
