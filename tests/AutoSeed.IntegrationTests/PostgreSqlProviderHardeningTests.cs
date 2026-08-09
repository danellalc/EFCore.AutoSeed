using EFCore.AutoSeed.Inference;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace EFCore.AutoSeed.IntegrationTests.Postgres;

[Trait("Category", "Integration")]
public sealed class PostgreSqlProviderHardeningTests : IAsyncLifetime
{
    private readonly PostgreSqlBuilder _builder = new PostgreSqlBuilder()
        .WithDatabase("autoseed")
        .WithUsername("autoseed")
        .WithPassword("autoseed");

    private PostgreSqlContainer _postgres = null!;

    public async Task InitializeAsync()
    {
        _postgres = _builder.Build();
        await _postgres.StartAsync().ConfigureAwait(false);
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync().ConfigureAwait(false);

    [Fact]
    public async Task AutoSeedFastAsync_WithAnOptionalForeignKeyAndAPositiveNullRate_PopulatesSomeRowsOnPostgreSql()
    {
        DbContextOptions<OptionalForeignKeyContext> options = new DbContextOptionsBuilder<OptionalForeignKeyContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        await using OptionalForeignKeyContext context = new(options);
        await context.Database.EnsureCreatedAsync();

        AutoSeedOptions autoSeedOptions = new(NullRate: 0.3);
        await context.AutoSeedFastAsync(seed: 42, scale: 100, autoSeedOptions);

        List<PurchaseOrder> orders = await context.PurchaseOrders.AsNoTracking().ToListAsync();
        List<PromoCode> promoCodes = await context.PromoCodes.AsNoTracking().ToListAsync();

        Assert.NotEmpty(promoCodes);
        Assert.Contains(orders, order => order.PromoCodeId is not null);
        Assert.Contains(orders, order => order.PromoCodeId is null);

        HashSet<int> promoCodeIds = [.. promoCodes.Select(promoCode => promoCode.Id)];
        Assert.All(
            orders.Where(order => order.PromoCodeId is not null),
            order => Assert.Contains(order.PromoCodeId!.Value, promoCodeIds));
    }

    [Fact]
    public async Task AutoSeedFastAsync_WithAnImplicitManyToMany_SeedsBothSidesAndLeavesTheJoinTableEmptyOnPostgreSql()
    {
        DbContextOptions<ManyToManyContext> options = new DbContextOptionsBuilder<ManyToManyContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        await using ManyToManyContext context = new(options);
        await context.Database.EnsureCreatedAsync();

        IReadOnlyDictionary<string, int> result = await context.AutoSeedFastAsync(seed: 42, scale: 20);

        string postKey = typeof(M2MPost).FullName!;
        string tagKey = typeof(M2MTag).FullName!;

        Assert.True(result[postKey] > 0);
        Assert.True(result[tagKey] > 0);
        Assert.False(result.ContainsKey("M2MPostM2MTag"));

        List<M2MPost> posts = await context.Posts.AsNoTracking().ToListAsync();
        List<M2MTag> tags = await context.Tags.AsNoTracking().ToListAsync();

        Assert.Equal(result[postKey], posts.Count);
        Assert.Equal(result[tagKey], tags.Count);

        long joinTableRowCount = await context.Database
            .SqlQueryRaw<long>("SELECT COUNT(*) AS \"Value\" FROM \"M2MPostM2MTag\"")
            .SingleAsync();
        Assert.Equal(0, joinTableRowCount);
    }
}

public sealed class OptionalForeignKeyContext(DbContextOptions<OptionalForeignKeyContext> options) : DbContext(options)
{
    public DbSet<PromoCode> PromoCodes => Set<PromoCode>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
}

public sealed class PromoCode
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
}

public sealed class PurchaseOrder
{
    public int Id { get; set; }
    public int? PromoCodeId { get; set; }
    public PromoCode? PromoCode { get; set; }
    public decimal Total { get; set; }
}

public sealed class ManyToManyContext(DbContextOptions<ManyToManyContext> options) : DbContext(options)
{
    public DbSet<M2MPost> Posts => Set<M2MPost>();
    public DbSet<M2MTag> Tags => Set<M2MTag>();
}

public sealed class M2MPost
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public List<M2MTag> Tags { get; set; } = [];
}

public sealed class M2MTag
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<M2MPost> Posts { get; set; } = [];
}
