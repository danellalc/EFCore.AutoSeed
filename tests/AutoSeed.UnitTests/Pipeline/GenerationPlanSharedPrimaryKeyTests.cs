using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.UnitTests.Pipeline;

public sealed class GenerationPlanSharedPrimaryKeyTests
{
    [Fact]
    public void Plan_ForASharedPrimaryKeyDependentWithAnAlphabeticallyEarlierSecondRequiredEdge_StillForcesExactlyOneRowPerSharedPrincipalRow()
    {
        using SharedPrimaryKeyWithExtraRequiredEdgeContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);
        IReadOnlyList<IEntityType> order = new CycleResolver().Resolve(read.EntityTypes, read.Edges).Order;

        IReadOnlyList<EntityGenerationPlan> plan = new GenerationPlan(meanChildrenPerParent: 3.0)
            .Plan(order, read.Edges, scale: 50, SeededRandom.FromRootSeed(42));

        int personCount = Single(plan, "Person").RowCount;
        EntityGenerationPlan addressPlan = Single(plan, "Address");

        Assert.Equal(personCount, addressPlan.RowCount);
        Assert.NotNull(addressPlan.Driver);
        Assert.Equal("Person", addressPlan.Driver!.Name.Split('.').Last());
        Assert.NotNull(addressPlan.ChildCountsByDriverRow);
        Assert.All(addressPlan.ChildCountsByDriverRow!, count => Assert.Equal(1, count));
    }

    [Fact]
    public void Plan_ForASharedPrimaryKeyDependentWithAnAlphabeticallyEarlierSecondRequiredEdge_WithTheSameInputs_IsDeterministic()
    {
        using SharedPrimaryKeyWithExtraRequiredEdgeContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);
        IReadOnlyList<IEntityType> order = new CycleResolver().Resolve(read.EntityTypes, read.Edges).Order;

        IReadOnlyList<EntityGenerationPlan> first = new GenerationPlan(meanChildrenPerParent: 3.0)
            .Plan(order, read.Edges, scale: 50, SeededRandom.FromRootSeed(42));
        IReadOnlyList<EntityGenerationPlan> second = new GenerationPlan(meanChildrenPerParent: 3.0)
            .Plan(order, read.Edges, scale: 50, SeededRandom.FromRootSeed(42));

        Assert.Equal(first.Select(entry => entry.RowCount), second.Select(entry => entry.RowCount));
        Assert.Equal(
            Single(first, "Address").ChildCountsByDriverRow,
            Single(second, "Address").ChildCountsByDriverRow);
    }

    [Fact]
    public void Plan_ForASharedPrimaryKeyOneToOneWithNoOtherRequiredEdge_StillGivesExactlyOneDependentRowPerPrincipalRow()
    {
        using SharedPrimaryKeyOnlyContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);
        IReadOnlyList<IEntityType> order = new CycleResolver().Resolve(read.EntityTypes, read.Edges).Order;

        IReadOnlyList<EntityGenerationPlan> plan = new GenerationPlan(meanChildrenPerParent: 3.0)
            .Plan(order, read.Edges, scale: 50, SeededRandom.FromRootSeed(42));

        int ownerCount = Single(plan, "Owner").RowCount;
        EntityGenerationPlan profilePlan = Single(plan, "Profile");

        Assert.Equal(ownerCount, profilePlan.RowCount);
        Assert.NotNull(profilePlan.ChildCountsByDriverRow);
        Assert.All(profilePlan.ChildCountsByDriverRow!, count => Assert.Equal(1, count));
    }

    private static EntityGenerationPlan Single(IReadOnlyList<EntityGenerationPlan> plan, string shortName) =>
        Assert.Single(plan, entry => entry.EntityType.Name.EndsWith(shortName, StringComparison.Ordinal));
}

public sealed class SharedPrimaryKeyWithExtraRequiredEdgeContext : DbContext
{
    public DbSet<Person> People => Set<Person>();
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<Address> Addresses => Set<Address>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Address>().HasKey(address => address.PersonId);

        modelBuilder.Entity<Person>()
            .HasOne(person => person.Address)
            .WithOne(address => address.Person)
            .HasForeignKey<Address>(address => address.PersonId);

        modelBuilder.Entity<Address>()
            .HasOne(address => address.Country)
            .WithMany()
            .HasForeignKey(address => address.CountryId)
            .IsRequired();
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(nameof(SharedPrimaryKeyWithExtraRequiredEdgeContext));
}

public sealed class Person
{
    public int Id { get; set; }
    public Address? Address { get; set; }
}

public sealed class Country
{
    public int Id { get; set; }
}

public sealed class Address
{
    public int PersonId { get; set; }
    public Person Person { get; set; } = null!;
    public int CountryId { get; set; }
    public Country Country { get; set; } = null!;
}

public sealed class SharedPrimaryKeyOnlyContext : DbContext
{
    public DbSet<Owner> Owners => Set<Owner>();
    public DbSet<Profile> Profiles => Set<Profile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Profile>().HasKey(profile => profile.OwnerId);

        modelBuilder.Entity<Owner>()
            .HasOne(owner => owner.Profile)
            .WithOne(profile => profile.Owner)
            .HasForeignKey<Profile>(profile => profile.OwnerId);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(nameof(SharedPrimaryKeyOnlyContext));
}

public sealed class Owner
{
    public int Id { get; set; }
    public Profile? Profile { get; set; }
}

public sealed class Profile
{
    public int OwnerId { get; set; }
    public Owner Owner { get; set; } = null!;
}
