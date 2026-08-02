using EFCore.AutoSeed;
using EFCore.AutoSeed.SchemaTests;
using Microsoft.EntityFrameworkCore;

namespace EFCore.AutoSeed.SchemaTests.Northwind;

public sealed class NorthwindSchemaTests
{
    [Fact]
    public async Task Explain_DoesNotThrowAndCoversEveryEntityType()
    {
        using NorthwindContext context = new(SchemaTestSupport.UniqueDatabaseName());

        AutoSeedExplainResult result = await context.AutoSeedExplainAsync(seed: 42, scale: 200);

        Assert.Contains(typeof(Category).FullName!, result.RowCounts.Keys);
        Assert.Contains(typeof(Product).FullName!, result.RowCounts.Keys);
        Assert.Contains(typeof(Order).FullName!, result.RowCounts.Keys);
        Assert.Contains(typeof(OrderDetail).FullName!, result.RowCounts.Keys);

        string report = result.ToReport();
        Assert.Contains("Category", report);
        Assert.Contains("OrderDetail", report);
    }

    [Fact]
    public async Task AutoSeedAsync_SeedsWithoutViolatingReferentialIntegrity()
    {
        using NorthwindContext context = new(SchemaTestSupport.UniqueDatabaseName());

        await context.AutoSeedAsync(seed: 42, scale: 200);

        SchemaTestSupport.AssertReferentialIntegrityHolds(context);
        Assert.True(await context.OrderDetails.AnyAsync());
        List<Employee> employees = await context.Employees.ToListAsync();
        Assert.True(employees.Count <= 1 || employees.All(employee => employee.ManagerId != employee.Id));
    }

    [Fact]
    public async Task AutoSeedAsync_WithTheSameSeed_ProducesTheSameRowCounts()
    {
        using NorthwindContext first = new(SchemaTestSupport.UniqueDatabaseName());
        using NorthwindContext second = new(SchemaTestSupport.UniqueDatabaseName());

        IReadOnlyDictionary<string, int> firstResult = await first.AutoSeedAsync(seed: 99, scale: 150);
        IReadOnlyDictionary<string, int> secondResult = await second.AutoSeedAsync(seed: 99, scale: 150);

        Assert.Equal(firstResult, secondResult);
    }
}

public sealed class NorthwindContext(string databaseName) : DbContext
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Shipper> Shippers => Set<Shipper>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderDetail> OrderDetails => Set<OrderDetail>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(databaseName);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>().HasIndex(product => product.Sku).IsUnique();
        modelBuilder.Entity<Product>().Property(product => product.UnitPrice).HasPrecision(18, 2);
        modelBuilder.Entity<OrderDetail>().HasKey(detail => new { detail.OrderId, detail.ProductId });
        modelBuilder.Entity<OrderDetail>().Property(detail => detail.UnitPrice).HasPrecision(18, 2);

        modelBuilder.Entity<Employee>()
            .HasOne(employee => employee.Manager)
            .WithMany()
            .HasForeignKey(employee => employee.ManagerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Order>()
            .HasOne(order => order.Shipper)
            .WithMany()
            .HasForeignKey(order => order.ShipperId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<Product> Products { get; set; } = [];
}

public sealed class Supplier
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = "";
    public string ContactEmail { get; set; } = "";
    public string Phone { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public List<Product> Products { get; set; } = [];
}

public sealed class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Sku { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int UnitsInStock { get; set; }
    public bool Discontinued { get; set; }
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
}

public sealed class Shipper
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = "";
    public string Phone { get; set; } = "";
}

public sealed class Customer
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = "";
    public string ContactEmail { get; set; } = "";
    public string Phone { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public List<Order> Orders { get; set; } = [];
}

public sealed class Employee
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTime HireDate { get; set; }
    public int? ManagerId { get; set; }
    public Employee? Manager { get; set; }
}

public sealed class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public int? ShipperId { get; set; }
    public Shipper? Shipper { get; set; }
    public DateTime OrderDate { get; set; }
    public decimal Freight { get; set; }
    public List<OrderDetail> OrderDetails { get; set; } = [];
}

public sealed class OrderDetail
{
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}
