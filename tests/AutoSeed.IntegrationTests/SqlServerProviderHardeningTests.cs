using EFCore.AutoSeed.Exceptions;
using EFCore.AutoSeed.Shape;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace EFCore.AutoSeed.IntegrationTests.SqlServer;

[Trait("Category", "Integration")]
public sealed class SqlServerProviderHardeningTests : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder().Build();
    private string _connectionString = "";

    public async Task InitializeAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);
        _connectionString = _container.GetConnectionString();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);

    [Fact]
    public async Task AutoSeedFastAsync_CalledTwiceAgainstTheSameTable_DoesNotCollideWithExistingIdentityValues()
    {
        DbContextOptions<WidgetContext> options = new DbContextOptionsBuilder<WidgetContext>()
            .UseSqlServer(_connectionString)
            .Options;

        await using WidgetContext context = new(options);
        await context.Database.EnsureCreatedAsync();

        IReadOnlyDictionary<string, int> firstResult = await context.AutoSeedFastAsync(seed: 1, scale: 20);
        IReadOnlyDictionary<string, int> secondResult = await context.AutoSeedFastAsync(seed: 2, scale: 15);

        string widgetKey = typeof(Widget).FullName!;
        int totalInserted = firstResult[widgetKey] + secondResult[widgetKey];

        int actualCount = await context.Widgets.CountAsync();
        Assert.Equal(totalInserted, actualCount);

        List<int> ids = await context.Widgets.Select(widget => widget.Id).ToListAsync();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, ids.Count), ids.OrderBy(id => id));
    }

    [Fact]
    public async Task AutoSeedFastAsync_AgainstATableNameContainingABracket_InsertsSuccessfullyInsteadOfCorruptingTheCommand()
    {
        DbContextOptions<BracketNameContext> options = new DbContextOptionsBuilder<BracketNameContext>()
            .UseSqlServer(_connectionString)
            .Options;

        await using BracketNameContext context = new(options);
        await context.Database.EnsureCreatedAsync();

        IReadOnlyDictionary<string, int> result = await context.AutoSeedFastAsync(seed: 42, scale: 10);

        string widgetKey = typeof(OddlyNamedWidget).FullName!;
        Assert.True(result[widgetKey] > 0);
        Assert.Equal(result[widgetKey], await context.Widgets.CountAsync());
    }

    [Fact]
    public async Task CaptureShapeAsync_AgainstATableNameContainingABracket_ReadsTheRealRowCountInsteadOfSilentlyReturningZero()
    {
        DbContextOptions<BracketNameContext> options = new DbContextOptionsBuilder<BracketNameContext>()
            .UseSqlServer(_connectionString)
            .Options;

        await using BracketNameContext context = new(options);
        await context.Database.EnsureCreatedAsync();
        await context.AutoSeedFastAsync(seed: 42, scale: 10);

        ShapeCapture shape = await context.CaptureShapeAsync();

        TableShape widgetShape = Assert.Single(shape.Tables, table => table.EntityTypeName == typeof(OddlyNamedWidget).FullName);
        long actualCount = await context.Widgets.LongCountAsync();
        Assert.Equal(actualCount, widgetShape.RowCount);
        Assert.True(widgetShape.RowCount > 0);
    }

    [Fact]
    public async Task AutoSeedFastAsync_WithAViewMappedEntityType_ThrowsUnsupportedEntityTypeExceptionNamingIt()
    {
        DbContextOptions<ViewMappedContext> options = new DbContextOptionsBuilder<ViewMappedContext>()
            .UseSqlServer(_connectionString)
            .Options;

        await using ViewMappedContext context = new(options);

        UnsupportedEntityTypeException exception = await Assert.ThrowsAsync<UnsupportedEntityTypeException>(
            () => context.AutoSeedFastAsync(seed: 42, scale: 10));

        Assert.Equal(typeof(ViewMappedWidget).FullName, exception.EntityTypeName);
    }

    [Fact]
    public async Task CaptureShapeAsync_WithAViewMappedEntityType_ThrowsUnsupportedEntityTypeExceptionNamingIt()
    {
        DbContextOptions<ViewMappedContext> options = new DbContextOptionsBuilder<ViewMappedContext>()
            .UseSqlServer(_connectionString)
            .Options;

        await using ViewMappedContext context = new(options);

        UnsupportedEntityTypeException exception = await Assert.ThrowsAsync<UnsupportedEntityTypeException>(
            () => context.CaptureShapeAsync());

        Assert.Equal(typeof(ViewMappedWidget).FullName, exception.EntityTypeName);
    }
}

public sealed class WidgetContext(DbContextOptions<WidgetContext> options) : DbContext(options)
{
    public DbSet<Widget> Widgets => Set<Widget>();
}

public sealed class Widget
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class BracketNameContext(DbContextOptions<BracketNameContext> options) : DbContext(options)
{
    public DbSet<OddlyNamedWidget> Widgets => Set<OddlyNamedWidget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<OddlyNamedWidget>().ToTable("Widget]s");
}

public sealed class OddlyNamedWidget
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class ViewMappedContext(DbContextOptions<ViewMappedContext> options) : DbContext(options)
{
    public DbSet<ViewMappedWidget> Widgets => Set<ViewMappedWidget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<ViewMappedWidget>().ToView("SomeView");
}

public sealed class ViewMappedWidget
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
