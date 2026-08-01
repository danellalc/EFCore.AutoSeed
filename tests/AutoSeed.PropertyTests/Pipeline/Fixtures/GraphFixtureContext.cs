using Microsoft.EntityFrameworkCore;

namespace EFCore.AutoSeed.PropertyTests.Pipeline.Fixtures;

public sealed class GraphFixtureContext : DbContext
{
    public DbSet<Publisher> Publishers => Set<Publisher>();
    public DbSet<Author> Authors => Set<Author>();
    public DbSet<Book> Books => Set<Book>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Reviewer> Reviewers => Set<Reviewer>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(nameof(GraphFixtureContext));
}

public sealed class Publisher
{
    public int Id { get; set; }
}

public sealed class Author
{
    public int Id { get; set; }
    public int PublisherId { get; set; }
    public Publisher Publisher { get; set; } = null!;
}

public sealed class Book
{
    public int Id { get; set; }
    public int AuthorId { get; set; }
    public Author Author { get; set; } = null!;
    public int PublisherId { get; set; }
    public Publisher Publisher { get; set; } = null!;
}

public sealed class Reviewer
{
    public int Id { get; set; }
}

public sealed class Review
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public Book Book { get; set; } = null!;
    public int ReviewerId { get; set; }
    public Reviewer Reviewer { get; set; } = null!;
}
