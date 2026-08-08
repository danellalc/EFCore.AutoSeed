using EFCore.AutoSeed.Coverage;
using EFCore.AutoSeed.Exceptions;
using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.UnitTests.Coverage;

public sealed class CoverageValueGeneratorTests
{
    [Fact]
    public void GenerateRow_LeavesEveryNullablePropertyAbsentOnTheFirstRow()
    {
        using CoverageFixtureContext context = new();
        IEntityType entityType = GetEntityType(context);
        CoverageValueGenerator generator = new();

        Dictionary<string, object> firstRow = generator.GenerateRow(entityType, SeededRandom.FromRootSeed(1));

        Assert.False(firstRow.ContainsKey(nameof(Widget.Nickname)));
        Assert.True(firstRow.ContainsKey(nameof(Widget.Name)));
    }

    [Fact]
    public void GenerateRow_PopulatesNullablePropertiesFromTheSecondRowOnward()
    {
        using CoverageFixtureContext context = new();
        IEntityType entityType = GetEntityType(context);
        CoverageValueGenerator generator = new();

        generator.GenerateRow(entityType, SeededRandom.FromRootSeed(1));
        Dictionary<string, object> secondRow = generator.GenerateRow(entityType, SeededRandom.FromRootSeed(1));

        Assert.True(secondRow.ContainsKey(nameof(Widget.Nickname)));
    }

    [Fact]
    public void GenerateRow_CyclesStringsThroughEmptyOneCharacterAndMaxLength()
    {
        using CoverageFixtureContext context = new();
        IEntityType entityType = GetEntityType(context);
        CoverageValueGenerator generator = new();

        HashSet<int> lengths = [];
        for (int row = 0; row < 4; row++)
        {
            Dictionary<string, object> values = generator.GenerateRow(entityType, SeededRandom.FromRootSeed(1));
            lengths.Add(((string)values[nameof(Widget.Name)]).Length);
        }

        Assert.Contains(0, lengths);
        Assert.Contains(1, lengths);
        Assert.Contains(10, lengths);
    }

    [Fact]
    public void GenerateRow_CyclesThroughEveryEnumValue()
    {
        using CoverageFixtureContext context = new();
        IEntityType entityType = GetEntityType(context);
        CoverageValueGenerator generator = new();

        HashSet<WidgetStatus> statuses = [];
        for (int row = 0; row < 5; row++)
        {
            Dictionary<string, object> values = generator.GenerateRow(entityType, SeededRandom.FromRootSeed(1));
            statuses.Add((WidgetStatus)values[nameof(Widget.Status)]);
        }

        Assert.Equal(Enum.GetValues<WidgetStatus>().Length, statuses.Count);
    }

    [Fact]
    public void GenerateRow_AlternatesBooleanValues()
    {
        using CoverageFixtureContext context = new();
        IEntityType entityType = GetEntityType(context);
        CoverageValueGenerator generator = new();

        HashSet<bool> flags = [];
        for (int row = 0; row < 3; row++)
        {
            Dictionary<string, object> values = generator.GenerateRow(entityType, SeededRandom.FromRootSeed(1));
            flags.Add((bool)values[nameof(Widget.IsActive)]);
        }

        Assert.Equal(2, flags.Count);
    }

    [Fact]
    public void GenerateRow_NeverSetsThePrimaryKeyOrAForeignKey()
    {
        using CoverageFixtureContext context = new();
        IEntityType entityType = context.Model.FindEntityType(typeof(Gadget))
            ?? throw new InvalidOperationException("Gadget entity type not found.");
        CoverageValueGenerator generator = new();

        Dictionary<string, object> values = generator.GenerateRow(entityType, SeededRandom.FromRootSeed(1));

        Assert.False(values.ContainsKey(nameof(Gadget.Id)));
        Assert.False(values.ContainsKey(nameof(Gadget.WidgetId)));
    }

