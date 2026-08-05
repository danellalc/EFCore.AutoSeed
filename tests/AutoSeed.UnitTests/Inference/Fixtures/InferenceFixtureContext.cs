using Microsoft.EntityFrameworkCore;

namespace EFCore.AutoSeed.UnitTests.Inference.Fixtures;

public sealed class InferenceFixtureContext : DbContext
{
    public DbSet<Person> People => Set<Person>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Person>(entity =>
        {
            entity.Property(person => person.Cpf).HasMaxLength(11);
            entity.Property(person => person.Cnpj).HasMaxLength(14);
            entity.Property(person => person.Balance).HasPrecision(8, 2);
            entity.Property(person => person.TinyPrice).HasPrecision(4, 2);
            entity.Property(person => person.EqualScalePrice).HasPrecision(2, 2);
            entity.Property(person => person.HugeTotal).HasPrecision(38, 0);
            entity.Property(person => person.Url).HasMaxLength(15);
            entity.Property(person => person.AvatarThumbnail).HasMaxLength(4);
        });
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(nameof(InferenceFixtureContext));
}

public sealed class Person
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Cpf { get; set; } = "";
    public string Cnpj { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public string Phone { get; set; } = "";
    public decimal Balance { get; set; }
    public decimal TinyPrice { get; set; }
    public decimal EqualScalePrice { get; set; }
    public decimal HugeTotal { get; set; }
    public string Url { get; set; } = "";
    public string Slug { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public string Nickname { get; set; } = "";
    public int VisitCount { get; set; }
    public bool IsActive { get; set; }
    public Guid ExternalId { get; set; }
    public PersonStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateOnly BirthDate { get; set; }
    public TimeOnly PreferredContactTime { get; set; }
    public TimeSpan SessionDuration { get; set; }
    public byte[] Avatar { get; set; } = [];
    public byte[] AvatarThumbnail { get; set; } = [];
}

public enum PersonStatus
{
    Pending,
    Active,
    Suspended,
}
