using Microsoft.EntityFrameworkCore;

namespace EFCore.AutoSeed.UnitTests.Pipeline.Fixtures;

public sealed class LinearChainContext : DbContext
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(nameof(LinearChainContext));
}

public sealed class Customer
{
    public int Id { get; set; }
}

public sealed class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
}

public sealed class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
}

public sealed class DiamondContext : DbContext
{
    public DbSet<Root> Roots => Set<Root>();
    public DbSet<Left> Lefts => Set<Left>();
    public DbSet<Right> Rights => Set<Right>();
    public DbSet<Merge> Merges => Set<Merge>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(nameof(DiamondContext));
}

public sealed class Root
{
    public int Id { get; set; }
}

public sealed class Left
{
    public int Id { get; set; }
    public int RootId { get; set; }
    public Root Root { get; set; } = null!;
}

public sealed class Right
{
    public int Id { get; set; }
    public int RootId { get; set; }
    public Root Root { get; set; } = null!;
}

public sealed class Merge
{
    public int Id { get; set; }
    public int LeftId { get; set; }
    public Left Left { get; set; } = null!;
    public int RightId { get; set; }
    public Right Right { get; set; } = null!;
}

public sealed class NullableSelfCycleContext : DbContext
{
    public DbSet<Employee> Employees => Set<Employee>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(nameof(NullableSelfCycleContext));
}

public sealed class Employee
{
    public int Id { get; set; }
    public int? ManagerId { get; set; }
    public Employee? Manager { get; set; }
}

public sealed class RequiredMutualCycleContext : DbContext
{
    public DbSet<CycleLeft> CycleLefts => Set<CycleLeft>();
    public DbSet<CycleRight> CycleRights => Set<CycleRight>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CycleLeft>()
            .HasOne(left => left.Right)
            .WithMany()
            .HasForeignKey(left => left.RightId)
            .IsRequired();

        modelBuilder.Entity<CycleRight>()
            .HasOne(right => right.Left)
            .WithMany()
            .HasForeignKey(right => right.LeftId)
            .IsRequired();
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(nameof(RequiredMutualCycleContext));
}

public sealed class CycleLeft
{
    public int Id { get; set; }
    public int RightId { get; set; }
    public CycleRight Right { get; set; } = null!;
}

public sealed class CycleRight
{
    public int Id { get; set; }
    public int LeftId { get; set; }
    public CycleLeft Left { get; set; } = null!;
}

public sealed class UnrelatedEntitiesContext : DbContext
{
    public DbSet<Zebra> Zebras => Set<Zebra>();
    public DbSet<Apple> Apples => Set<Apple>();
    public DbSet<Mango> Mangoes => Set<Mango>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(nameof(UnrelatedEntitiesContext));
}

public sealed class Zebra
{
    public int Id { get; set; }
}

public sealed class Apple
{
    public int Id { get; set; }
}

public sealed class Mango
{
    public int Id { get; set; }
}

public sealed class KeylessAndOwnedContext : DbContext
{
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Invoice>().OwnsOne(invoice => invoice.BillingAddress);
        modelBuilder.Entity<AuditLogEntry>().HasNoKey();
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(nameof(KeylessAndOwnedContext));
}

public sealed class Invoice
{
    public int Id { get; set; }
    public Address BillingAddress { get; set; } = null!;
}

public sealed class Address
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
}

public sealed class AuditLogEntry
{
    public string Message { get; set; } = "";
}
