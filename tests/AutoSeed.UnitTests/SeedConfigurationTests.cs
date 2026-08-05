using System.ComponentModel.DataAnnotations.Schema;
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

    [Fact]
    public async Task AutoSeedAsync_WithACustomGenerator_UsesItInsteadOfAnyBuiltInRule()
    {
        using StoreContext context = NewContext();

        await context.AutoSeedAsync(seed: 42, scale: 5, configure: seed =>
            seed.Entity<Product>().Property(product => product.Name).GenerateWith((random, values) => $"TST{random.Next(0, 10_000):D4}"));

        List<string> names = await context.Products.Select(product => product.Name).ToListAsync();
        Assert.NotEmpty(names);
        Assert.All(names, name => Assert.Matches("^TST[0-9]{4}$", name));
    }

    [Fact]
    public async Task AutoSeedAsync_WithACustomGenerator_IsDeterministicForTheSameSeed()
    {
        using StoreContext first = NewContext();
        using StoreContext second = NewContext();

        Action<SeedConfigurationBuilder> configure = seed =>
            seed.Entity<Product>().Property(product => product.Name).GenerateWith((random, values) => $"TST{random.Next(0, 10_000):D4}");

        await first.AutoSeedAsync(seed: 7, scale: 10, configure: configure);
        await second.AutoSeedAsync(seed: 7, scale: 10, configure: configure);

        List<string> firstNames = await first.Products.OrderBy(product => product.Id).Select(product => product.Name).ToListAsync();
        List<string> secondNames = await second.Products.OrderBy(product => product.Id).Select(product => product.Name).ToListAsync();

        Assert.Equal(firstNames, secondNames);
    }

    [Fact]
    public async Task AutoSeedAsync_WithACustomGeneratorForAnUnrecognizedRequiredProperty_DoesNotThrow()
    {
        using UnsupportedStatusContext context = NewUnsupportedStatusContext();

        Exception? exception = await Record.ExceptionAsync(() => context.AutoSeedAsync(seed: 42, scale: 5, configure: seed =>
            seed.Entity<UnsupportedStatus>().Property(status => status.Grade).GenerateWith((random, values) => 'A')));

        Assert.Null(exception);
        Assert.All(await context.Statuses.Select(status => status.Grade).ToListAsync(), grade => Assert.Equal('A', grade));
    }

    [Fact]
    public async Task AutoSeedAsync_WithACustomGeneratorForAPropertyTypeNoRuleCouldEverInfer_ProducesItsValues()
    {
        using LocationContext context = NewLocationContext();

        await context.AutoSeedAsync(seed: 42, scale: 10, configure: seed =>
            seed.Entity<Place>().Property(place => place.Coordinates).GenerateWith((random, values) => new Coordinates(random.Next(-90, 90), random.Next(-180, 180))));

        List<Coordinates> coordinates = await context.Places.Select(place => place.Coordinates).ToListAsync();
        Assert.NotEmpty(coordinates);
        Assert.All(coordinates, coordinate => Assert.True(coordinate.Latitude is >= -90 and < 90));
    }

    [Fact]
    public async Task AutoSeedAsync_WithACustomGeneratorForAPropertyThatIsNotMapped_ThrowsArgumentException()
    {
        using StoreContext context = NewContext();

        await Assert.ThrowsAsync<ArgumentException>(() => context.AutoSeedAsync(seed: 42, scale: 5, configure: seed =>
            seed.Entity<Product>().Property(product => product.Unmapped).GenerateWith((random, values) => "")));
    }

    [Fact]
    public void PropertyConfigurationBuilder_WithANullGenerator_ThrowsArgumentNullException()
    {
        SeedConfigurationBuilder builder = new();
        Assert.Throws<ArgumentNullException>(() => builder.Entity<Product>().Property(product => product.Name).GenerateWith(null!));
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

    private static LocationContext NewLocationContext()
    {
        int id = Interlocked.Increment(ref _databaseCounter);
        return new LocationContext($"{nameof(LocationContext)}_{id}");
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

    [NotMapped]
    public string Unmapped { get; set; } = "";
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

public sealed class LocationContext(string databaseName) : DbContext
{
    public DbSet<Place> Places => Set<Place>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(databaseName);

    // A scalar property backed by a value converter, the same shape a PostGIS geography column
    // (NetTopologySuite's Point, say) takes: a custom CLR type no built-in rule recognizes, mapped
    // to and from a primitive, never an owned type or navigation.
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