    [Fact]
    public void GenerateRow_WithTheSameSeed_IsDeterministic()
    {
        using CoverageFixtureContext firstContext = new();
        using CoverageFixtureContext secondContext = new();
        IEntityType firstEntityType = GetEntityType(firstContext);
        IEntityType secondEntityType = GetEntityType(secondContext);

        CoverageValueGenerator firstGenerator = new();
        CoverageValueGenerator secondGenerator = new();

        firstGenerator.GenerateRow(firstEntityType, SeededRandom.FromRootSeed(1));
        secondGenerator.GenerateRow(secondEntityType, SeededRandom.FromRootSeed(1));

        Dictionary<string, object> firstSecondRow = firstGenerator.GenerateRow(firstEntityType, SeededRandom.FromRootSeed(1));
        Dictionary<string, object> secondSecondRow = secondGenerator.GenerateRow(secondEntityType, SeededRandom.FromRootSeed(1));

        Assert.Equal(firstSecondRow, secondSecondRow);
    }

    [Fact]
    public void GenerateRow_WithNullArguments_ThrowsArgumentNullException()
    {
        using CoverageFixtureContext context = new();
        IEntityType entityType = GetEntityType(context);

        Assert.Throws<ArgumentNullException>(() => new CoverageValueGenerator().GenerateRow(null!, SeededRandom.FromRootSeed(1)));
        Assert.Throws<ArgumentNullException>(() => new CoverageValueGenerator().GenerateRow(entityType, null!));
    }

    [Fact]
    public void ValidateRequiredProperties_WithoutAnyComplexProperty_DoesNotThrow()
    {
        using CoverageFixtureContext context = new();
        IEntityType entityType = GetEntityType(context);

        Exception? exception = Record.Exception(() => CoverageValueGenerator.ValidateRequiredProperties([entityType]));

        Assert.Null(exception);
    }

    [Fact]
    public void ValidateRequiredProperties_WithARequiredComplexProperty_ThrowsUnsupportedPropertyException()
    {
        using ComplexPropertyFixtureContext context = new();
        IEntityType entityType = context.Model.FindEntityType(typeof(Invoice))
            ?? throw new InvalidOperationException("Invoice entity type not found.");

        UnsupportedPropertyException exception = Assert.Throws<UnsupportedPropertyException>(
            () => CoverageValueGenerator.ValidateRequiredProperties([entityType]));

        Assert.Equal("Total", exception.PropertyName);
    }

    [Fact]
    public void ValidateRequiredProperties_WithNullEntityTypes_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => CoverageValueGenerator.ValidateRequiredProperties(null!));
    }

    private static IEntityType GetEntityType(CoverageFixtureContext context) =>
        context.Model.FindEntityType(typeof(Widget)) ?? throw new InvalidOperationException("Widget entity type not found.");
}

public sealed class ComplexPropertyFixtureContext : DbContext
{
    public DbSet<Invoice> Invoices => Set<Invoice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Invoice>().ComplexProperty(invoice => invoice.Total);

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(nameof(ComplexPropertyFixtureContext));
}

public sealed class Invoice
{
    public int Id { get; set; }
    public Money Total { get; set; }
}

public readonly record struct Money(decimal Amount, string Currency);

public sealed class CoverageFixtureContext : DbContext
{
    public DbSet<Widget> Widgets => Set<Widget>();
    public DbSet<Gadget> Gadgets => Set<Gadget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Widget>().Property(widget => widget.Name).HasMaxLength(10);

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(nameof(CoverageFixtureContext));
}

public enum WidgetStatus
{
    Pending,
    Active,
    Retired,
}

public sealed class Widget
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Nickname { get; set; }
    public bool IsActive { get; set; }
    public WidgetStatus Status { get; set; }
}

public sealed class Gadget
{
    public int Id { get; set; }
    public int WidgetId { get; set; }
    public Widget Widget { get; set; } = null!;
}
