using EFCore.AutoSeed.Exceptions;
using EFCore.AutoSeed.Inference;
using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore;

namespace EFCore.AutoSeed.UnitTests.Pipeline;

public sealed class TableSplittingGuardTests
{
    [Fact]
    public void EnsureNoTableSplitting_WithTwoEntityTypesMappedToTheSameTable_ThrowsUnsupportedEntityTypeException()
    {
        using TableSplitContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);

        UnsupportedEntityTypeException exception = Assert.Throws<UnsupportedEntityTypeException>(
            () => TableSplittingGuard.EnsureNoTableSplitting(read.Edges));

        Assert.Contains("SplitPersonDetail", exception.EntityTypeName);
        Assert.Contains("SplitPerson", exception.Message);
    }

    [Fact]
    public void EnsureNoTableSplitting_WithASharedPrimaryKeyAcrossSeparateTables_DoesNotThrow()
    {
        using SharedPrimaryKeySeparateTablesContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);

        Exception? exception = Record.Exception(() => TableSplittingGuard.EnsureNoTableSplitting(read.Edges));

        Assert.Null(exception);
    }

    [Fact]
    public void EnsureNoTableSplitting_WithNoSharedPrimaryKeyEdges_DoesNotThrow()
    {
        using OrdinaryForeignKeyContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);

        Exception? exception = Record.Exception(() => TableSplittingGuard.EnsureNoTableSplitting(read.Edges));

        Assert.Null(exception);
    }

    [Fact]
    public void EnsureNoTableSplitting_WithNullEdges_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => TableSplittingGuard.EnsureNoTableSplitting(null!));
    }
}

public sealed class TableSplitContext : DbContext
{
    public DbSet<SplitPerson> People => Set<SplitPerson>();
    public DbSet<SplitPersonDetail> SplitPersonDetails => Set<SplitPersonDetail>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SplitPersonDetail>().HasKey(detail => detail.SplitPersonId);

        modelBuilder.Entity<SplitPerson>()
            .HasOne(person => person.Detail)
            .WithOne(detail => detail.SplitPerson)
            .HasForeignKey<SplitPersonDetail>(detail => detail.SplitPersonId);

        modelBuilder.Entity<SplitPerson>().ToTable("People");
        modelBuilder.Entity<SplitPersonDetail>().ToTable("People");
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(nameof(TableSplitContext));
}

public sealed class SplitPerson
{
    public int Id { get; set; }
    public SplitPersonDetail? Detail { get; set; }
}

public sealed class SplitPersonDetail
{
    public int SplitPersonId { get; set; }
    public SplitPerson SplitPerson { get; set; } = null!;
    public string Biography { get; set; } = "";
}

public sealed class SharedPrimaryKeySeparateTablesContext : DbContext
{
    public DbSet<Instructor> Instructors => Set<Instructor>();
    public DbSet<OfficeAssignment> OfficeAssignments => Set<OfficeAssignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OfficeAssignment>().HasKey(assignment => assignment.InstructorId);

        modelBuilder.Entity<Instructor>()
            .HasOne(instructor => instructor.OfficeAssignment)
            .WithOne(assignment => assignment.Instructor)
            .HasForeignKey<OfficeAssignment>(assignment => assignment.InstructorId);

        modelBuilder.Entity<Instructor>().ToTable("Instructors");
        modelBuilder.Entity<OfficeAssignment>().ToTable("OfficeAssignments");
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(nameof(SharedPrimaryKeySeparateTablesContext));
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
    public string Location { get; set; } = "";
}

public sealed class OrdinaryForeignKeyContext : DbContext
{
    public DbSet<Blog> Blogs => Set<Blog>();
    public DbSet<Post> Posts => Set<Post>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(nameof(OrdinaryForeignKeyContext));
}

public sealed class Blog
{
    public int Id { get; set; }
    public List<Post> Posts { get; set; } = [];
}

public sealed class Post
{
    public int Id { get; set; }
    public int BlogId { get; set; }
    public Blog Blog { get; set; } = null!;
}
