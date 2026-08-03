using EFCore.AutoSeed.Shape;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace EFCore.AutoSeed.IntegrationTests.Postgres;

[Trait("Category", "Integration")]
public sealed class PostgreSqlShapeCaptureTests : IAsyncLifetime
{
    private const string RestrictedRole = "autoseed_shape_reader";
    private const string RestrictedPassword = "autoseed_shape_reader_password";

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
    public async Task CaptureShapeAsync_ReportsTheRealRowCountEvenWhenTableSelectIsDenied()
    {
        string connectionString = _postgres.GetConnectionString();

        await using ShapeCaptureContext adminContext = CreateContext(connectionString);
        await adminContext.Database.EnsureCreatedAsync();
        adminContext.ShapeCustomers.AddRange(Enumerable.Range(1, 41).Select(_ => new ShapeCustomer()));
        await adminContext.SaveChangesAsync();
        await AnalyzeAsync(connectionString);

        await CreateRestrictedRoleAsync(connectionString);

        string restrictedConnectionString = WithCredentials(connectionString, RestrictedRole, RestrictedPassword);

        await using (NpgsqlConnection restrictedConnection = new(restrictedConnectionString))
        {
            await restrictedConnection.OpenAsync();
            await using NpgsqlCommand selectAttempt = new("SELECT * FROM \"ShapeCustomers\"", restrictedConnection);
            PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() => selectAttempt.ExecuteScalarAsync());
            Assert.Equal("42501", exception.SqlState);
        }

        await using ShapeCaptureContext restrictedContext = CreateContext(restrictedConnectionString);
        ShapeCapture shape = await restrictedContext.CaptureShapeAsync();

        TableShape customerShape = Assert.Single(shape.Tables, table => table.EntityTypeName.EndsWith("ShapeCustomer", StringComparison.Ordinal));
        Assert.Equal(41, customerShape.RowCount);
    }

    private static async Task AnalyzeAsync(string connectionString)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand analyze = new("ANALYZE \"ShapeCustomers\";", connection);
        await analyze.ExecuteNonQueryAsync();
    }

    private static async Task CreateRestrictedRoleAsync(string connectionString)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand createRole = new(
            $"CREATE ROLE {RestrictedRole} LOGIN PASSWORD '{RestrictedPassword}';", connection);
        await createRole.ExecuteNonQueryAsync();

        await using NpgsqlCommand revokeSelect = new(
            $"REVOKE ALL ON ALL TABLES IN SCHEMA public FROM {RestrictedRole};", connection);
        await revokeSelect.ExecuteNonQueryAsync();
    }

    private static string WithCredentials(string connectionString, string username, string password)
    {
        NpgsqlConnectionStringBuilder builder = new(connectionString)
        {
            Username = username,
            Password = password,
        };

        return builder.ConnectionString;
    }

    private static ShapeCaptureContext CreateContext(string connectionString)
    {
        DbContextOptions<ShapeCaptureContext> options = new DbContextOptionsBuilder<ShapeCaptureContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ShapeCaptureContext(options);
    }
}

public sealed class ShapeCaptureContext(DbContextOptions<ShapeCaptureContext> options) : DbContext(options)
{
    public DbSet<ShapeCustomer> ShapeCustomers => Set<ShapeCustomer>();
}

public sealed class ShapeCustomer
{
    public int Id { get; set; }
}
