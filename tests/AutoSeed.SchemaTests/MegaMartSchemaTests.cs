using EFCore.AutoSeed;
using EFCore.AutoSeed.SchemaTests;
using Microsoft.EntityFrameworkCore;

namespace EFCore.AutoSeed.SchemaTests.MegaMart;

public sealed class MegaMartSchemaTests
{
    [Fact]
    public async Task Explain_DoesNotThrowAndCoversEveryEntityType()
    {
        using MegaMartContext context = new(SchemaTestSupport.UniqueDatabaseName());

        AutoSeedExplainResult result = await context.AutoSeedExplainAsync(seed: 42, scale: 200);

        Assert.Contains(typeof(Company).FullName!, result.RowCounts.Keys);
        Assert.Contains(typeof(Employee).FullName!, result.RowCounts.Keys);
        Assert.Contains(typeof(Supervisor).FullName!, result.RowCounts.Keys);
        Assert.Contains(typeof(Product).FullName!, result.RowCounts.Keys);
        Assert.Contains(typeof(DigitalProduct).FullName!, result.RowCounts.Keys);
        Assert.Contains(typeof(PhysicalProduct).FullName!, result.RowCounts.Keys);
        Assert.Contains(typeof(ProductProfile).FullName!, result.RowCounts.Keys);
        Assert.Contains(typeof(InventoryItem).FullName!, result.RowCounts.Keys);
        Assert.Contains(typeof(OrderLine).FullName!, result.RowCounts.Keys);
        Assert.Contains(typeof(AuditLogEntry).FullName!, result.RowCounts.Keys);

        string report = result.ToReport();
        Assert.Contains("Company", report);
        Assert.Contains("OrderLine", report);
    }

    [Fact]
    public async Task AutoSeedAsync_SeedsWithoutViolatingReferentialIntegrity()
    {
        using MegaMartContext context = new(SchemaTestSupport.UniqueDatabaseName());

        IReadOnlyDictionary<string, int> result = await context.AutoSeedAsync(seed: 42, scale: 200);

        SchemaTestSupport.AssertReferentialIntegrityHolds(context);

        List<Employee> employees = await context.Employees.IgnoreQueryFilters().ToListAsync();
        Assert.True(employees.Count <= 1 || employees.All(employee => employee.ManagerId != employee.Id));
        Assert.Contains(employees, employee => employee is Supervisor);

        List<Product> products = await context.Products.ToListAsync();
        Assert.Contains(products, product => product is DigitalProduct);
        Assert.Contains(products, product => product is PhysicalProduct);
        Assert.All(products, product => Assert.True(product.Dimensions.Length > 0 && product.Dimensions.Width > 0 && product.Dimensions.Height > 0));

        int profileCount = await context.Set<ProductProfile>().CountAsync();
        Assert.Equal(result[typeof(Product).FullName!], profileCount);

        Assert.True(await context.InventoryItems.AnyAsync());
        Assert.True(await context.OrderLines.AnyAsync());
    }

    [Fact]
    public async Task AutoSeedAsync_BiasesEmployeeIsDeletedAndOrderIsCancelledTowardPassingTheirQueryFilters()
    {
        using MegaMartContext context = new(SchemaTestSupport.UniqueDatabaseName());

        await context.AutoSeedAsync(seed: 42, scale: 200);

        List<bool> isDeletedValues = await context.Employees.IgnoreQueryFilters().Select(employee => employee.IsDeleted).ToListAsync();
        int notDeletedCount = isDeletedValues.Count(isDeleted => !isDeleted);
        Assert.True(notDeletedCount > isDeletedValues.Count * 0.7,
            $"expected most employees to pass the query filter, got {notDeletedCount}/{isDeletedValues.Count}.");
        Assert.Contains(isDeletedValues, isDeleted => isDeleted);

        List<bool> isCancelledValues = await context.Orders.IgnoreQueryFilters().Select(order => order.IsCancelled).ToListAsync();
        int notCancelledCount = isCancelledValues.Count(isCancelled => !isCancelled);
        Assert.True(notCancelledCount > isCancelledValues.Count * 0.7,
            $"expected most orders to pass the query filter, got {notCancelledCount}/{isCancelledValues.Count}.");
        Assert.Contains(isCancelledValues, isCancelled => isCancelled);
    }

    [Fact]
    public async Task AutoSeedAsync_WithTheSameSeed_ProducesTheSameRowCountsAndPrimaryKeys()
    {
        using MegaMartContext first = new(SchemaTestSupport.UniqueDatabaseName());
        using MegaMartContext second = new(SchemaTestSupport.UniqueDatabaseName());

        IReadOnlyDictionary<string, int> firstResult = await first.AutoSeedAsync(seed: 99, scale: 150);
        IReadOnlyDictionary<string, int> secondResult = await second.AutoSeedAsync(seed: 99, scale: 150);

        Assert.Equal(firstResult, secondResult);

        List<Guid> firstAuditIds = await first.AuditLogEntries.OrderBy(entry => entry.Id).Select(entry => entry.Id).ToListAsync();
        List<Guid> secondAuditIds = await second.AuditLogEntries.OrderBy(entry => entry.Id).Select(entry => entry.Id).ToListAsync();
        Assert.Equal(firstAuditIds, secondAuditIds);
    }

