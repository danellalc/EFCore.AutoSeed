using EFCore.AutoSeed;
using EFCore.AutoSeed.SchemaTests;
using Microsoft.EntityFrameworkCore;

namespace EFCore.AutoSeed.SchemaTests.Chinook;

public sealed class ChinookSchemaTests
{
    [Fact]
    public async Task Explain_DoesNotThrowAndCoversEveryEntityType()
    {
        using ChinookContext context = new(SchemaTestSupport.UniqueDatabaseName());

        AutoSeedExplainResult result = await context.AutoSeedExplainAsync(seed: 42, scale: 200);

        Assert.Contains(typeof(Artist).FullName!, result.RowCounts.Keys);
        Assert.Contains(typeof(Album).FullName!, result.RowCounts.Keys);
        Assert.Contains(typeof(Track).FullName!, result.RowCounts.Keys);
        Assert.Contains(typeof(PlaylistTrack).FullName!, result.RowCounts.Keys);
        Assert.Contains(typeof(InvoiceLine).FullName!, result.RowCounts.Keys);

        string report = result.ToReport();
        Assert.Contains("Artist", report);
        Assert.Contains("PlaylistTrack", report);
    }

    [Fact]
    public async Task AutoSeedAsync_SeedsWithoutViolatingReferentialIntegrity()
    {
        using ChinookContext context = new(SchemaTestSupport.UniqueDatabaseName());

        await context.AutoSeedAsync(seed: 42, scale: 200);

        SchemaTestSupport.AssertReferentialIntegrityHolds(context);
        Assert.True(await context.PlaylistTracks.AnyAsync());
        Assert.True(await context.InvoiceLines.AnyAsync());

        List<Employee> employees = await context.Employees.ToListAsync();
        Assert.True(employees.Count <= 1 || employees.All(employee => employee.ReportsToId != employee.Id));

        List<string> emails = await context.Customers.Select(customer => customer.Email).ToListAsync();
        Assert.Equal(emails.Count, emails.Distinct(StringComparer.Ordinal).Count());

        List<int> durations = await context.Tracks.Select(track => track.Milliseconds).ToListAsync();
        Assert.Contains(durations, duration => duration > 0);
    }

    [Fact]
    public async Task AutoSeedAsync_WithTheSameSeed_ProducesTheSameRowCounts()
    {
        using ChinookContext first = new(SchemaTestSupport.UniqueDatabaseName());
        using ChinookContext second = new(SchemaTestSupport.UniqueDatabaseName());

        IReadOnlyDictionary<string, int> firstResult = await first.AutoSeedAsync(seed: 99, scale: 150);
        IReadOnlyDictionary<string, int> secondResult = await second.AutoSeedAsync(seed: 99, scale: 150);

        Assert.Equal(firstResult, secondResult);
    }
}

public sealed class ChinookContext(string databaseName) : DbContext
{
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<MediaType> MediaTypes => Set<MediaType>();
    public DbSet<Artist> Artists => Set<Artist>();
    public DbSet<Album> Albums => Set<Album>();
    public DbSet<Track> Tracks => Set<Track>();
    public DbSet<Playlist> Playlists => Set<Playlist>();
    public DbSet<PlaylistTrack> PlaylistTracks => Set<PlaylistTrack>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(databaseName);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().HasIndex(customer => customer.Email).IsUnique();
        modelBuilder.Entity<Track>().Property(track => track.UnitPrice).HasPrecision(18, 2);
        modelBuilder.Entity<Invoice>().Property(invoice => invoice.Total).HasPrecision(18, 2);
        modelBuilder.Entity<InvoiceLine>().Property(line => line.UnitPrice).HasPrecision(18, 2);

        modelBuilder.Entity<PlaylistTrack>().HasKey(playlistTrack => new { playlistTrack.PlaylistId, playlistTrack.TrackId });

        modelBuilder.Entity<Employee>()
            .HasOne(employee => employee.ReportsTo)
            .WithMany()
            .HasForeignKey(employee => employee.ReportsToId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Customer>()
            .HasOne(customer => customer.SupportRep)
            .WithMany()
            .HasForeignKey(customer => customer.SupportRepId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class Genre
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<Track> Tracks { get; set; } = [];
}

public sealed class MediaType
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<Track> Tracks { get; set; } = [];
}

public sealed class Artist
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<Album> Albums { get; set; } = [];
}

public sealed class Album
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int ArtistId { get; set; }
    public Artist Artist { get; set; } = null!;
    public List<Track> Tracks { get; set; } = [];
}

public sealed class Track
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Milliseconds { get; set; }
    public int? AlbumId { get; set; }
    public Album? Album { get; set; }
    public int? GenreId { get; set; }
    public Genre? Genre { get; set; }
    public int MediaTypeId { get; set; }
    public MediaType MediaType { get; set; } = null!;
}

public sealed class Playlist
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class PlaylistTrack
{
    public int PlaylistId { get; set; }
    public Playlist Playlist { get; set; } = null!;
    public int TrackId { get; set; }
    public Track Track { get; set; } = null!;
}

public sealed class Employee
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Title { get; set; } = "";
    public int? ReportsToId { get; set; }
    public Employee? ReportsTo { get; set; }
}

public sealed class Customer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public int? SupportRepId { get; set; }
    public Employee? SupportRep { get; set; }
    public List<Invoice> Invoices { get; set; } = [];
}

public sealed class Invoice
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public DateTime InvoiceDate { get; set; }
    public decimal Total { get; set; }
    public List<InvoiceLine> Lines { get; set; } = [];
}

public sealed class InvoiceLine
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;
    public int TrackId { get; set; }
    public Track Track { get; set; } = null!;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}
