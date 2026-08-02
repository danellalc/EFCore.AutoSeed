using Microsoft.EntityFrameworkCore;

namespace EFCore.AutoSeed.UnitTests.Pipeline.Fixtures;

public sealed class UniquenessFixtureContext : DbContext
{
    public DbSet<Widget> Widgets => Set<Widget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Widget>().HasIndex(widget => widget.Code).IsUnique();
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(nameof(UniquenessFixtureContext));
}

public sealed class Widget
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
}