    [Fact]
    public async Task AutoSeedAsync_CorrelatesOrderLineAmountDueWithUnitPriceQuantityAndDiscount()
    {
        using MegaMartContext context = new(SchemaTestSupport.UniqueDatabaseName());

        await context.AutoSeedAsync(seed: 42, scale: 200);

        List<OrderLine> lines = await context.OrderLines.ToListAsync();
        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.Equal(line.UnitPrice * line.Quantity, line.Total));
        Assert.All(lines, line =>
            Assert.Equal(Math.Round(line.UnitPrice * line.Quantity * (1m - line.Discount), 2), line.AmountDue));
    }
}

public sealed class MegaMartContext(string databaseName) : DbContext
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Supervisor> Supervisors => Set<Supervisor>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(databaseName);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Company>().HasIndex(company => company.Name).IsUnique();

        modelBuilder.Entity<Employee>()
            .HasOne(employee => employee.Manager)
            .WithMany()
            .HasForeignKey(employee => employee.ManagerId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Employee>().HasQueryFilter(employee => !employee.IsDeleted);

        modelBuilder.Entity<Warehouse>().HasIndex(warehouse => warehouse.Code).IsUnique();
        modelBuilder.Entity<Warehouse>().Property(warehouse => warehouse.Code).HasMaxLength(10);

        modelBuilder.Entity<Product>().HasIndex(product => product.Sku).IsUnique();
        modelBuilder.Entity<Product>().OwnsOne(product => product.Dimensions);
        modelBuilder.Entity<Product>().ToTable("Products");
        modelBuilder.Entity<DigitalProduct>().ToTable("DigitalProducts");
        modelBuilder.Entity<PhysicalProduct>().ToTable("PhysicalProducts");
        modelBuilder.Entity<PhysicalProduct>().Property(product => product.WeightKg).HasPrecision(10, 3);

        modelBuilder.Entity<ProductProfile>().HasKey(profile => profile.ProductId);
        modelBuilder.Entity<ProductProfile>().HasIndex(profile => profile.SeoSlug).IsUnique();
        modelBuilder.Entity<Product>()
            .HasOne(product => product.Profile)
            .WithOne(profile => profile.Product)
            .HasForeignKey<ProductProfile>(profile => profile.ProductId);

        modelBuilder.Entity<InventoryItem>().HasKey(item => new { item.WarehouseId, item.ProductId });

        modelBuilder.Entity<Order>().HasQueryFilter(order => !order.IsCancelled);

        modelBuilder.Entity<OrderLine>().HasKey(line => new { line.OrderId, line.ProductId });
        modelBuilder.Entity<OrderLine>().Property(line => line.UnitPrice).HasPrecision(18, 2);
        modelBuilder.Entity<OrderLine>().Property(line => line.Discount).HasPrecision(3, 2);
        modelBuilder.Entity<OrderLine>().Property(line => line.Total).HasPrecision(18, 2);
        modelBuilder.Entity<OrderLine>().Property(line => line.AmountDue).HasPrecision(18, 2);
    }
}

public sealed class Company
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class Employee
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public int? ManagerId { get; set; }
    public Employee? Manager { get; set; }
    public DateTime HireDate { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class Supervisor : Employee
{
    public int TeamSize { get; set; }
}

public sealed class Warehouse
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
}

public enum ProductCategory
{
    Grocery,
    Electronics,
    Apparel,
    HomeAndGarden,
}

public sealed class Dimensions
{
    public decimal Length { get; set; }
    public decimal Width { get; set; }
    public decimal Height { get; set; }
}

public class Product
{
    public int Id { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public ProductCategory Category { get; set; }
    public Dimensions Dimensions { get; set; } = new();
    public ProductProfile? Profile { get; set; }
}

public sealed class DigitalProduct : Product
{
    public string DownloadUrl { get; set; } = "";
    public int FileSizeMb { get; set; }
}

public sealed class PhysicalProduct : Product
{
    public decimal WeightKg { get; set; }
}

public sealed class ProductProfile
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public string SeoSlug { get; set; } = "";
    public string? MetaDescription { get; set; }
}

public sealed class InventoryItem
{
    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int QuantityOnHand { get; set; }
}

public sealed class Order
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public Guid ExternalReference { get; set; }
    public bool IsCancelled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public List<OrderLine> Lines { get; set; } = [];
}

public sealed class OrderLine
{
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }
    public decimal AmountDue { get; set; }
}

public sealed class AuditLogEntry
{
    public Guid Id { get; set; }
    public string EntityName { get; set; } = "";
    public DateTime ChangedAt { get; set; }
}
