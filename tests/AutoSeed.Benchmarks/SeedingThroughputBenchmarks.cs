using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using EFCore.AutoSeed;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace EFCore.AutoSeed.Benchmarks;

public sealed class BenchmarkConfig : ManualConfig
{
    public BenchmarkConfig() =>
        AddJob(Job.Default
            .WithToolchain(InProcessNoEmitToolchain.Instance)
            .WithStrategy(RunStrategy.Monitoring)
            .WithLaunchCount(1)
            .WithWarmupCount(0)
            .WithIterationCount(3));
}

[MemoryDiagnoser]
[Config(typeof(BenchmarkConfig))]
public class SeedingThroughputBenchmarks
{
    private MsSqlContainer _container = null!;
    private BenchStoreContext _context = null!;

    [Params(1_000, 5_000)]
    public int Scale { get; set; }

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _container = new MsSqlBuilder().Build();
        await _container.StartAsync();
    }

    [GlobalCleanup]
    public async Task GlobalCleanup() => await _container.DisposeAsync();

    [IterationSetup]
    public void IterationSetup() => CreateFreshDatabaseAsync().GetAwaiter().GetResult();

    private async Task CreateFreshDatabaseAsync()
    {
        SqlConnectionStringBuilder builder = new(_container.GetConnectionString())
        {
            InitialCatalog = $"bench_{Guid.NewGuid():N}",
        };

        DbContextOptions<BenchStoreContext> options = new DbContextOptionsBuilder<BenchStoreContext>()
            .UseSqlServer(builder.ConnectionString)
            .Options;

        _context = new BenchStoreContext(options);
        await _context.Database.EnsureCreatedAsync();
    }

    [IterationCleanup]
    public void IterationCleanup() => _context.Dispose();

    [Benchmark(Baseline = true)]
    public Task Fidelity() => _context.AutoSeedAsync(seed: 42, scale: Scale);

    [Benchmark]
    public Task Fast() => _context.AutoSeedFastAsync(seed: 42, scale: Scale);
}

public sealed class BenchStoreContext(DbContextOptions<BenchStoreContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().Property(order => order.Total).HasPrecision(10, 2);
        modelBuilder.Entity<OrderItem>().Property(item => item.UnitPrice).HasPrecision(10, 2);
    }
}

public sealed class Customer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
}

public sealed class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public decimal Total { get; set; }
}

public sealed class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public decimal UnitPrice { get; set; }
}
