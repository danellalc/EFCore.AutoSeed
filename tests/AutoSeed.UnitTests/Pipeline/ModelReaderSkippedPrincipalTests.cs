using EFCore.AutoSeed.Exceptions;
using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore;

namespace EFCore.AutoSeed.UnitTests.Pipeline;

public sealed class ModelReaderSkippedPrincipalTests
{
    [Fact]
    public void Read_WithARequiredForeignKeyToAnAbstractSkippedPrincipal_ThrowsNamingTheDependentAndThePrincipal()
    {
        using RequiredFkToAbstractContext context = new();

        UnsupportedEntityTypeException exception = Assert.Throws<UnsupportedEntityTypeException>(
            () => new ModelReader().Read(context.Model));

        Assert.Contains(nameof(Comment), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(Document), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_WithANullableForeignKeyToAnAbstractSkippedPrincipal_ReadsTheModelWithNoEdgeAndNoException()
    {
        using NullableFkToAbstractContext context = new();

        ModelReadResult read = new ModelReader().Read(context.Model);

        Assert.Contains(read.EntityTypes, entityType => entityType.ClrType == typeof(NullableComment));
        Assert.DoesNotContain(read.EntityTypes, entityType => entityType.ClrType == typeof(Document));
        Assert.DoesNotContain(read.Edges, edge => edge.Dependent.ClrType == typeof(NullableComment));
    }

    private sealed class RequiredFkToAbstractContext : DbContext
    {
        public DbSet<Report> Reports => Set<Report>();
        public DbSet<Comment> Comments => Set<Comment>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Document>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(nameof(RequiredFkToAbstractContext));
    }

    private sealed class NullableFkToAbstractContext : DbContext
    {
        public DbSet<Report> Reports => Set<Report>();
        public DbSet<NullableComment> Comments => Set<NullableComment>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Document>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(nameof(NullableFkToAbstractContext));
    }

    private abstract class Document
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
    }

    private sealed class Report : Document
    {
        public string Body { get; set; } = "";
    }

    private sealed class Comment
    {
        public int Id { get; set; }
        public int DocumentId { get; set; }
        public Document Document { get; set; } = null!;
    }

    private sealed class NullableComment
    {
        public int Id { get; set; }
        public int? DocumentId { get; set; }
        public Document? Document { get; set; }
    }
}
