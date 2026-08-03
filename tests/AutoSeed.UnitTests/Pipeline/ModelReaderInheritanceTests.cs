using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.UnitTests.Pipeline;

public sealed class ModelReaderInheritanceTests
{
    [Fact]
    public void Read_WithATphHierarchy_KeepsBothTheBaseAndTheDerivedTypeSeedableWithNoEdgeBetweenThem()
    {
        using TphContext context = new();

        ModelReadResult read = new ModelReader().Read(context.Model);

        Assert.Contains(read.EntityTypes, entityType => entityType.ClrType == typeof(Employee));
        Assert.Contains(read.EntityTypes, entityType => entityType.ClrType == typeof(Manager));
        Assert.DoesNotContain(read.Edges, edge => edge.Dependent.ClrType == typeof(Manager) && edge.Principal.ClrType == typeof(Employee));
    }

    [Fact]
    public void Read_WithAnAbstractBaseAndTwoConcreteSiblings_SkipsTheAbstractTypeAndKeepsBothConcreteOnes()
    {
        using AbstractBaseContext context = new();

        ModelReadResult read = new ModelReader().Read(context.Model);

        Assert.DoesNotContain(read.EntityTypes, entityType => entityType.ClrType == typeof(AbstractEmployee));
        Assert.Contains(read.EntityTypes, entityType => entityType.ClrType == typeof(ConcreteManager));
        Assert.Contains(read.EntityTypes, entityType => entityType.ClrType == typeof(ConcreteContractor));
        Assert.Contains(read.SkippedEntityTypes, skipped => skipped.EntityTypeName.EndsWith(nameof(AbstractEmployee), StringComparison.Ordinal));
    }

    [Fact]
    public async Task AutoSeedAsync_WithAnAbstractBaseAndTwoConcreteSiblings_SeedsBothConcreteTypes()
    {
        using AbstractBaseContext context = new();

        IReadOnlyDictionary<string, int> result = await context.AutoSeedAsync(seed: 1, scale: 5);

        Assert.Equal(5, result[typeof(ConcreteManager).FullName!]);
        Assert.Equal(5, result[typeof(ConcreteContractor).FullName!]);
    }

    private sealed class TphContext : DbContext
    {
        public DbSet<Employee> Employees => Set<Employee>();
        public DbSet<Manager> Managers => Set<Manager>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(nameof(TphContext));
    }

    private class Employee
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class Manager : Employee
    {
        public decimal Budget { get; set; }
    }

    private sealed class AbstractBaseContext : DbContext
    {
        public DbSet<ConcreteManager> Managers => Set<ConcreteManager>();
        public DbSet<ConcreteContractor> Contractors => Set<ConcreteContractor>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<AbstractEmployee>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(nameof(AbstractBaseContext));
    }

    private abstract class AbstractEmployee
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class ConcreteManager : AbstractEmployee
    {
        public decimal Budget { get; set; }
    }

    private sealed class ConcreteContractor : AbstractEmployee
    {
        public decimal HourlyRate { get; set; }
    }
}
