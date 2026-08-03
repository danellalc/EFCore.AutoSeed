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

public sealed class DownstreamOfCycleContext : DbContext
{
    public DbSet<LoopA> LoopAs => Set<LoopA>();
    public DbSet<LoopB> LoopBs => Set<LoopB>();
    public DbSet<Downstream> Downstreams => Set<Downstream>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LoopA>().HasOne(a => a.B).WithMany().HasForeignKey(a => a.BId).IsRequired();
        modelBuilder.Entity<LoopB>().HasOne(b => b.A).WithMany().HasForeignKey(b => b.AId).IsRequired();
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(nameof(DownstreamOfCycleContext));
}

public sealed class LoopA
{
    public int Id { get; set; }
    public int BId { get; set; }
    public LoopB B { get; set; } = null!;
}

public sealed class LoopB
{
    public int Id { get; set; }
    public int AId { get; set; }
    public LoopA A { get; set; } = null!;
}

public sealed class Downstream
{
    public int Id { get; set; }
    public int LoopBId { get; set; }
    public LoopB LoopB { get; set; } = null!;
}

public sealed class DisjointCyclesContext : DbContext
{
    public DbSet<PairOneLeft> PairOneLefts => Set<PairOneLeft>();
    public DbSet<PairOneRight> PairOneRights => Set<PairOneRight>();
    public DbSet<PairTwoLeft> PairTwoLefts => Set<PairTwoLeft>();
    public DbSet<PairTwoRight> PairTwoRights => Set<PairTwoRight>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PairOneLeft>().HasOne(left => left.Right).WithMany().HasForeignKey(left => left.RightId).IsRequired();
        modelBuilder.Entity<PairOneRight>().HasOne(right => right.Left).WithMany().HasForeignKey(right => right.LeftId).IsRequired();
        modelBuilder.Entity<PairTwoLeft>().HasOne(left => left.Right).WithMany().HasForeignKey(left => left.RightId).IsRequired();
        modelBuilder.Entity<PairTwoRight>().HasOne(right => right.Left).WithMany().HasForeignKey(right => right.LeftId).IsRequired();
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(nameof(DisjointCyclesContext));
}

public sealed class PairOneLeft
{
    public int Id { get; set; }
    public int RightId { get; set; }
    public PairOneRight Right { get; set; } = null!;
}

public sealed class PairOneRight
{
    public int Id { get; set; }
    public int LeftId { get; set; }
    public PairOneLeft Left { get; set; } = null!;
}

public sealed class PairTwoLeft
{
    public int Id { get; set; }
    public int RightId { get; set; }
    public PairTwoRight Right { get; set; } = null!;
}

public sealed class PairTwoRight
{
    public int Id { get; set; }
    public int LeftId { get; set; }
    public PairTwoLeft Left { get; set; } = null!;
}

public sealed class MultiNullableCandidateCycleContext : DbContext
{
    public DbSet<TriangleX> TriangleXs => Set<TriangleX>();
    public DbSet<TriangleY> TriangleYs => Set<TriangleY>();
    public DbSet<TriangleZ> TriangleZs => Set<TriangleZ>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TriangleX>().HasOne(x => x.Y).WithMany().HasForeignKey(x => x.YId).IsRequired(false);
        modelBuilder.Entity<TriangleY>().HasOne(y => y.Z).WithMany().HasForeignKey(y => y.ZId).IsRequired(false);
        modelBuilder.Entity<TriangleZ>().HasOne(z => z.X).WithMany().HasForeignKey(z => z.XId).IsRequired();
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(nameof(MultiNullableCandidateCycleContext));
}

public sealed class TriangleX
{
    public int Id { get; set; }
    public int? YId { get; set; }
    public TriangleY? Y { get; set; }
}

public sealed class TriangleY
{
    public int Id { get; set; }
    public int? ZId { get; set; }
    public TriangleZ? Z { get; set; }
}

public sealed class TriangleZ
{
    public int Id { get; set; }
    public int XId { get; set; }
    public TriangleX X { get; set; } = null!;
}

public sealed class SharedPrimaryKeyContext : DbContext
{
    public DbSet<Instructor> Instructors => Set<Instructor>();
    public DbSet<OfficeAssignment> OfficeAssignments => Set<OfficeAssignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OfficeAssignment>().HasKey(office => office.InstructorId);
        modelBuilder.Entity<Instructor>()
            .HasOne(instructor => instructor.OfficeAssignment)
            .WithOne(office => office.Instructor)
            .HasForeignKey<OfficeAssignment>(office => office.InstructorId);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(nameof(SharedPrimaryKeyContext));
}

public sealed class Instructor
{
    public int Id { get; set; }
    public OfficeAssignment? OfficeAssignment { get; set; }
}

public sealed class OfficeAssignment
{
    public int InstructorId { get; set; }
    public Instructor Instructor { get; set; } = null!;
}
