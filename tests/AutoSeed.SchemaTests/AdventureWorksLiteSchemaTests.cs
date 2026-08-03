using EFCore.AutoSeed;
using EFCore.AutoSeed.SchemaTests;
using Microsoft.EntityFrameworkCore;

namespace EFCore.AutoSeed.SchemaTests.AdventureWorksLite;

public sealed class AdventureWorksLiteSchemaTests
{
    [Fact]
    public async Task Explain_DoesNotThrowAndCoversEveryEntityType()
    {
        using AdventureWorksLiteContext context = new(SchemaTestSupport.UniqueDatabaseName());

        AutoSeedExplainResult result = await context.AutoSeedExplainAsync(seed: 42, scale: 200);

        Assert.Contains(typeof(ProductCategory).FullName!, result.RowCounts.Keys);
        Assert.Contains(typeof(Product).FullName!, result.RowCounts.Keys);
        Assert.Contains(typeof(SalesOrderHeader).FullName!, result.RowCounts.Keys);
        Assert.Contains(typeof(SalesOrderDetail).FullName!, result.RowCounts.Keys);

        string report = result.ToReport();
        Assert.Contains("ProductCategory", report);
        Assert.Contains("SalesOrderDetail", report);
    }

    [Fact]
    public async Task AutoSeedAsync_SeedsWithoutViolatingReferentialIntegrity()
    {
        using AdventureWorksLiteContext context = new(SchemaTestSupport.UniqueDatabaseName());

        await context.AutoSeedAsync(seed: 42, scale: 200);

        SchemaTestSupport.AssertReferentialIntegrityHolds(context);
        Assert.True(await context.SalesOrderDetails.AnyAsync());

        List<ProductCategory> categories = await context.ProductCategories.ToListAsync();
        Assert.True(categories.Count <= 1 || categories.All(category => category.ParentProductCategoryId != category.Id));

        List<string> productNumbers = await context.Products.Select(product => product.ProductNumber).ToListAsync();
        Assert.Equal(productNumbers.Count, productNumbers.Distinct(StringComparer.Ordinal).Count());

        List<int> orderQuantities = await context.SalesOrderDetails.Select(detail => detail.OrderQty).ToListAsync();
        Assert.Contains(orderQuantities, quantity => quantity > 0);
        List<bool> discontinuedFlags = await context.Products.Select(product => product.Discontinued).ToListAsync();
        Assert.Contains(discontinuedFlags, flag => flag);
        Assert.Contains(discontinuedFlags, flag => !flag);
    }

    [Fact]
    public async Task AutoSeedAsync_WithTheSameSeed_ProducesTheSameRowCounts()
    {
        using AdventureWorksLiteContext first = new(SchemaTestSupport.UniqueDatabaseName());
        using AdventureWorksLiteContext second = new(SchemaTestSupport.UniqueDatabaseName());

        IReadOnlyDictionary<string, int> firstResult = await first.AutoSeedAsync(seed: 99, scale: 150);
        IReadOnlyDictionary<string, int> secondResult = await second.AutoSeedAsync(seed: 99, scale: 150);

        Assert.Equal(firstResult, secondResult);
    }
}

public sealed class AdventureWorksLiteContext(string databaseName) : DbContext
{
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<ProductSubcategory> ProductSubcategories => Set<ProductSubcategory>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<SalesOrderHeader> SalesOrderHeaders => Set<SalesOrderHeader>();
    public DbSet<SalesOrderDetail> SalesOrderDetails => Set<SalesOrderDetail>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(databaseName);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>().HasIndex(product => product.ProductNumber).IsUnique();
        modelBuilder.Entity<Product>().Property(product => product.ListPrice).HasPrecision(18, 2);
        modelBuilder.Entity<SalesOrderHeader>().Property(order => order.TotalDue).HasPrecision(18, 2);
        modelBuilder.Entity<SalesOrderDetail>().Property(detail => detail.UnitPrice).HasPrecision(18, 2);

        modelBuilder.Entity<ProductCategory>()
            .HasOne(category => category.ParentProductCategory)
            .WithMany()
            .HasForeignKey(category => category.ParentProductCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<SalesOrderHeader>()
            .HasOne(order => order.ShipToAddress)
            .WithMany()
            .HasForeignKey(order => order.ShipToAddressId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ProductCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? ParentProductCategoryId { get; set; }
    public ProductCategory? ParentProductCategory { get; set; }
}

public sealed class ProductSubcategory
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int ProductCategoryId { get; set; }
    public ProductCategory ProductCategory { get; set; } = null!;
    public List<Product> Products { get; set; } = [];
}

public sealed class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string ProductNumber { get; set; } = "";
    public decimal ListPrice { get; set; }
    public bool Discontinued { get; set; }
    public int? ProductSubcategoryId { get; set; }
    public ProductSubcategory? ProductSubcategory { get; set; }
}

public sealed class Address
{
    public int Id { get; set; }
    public string AddressLine { get; set; } = "";
    public string City { get; set; } = "";
    public string PostalCode { get; set; } = "";
}

public sealed class Customer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public List<SalesOrderHeader> Orders { get; set; } = [];
}

public sealed class SalesOrderHeader
{
    public int Id { get; set; }
    public DateTime OrderDate { get; set; }
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public int ShipToAddressId { get; set; }
    public Address ShipToAddress { get; set; } = null!;
    public decimal TotalDue { get; set; }
    public List<SalesOrderDetail> Details { get; set; } = [];
}

public sealed class SalesOrderDetail
{
    public int Id { get; set; }
    public int SalesOrderHeaderId { get; set; }
    public SalesOrderHeader SalesOrderHeader { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public decimal UnitPrice { get; set; }
    public int OrderQty { get; set; }
}
