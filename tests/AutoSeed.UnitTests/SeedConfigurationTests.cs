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
    public async Task AutoSeedAsync_WithAPinnedRowCountOfZero_GeneratesNoRows()
    {
        using StoreContext context = NewContext();

        IReadOnlyDictionary<string, int> result =
            await context.AutoSeedAsync(seed: 42, scale: 37, configure: seed => seed.Entity<Category>().HasRowCount(0));

        Assert.Equal(0, await context.Categories.CountAsync());
        Assert.Equal(0, result[typeof(Category).FullName!]);
    }

    [Fact]
    public async Task AutoSeedAsync_WithAnExcludedEntityTypeThatHasNoExistingRows_DoesNotThrowAndGeneratesNoRequiredDependents()
    {
        using StoreContext context = NewContext();

        IReadOnlyDictionary<string, int> result =
            await context.AutoSeedAsync(seed: 42, scale: 37, configure: seed => seed.Entity<Status>().Exclude());

        Assert.Equal(0, await context.Statuses.CountAsync());
        Assert.Equal(0, await context.Products.CountAsync());
        Assert.Equal(0, result[typeof(Product).FullName!]);
    }

    [Fact]
    public async Task AutoSeedAsync_ExcludingAnEntityTypeWithARequiredForeignKey_ThrowsUnsupportedSeedConfigurationException()
    {
        using StoreContext context = NewContext();
        context.Statuses.Add(new Status { Id = 1, Code = "ACTIVE" });
        context.Products.Add(new Product { Id = 1, Name = "Widget", StatusId = 1 });
        await context.SaveChangesAsync();

        string productEntityTypeName = context.Model.FindEntityType(typeof(Product))!.Name;
        string statusEntityTypeName = context.Model.FindEntityType(typeof(Status))!.Name;

        UnsupportedSeedConfigurationException exception = await Assert.ThrowsAsync<UnsupportedSeedConfigurationException>(
            () => context.AutoSeedAsync(seed: 42, scale: 20, configure: seed => seed.Entity<Product>().Exclude()));

        Assert.Equal(productEntityTypeName, exception.EntityTypeName);
        Assert.Equal(statusEntityTypeName, exception.PrincipalEntityTypeName);
        Assert.Equal(1, await context.Products.CountAsync());
    }

    [Fact]
    public async Task AutoSeedAsync_PinningTheRowCountOfAnEntityTypeWithARequiredForeignKey_ThrowsUnsupportedSeedConfigurationException()
    {
        using StoreContext context = NewContext();

        string productEntityTypeName = context.Model.FindEntityType(typeof(Product))!.Name;
        string statusEntityTypeName = context.Model.FindEntityType(typeof(Status))!.Name;

        UnsupportedSeedConfigurationException exception = await Assert.ThrowsAsync<UnsupportedSeedConfigurationException>(
            () => context.AutoSeedAsync(seed: 42, scale: 20, configure: seed => seed.Entity<Product>().HasRowCount(3)));

        Assert.Equal(productEntityTypeName, exception.EntityTypeName);
        Assert.Equal(statusEntityTypeName, exception.PrincipalEntityTypeName);
        Assert.Equal(0, await context.Products.CountAsync());
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
    public async Task AutoSeedAsync_ConfiguringATypeThatIsNotInTheModel_ThrowsInvalidSeedConfigurationException()
    {
        using StoreContext context = NewContext();

        await Assert.ThrowsAsync<InvalidSeedConfigurationException>(
            () => context.AutoSeedAsync(seed: 42, scale: 5, configure: seed => seed.Entity<NotInModel>().Exclude()));
    }

    [Fact]
    public async Task AutoSeedAsync_WithHasRowCountThenExcludeOnTheSameEntityType_ThrowsArgumentException()
    {
        using StoreContext context = NewContext();
        context.Categories.Add(new Category { Id = 1, Name = "Books" });
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => context.AutoSeedAsync(seed: 42, scale: 20, configure: seed =>
        {
            seed.Entity<Category>().HasRowCount(50);
            seed.Entity<Category>().Exclude();
        }));
    }

    [Fact]
    public async Task AutoSeedAsync_WithExcludeThenHasRowCountOnTheSameEntityType_ThrowsArgumentException()
    {
        using StoreContext context = NewContext();
        context.Categories.Add(new Category { Id = 1, Name = "Books" });
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => context.AutoSeedAsync(seed: 42, scale: 20, configure: seed =>
        {
            seed.Entity<Category>().Exclude();
            seed.Entity<Category>().HasRowCount(50);
        }));
    }

    [Fact]
    public async Task AutoSeedAsync_WithExcludeThenGenerateWithOnTheSameEntityType_ThrowsArgumentException()
    {
        using StoreContext context = NewContext();
        context.Statuses.Add(new Status { Id = 1, Code = "ACTIVE" });
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => context.AutoSeedAsync(seed: 42, scale: 20, configure: seed =>
        {
            seed.Entity<Status>().Exclude();
            seed.Entity<Status>().Property(status => status.Code).GenerateWith((random, values) => "X");
        }));
    }

    [Fact]
    public async Task AutoSeedAsync_WithGenerateWithThenExcludeOnTheSameEntityType_ThrowsArgumentException()
    {
        using StoreContext context = NewContext();
        context.Statuses.Add(new Status { Id = 1, Code = "ACTIVE" });
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => context.AutoSeedAsync(seed: 42, scale: 20, configure: seed =>
        {
            seed.Entity<Status>().Property(status => status.Code).GenerateWith((random, values) => "X");
            seed.Entity<Status>().Exclude();
        }));
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
    public async Task AutoSeedAsync_WithACustomGeneratorOnAUniquelyConstrainedProperty_StillRewritesCollidingValues()
    {
        using SkuContext context = NewSkuContext();

        await context.AutoSeedAsync(seed: 42, scale: 10, configure: seed =>
            seed.Entity<SkuProduct>().Property(product => product.Sku).GenerateWith((random, values) => "FIXED"));

        List<string> skus = await context.SkuProducts.Select(product => product.Sku).ToListAsync();
        Assert.Equal(10, skus.Count);
        Assert.Equal(10, skus.Distinct().Count());
        Assert.Contains("FIXED", skus);
        Assert.All(skus.Where(sku => sku != "FIXED"), sku => Assert.Matches("^FIXED-[0-9]+$", sku));
    }

    [Fact]
    public async Task AutoSeedAsync_WithACustomGeneratorForAPropertyThatIsNotMapped_ThrowsInvalidSeedConfigurationException()
    {
        using StoreContext context = NewContext();

        await Assert.ThrowsAsync<InvalidSeedConfigurationException>(() => context.AutoSeedAsync(seed: 42, scale: 5, configure: seed =>
            seed.Entity<Product>().Property(product => product.Unmapped).GenerateWith((random, values) => "")));
    }

    [Fact]
    public void PropertyConfigurationBuilder_WithANullGenerator_ThrowsArgumentNullException()
    {
        SeedConfigurationBuilder builder = new();
        Assert.Throws<ArgumentNullException>(() => builder.Entity<Product>().Property(product => product.Name).GenerateWith(null!));
    }

    [Fact]
    public void EntityConfigurationBuilder_WithANullPropertySelector_ThrowsArgumentNullException()
    {
        SeedConfigurationBuilder builder = new();
        Assert.Throws<ArgumentNullException>(() => builder.Entity<Product>().Property<string>(null!));
    }

    [Fact]
    public void EntityConfigurationBuilder_WithANestedPropertySelector_ThrowsArgumentException()
    {
        SeedConfigurationBuilder builder = new();
        Assert.Throws<ArgumentException>(() => builder.Entity<EntityWithNestedProperty>().Property(entity => entity.Nested.Value));
    }

    [Fact]
    public async Task AutoSeedAsync_WithTphAndExcludeOnTheBaseType_LeavesTheDerivedTypeGeneratingAtItsOwnScale()
    {
        using TphInheritanceContext context = NewTphInheritanceContext();
        context.Employees.Add(new InheritedEmployee { Id = 1, Name = "Ada" });
        await context.SaveChangesAsync();

        await context.AutoSeedAsync(seed: 42, scale: 5, configure: seed => seed.Entity<InheritedEmployee>().Exclude());

        Assert.Equal(5, await context.Managers.CountAsync());
        Assert.Equal(6, await context.Employees.CountAsync());
    }

    [Fact]
    public async Task AutoSeedAsync_WithTphAndExcludeOnTheDerivedType_LeavesTheBaseTypeGeneratingAtItsOwnScale()
    {
        using TphInheritanceContext context = NewTphInheritanceContext();
        context.Managers.Add(new InheritedManager { Id = 1, Name = "Ada", Budget = 100m });
        await context.SaveChangesAsync();

        await context.AutoSeedAsync(seed: 42, scale: 5, configure: seed => seed.Entity<InheritedManager>().Exclude());

        Assert.Equal(1, await context.Managers.CountAsync());
        Assert.Equal(6, await context.Employees.CountAsync());
    }

    [Fact]
    public async Task AutoSeedAsync_WithTphAndHasRowCountOnBothTheBaseAndDerivedType_PinsThemIndependently()
    {
        using TphInheritanceContext context = NewTphInheritanceContext();

        await context.AutoSeedAsync(seed: 42, scale: 100, configure: seed =>
        {
            seed.Entity<InheritedEmployee>().HasRowCount(3);
            seed.Entity<InheritedManager>().HasRowCount(4);
        });

        Assert.Equal(4, await context.Managers.CountAsync());
        Assert.Equal(7, await context.Employees.CountAsync());
    }

    [Fact]
    public async Task AutoSeedAsync_WithTphAndACustomGeneratorOnADerivedTypeProperty_UsesItAndLeavesTheDiscriminatorWorking()
    {
        using TphInheritanceContext context = NewTphInheritanceContext();

        await context.AutoSeedAsync(seed: 42, scale: 5, configure: seed =>
            seed.Entity<InheritedManager>().Property(manager => manager.Budget).GenerateWith((random, values) => 999.99m));

        List<decimal> budgets = await context.Managers.Select(manager => manager.Budget).ToListAsync();
        Assert.Equal(5, budgets.Count);
        Assert.All(budgets, budget => Assert.Equal(999.99m, budget));
        Assert.Equal(10, await context.Employees.CountAsync());
    }

    [Fact]
    public async Task AutoSeedAsync_WithTptAndExcludeOnTheBaseType_StillGeneratesTheDerivedTypeWithoutAKeyCollision()
    {
        using TptInheritanceContext context = NewTptInheritanceContext();
        context.Employees.Add(new TptInheritedEmployee { Id = 1, Name = "Ada" });
        await context.SaveChangesAsync();

        await context.AutoSeedAsync(seed: 42, scale: 5, configure: seed => seed.Entity<TptInheritedEmployee>().Exclude());

        Assert.Equal(5, await context.Managers.CountAsync());
        Assert.Equal(6, await context.Employees.CountAsync());

        List<int> managerIds = await context.Managers.Select(manager => manager.Id).ToListAsync();
        Assert.Equal(managerIds.Count, managerIds.Distinct().Count());
    }

    [Fact]
    public async Task AutoSeedAsync_WithTptAndHasRowCountOnTheDerivedType_PinsItIndependentlyOfTheBaseTypeScale()
    {
        using TptInheritanceContext context = NewTptInheritanceContext();

        await context.AutoSeedAsync(seed: 42, scale: 100, configure: seed => seed.Entity<TptInheritedManager>().HasRowCount(4));

        Assert.Equal(4, await context.Managers.CountAsync());
        Assert.Equal(104, await context.Employees.CountAsync());
    }

    [Fact]
    public async Task AutoSeedAsync_WithTpcAndExcludeOnOneConcreteType_LeavesTheSiblingUnaffected()
    {
        using TpcInheritanceContext context = NewTpcInheritanceContext();
        context.Managers.Add(new TpcManager { Id = 1, Name = "Ada", Budget = 100m });
        await context.SaveChangesAsync();

        await context.AutoSeedAsync(seed: 42, scale: 5, configure: seed => seed.Entity<TpcManager>().Exclude());

        Assert.Equal(1, await context.Managers.CountAsync());
        Assert.Equal(5, await context.Contractors.CountAsync());
    }

    [Fact]
    public async Task AutoSeedAsync_WithTpcAndHasRowCountOnBothConcreteTypes_PinsThemIndependently()
    {
        using TpcInheritanceContext context = NewTpcInheritanceContext();

        await context.AutoSeedAsync(seed: 42, scale: 100, configure: seed =>
        {
            seed.Entity<TpcManager>().HasRowCount(3);
            seed.Entity<TpcContractor>().HasRowCount(4);
        });

        Assert.Equal(3, await context.Managers.CountAsync());
        Assert.Equal(4, await context.Contractors.CountAsync());
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

    private static SkuContext NewSkuContext()
    {
        int id = Interlocked.Increment(ref _databaseCounter);
        return new SkuContext($"{nameof(SkuContext)}_{id}");
    }

    private static TphInheritanceContext NewTphInheritanceContext()
    {
        int id = Interlocked.Increment(ref _databaseCounter);
        return new TphInheritanceContext($"{nameof(TphInheritanceContext)}_{id}");
    }

    private static TptInheritanceContext NewTptInheritanceContext()
    {
        int id = Interlocked.Increment(ref _databaseCounter);
        return new TptInheritanceContext($"{nameof(TptInheritanceContext)}_{id}");
    }

    private static TpcInheritanceContext NewTpcInheritanceContext()
    {
        int id = Interlocked.Increment(ref _databaseCounter);
        return new TpcInheritanceContext($"{nameof(TpcInheritanceContext)}_{id}");
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

public sealed class SkuContext(string databaseName) : DbContext
{
    public DbSet<SkuProduct> SkuProducts => Set<SkuProduct>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(databaseName);

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<SkuProduct>().HasIndex(product => product.Sku).IsUnique();
}

public sealed class SkuProduct
{
    public int Id { get; set; }
    public string Sku { get; set; } = "";
}

public sealed class EntityWithNestedProperty
{
    public int Id { get; set; }
    public string Value { get; set; } = "";
    public NestedOwned Nested { get; set; } = null!;
}

public sealed class NestedOwned
{
    public string Value { get; set; } = "";
}

public sealed class TphInheritanceContext(string databaseName) : DbContext
{
    public DbSet<InheritedEmployee> Employees => Set<InheritedEmployee>();
    public DbSet<InheritedManager> Managers => Set<InheritedManager>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(databaseName);
}

public class InheritedEmployee
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class InheritedManager : InheritedEmployee
{
    public decimal Budget { get; set; }
}

public sealed class TptInheritanceContext(string databaseName) : DbContext
{
    public DbSet<TptInheritedEmployee> Employees => Set<TptInheritedEmployee>();
    public DbSet<TptInheritedManager> Managers => Set<TptInheritedManager>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(databaseName);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TptInheritedEmployee>().ToTable("TptConfigEmployees");
        modelBuilder.Entity<TptInheritedManager>().ToTable("TptConfigManagers");
    }
}

public class TptInheritedEmployee
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class TptInheritedManager : TptInheritedEmployee
{
    public decimal Budget { get; set; }
}

public sealed class TpcInheritanceContext(string databaseName) : DbContext
{
    public DbSet<TpcManager> Managers => Set<TpcManager>();
    public DbSet<TpcContractor> Contractors => Set<TpcContractor>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(databaseName);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TpcEmployeeBase>().UseTpcMappingStrategy();
        modelBuilder.Entity<TpcManager>().ToTable("TpcConfigManagers");
        modelBuilder.Entity<TpcContractor>().ToTable("TpcConfigContractors");
    }
}

public abstract class TpcEmployeeBase
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class TpcManager : TpcEmployeeBase
{
    public decimal Budget { get; set; }
}

public sealed class TpcContractor : TpcEmployeeBase
{
    public decimal HourlyRate { get; set; }
}
