using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace EFCore.AutoSeed.IntegrationTests.SqlServer;

public sealed class SqlServerContainerFixture : IAsyncLifetime
{
    public MsSqlContainer Container { get; } = new MsSqlBuilder().Build();

    public async Task InitializeAsync() => await Container.StartAsync().ConfigureAwait(false);

    public async Task DisposeAsync() => await Container.DisposeAsync().ConfigureAwait(false);
}

[Trait("Category", "Integration")]
public sealed class InheritanceAutoSeedTests(SqlServerContainerFixture fixture) : IClassFixture<SqlServerContainerFixture>
{
    [Fact]
    public async Task AutoSeedAsync_WithTph_SeedsBothTheBaseAndTheDerivedTypeAndLeavesTheDiscriminatorAlone()
    {
        await using TphContext context = new(await CreateOptionsAsync<TphContext>("tph"));

        IReadOnlyDictionary<string, int> result = await context.AutoSeedAsync(seed: 42, scale: 10);

        Assert.Equal(10, result[typeof(Employee).FullName!]);
        Assert.Equal(10, result[typeof(Manager).FullName!]);
        Assert.Equal(20, await context.Employees.CountAsync());
        Assert.Equal(10, await context.Managers.CountAsync());
    }

    [Fact]
    public async Task AutoSeedAsync_WithTpt_SeedsBothTablesWithoutAKeyCollision()
    {
        await using TptContext context = new(await CreateOptionsAsync<TptContext>("tpt"));

        IReadOnlyDictionary<string, int> result = await context.AutoSeedAsync(seed: 42, scale: 10);

        Assert.Equal(10, result[typeof(TptEmployee).FullName!]);
        Assert.Equal(10, result[typeof(TptManager).FullName!]);
        Assert.Equal(20, await context.Employees.CountAsync());
        Assert.Equal(10, await context.Managers.CountAsync());

        List<int> managerIds = await context.Managers.Select(manager => manager.Id).ToListAsync();
        Assert.Equal(managerIds.Count, managerIds.Distinct().Count());
    }

    [Fact]
    public async Task AutoSeedAsync_WithTpc_SeedsEveryConcreteTypeWithoutFailingOnTheAbstractBase()
    {
        await using TpcContext context = new(await CreateOptionsAsync<TpcContext>("tpc"));

        IReadOnlyDictionary<string, int> result = await context.AutoSeedAsync(seed: 42, scale: 10);

        Assert.Equal(10, result[typeof(TpcManager).FullName!]);
        Assert.Equal(10, result[typeof(TpcContractor).FullName!]);
        Assert.Equal(10, await context.Managers.CountAsync());
        Assert.Equal(10, await context.Contractors.CountAsync());
    }

    private async Task<DbContextOptions<TContext>> CreateOptionsAsync<TContext>(string databaseName)
        where TContext : DbContext
    {
        string connectionString = fixture.Container.GetConnectionString();

        await using (SqlConnection connection = new(connectionString))
        {
            await connection.OpenAsync().ConfigureAwait(false);
            await using SqlCommand command = new($"CREATE DATABASE {databaseName}", connection);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        SqlConnectionStringBuilder builder = new(connectionString) { InitialCatalog = databaseName };
        DbContextOptions<TContext> options = new DbContextOptionsBuilder<TContext>().UseSqlServer(builder.ConnectionString).Options;

        await using TContext context = (TContext)Activator.CreateInstance(typeof(TContext), options)!;
        await context.Database.EnsureCreatedAsync().ConfigureAwait(false);

        return options;
    }
}

public sealed class TphContext(DbContextOptions<TphContext> options) : DbContext(options)
{
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Manager> Managers => Set<Manager>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Manager>().Property(manager => manager.Budget).HasPrecision(10, 2);
}

public class Employee
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class Manager : Employee
{
    public decimal Budget { get; set; }
}

public sealed class TptContext(DbContextOptions<TptContext> options) : DbContext(options)
{
    public DbSet<TptEmployee> Employees => Set<TptEmployee>();
    public DbSet<TptManager> Managers => Set<TptManager>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TptEmployee>().ToTable("TptEmployees");
        modelBuilder.Entity<TptManager>().ToTable("TptManagers");
        modelBuilder.Entity<TptManager>().Property(manager => manager.Budget).HasPrecision(10, 2);
    }
}

public class TptEmployee
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class TptManager : TptEmployee
{
    public decimal Budget { get; set; }
}

public sealed class TpcContext(DbContextOptions<TpcContext> options) : DbContext(options)
{
    public DbSet<TpcManager> Managers => Set<TpcManager>();
    public DbSet<TpcContractor> Contractors => Set<TpcContractor>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TpcEmployee>().UseTpcMappingStrategy();
        modelBuilder.Entity<TpcManager>().ToTable("TpcManagers");
        modelBuilder.Entity<TpcContractor>().ToTable("TpcContractors");
        modelBuilder.Entity<TpcManager>().Property(manager => manager.Budget).HasPrecision(10, 2);
        modelBuilder.Entity<TpcContractor>().Property(contractor => contractor.HourlyRate).HasPrecision(10, 2);
    }
}

public abstract class TpcEmployee
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class TpcManager : TpcEmployee
{
    public decimal Budget { get; set; }
}

public sealed class TpcContractor : TpcEmployee
{
    public decimal HourlyRate { get; set; }
}
