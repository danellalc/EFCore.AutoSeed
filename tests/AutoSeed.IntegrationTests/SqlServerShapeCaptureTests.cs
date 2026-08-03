using EFCore.AutoSeed.Shape;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace EFCore.AutoSeed.IntegrationTests.SqlServer;

[Trait("Category", "Integration")]
public sealed class SqlServerShapeCaptureTests : IAsyncLifetime
{
    private const string RestrictedLogin = "autoseed_shape_reader";
    private const string RestrictedPassword = "Str0ng!Passw0rd123";

    private readonly MsSqlContainer _container = new MsSqlBuilder().Build();
    private string _connectionString = "";

    public async Task InitializeAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);
        _connectionString = _container.GetConnectionString();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);

    [Fact]
    public async Task CaptureShapeAsync_ReportsTheRealRowCountEvenWhenTableSelectIsDenied()
    {
        await using ShapeCaptureContext adminContext = CreateContext(_connectionString);
        await adminContext.Database.EnsureCreatedAsync();
        adminContext.ShapeCustomers.AddRange(Enumerable.Range(1, 37).Select(_ => new ShapeCustomer()));
        await adminContext.SaveChangesAsync();

        await CreateRestrictedLoginAsync();

        string restrictedConnectionString = WithCredentials(_connectionString, RestrictedLogin, RestrictedPassword);

        await using (SqlConnection restrictedConnection = new(restrictedConnectionString))
        {
            await restrictedConnection.OpenAsync();
            await using SqlCommand selectAttempt = new("SELECT * FROM ShapeCustomers", restrictedConnection);
            SqlException exception = await Assert.ThrowsAsync<SqlException>(() => selectAttempt.ExecuteScalarAsync());
            Assert.Contains("permission", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        await using ShapeCaptureContext restrictedContext = CreateContext(restrictedConnectionString);
        ShapeCapture shape = await restrictedContext.CaptureShapeAsync();

        TableShape customerShape = Assert.Single(shape.Tables, table => table.EntityTypeName.EndsWith("ShapeCustomer", StringComparison.Ordinal));
        Assert.Equal(37, customerShape.RowCount);
    }

    private async Task CreateRestrictedLoginAsync()
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync();

        await using SqlCommand createLogin = new(
            $"CREATE LOGIN [{RestrictedLogin}] WITH PASSWORD = '{RestrictedPassword}';", connection);
        await createLogin.ExecuteNonQueryAsync();

        await using SqlCommand createUser = new(
            $"CREATE USER [{RestrictedLogin}] FOR LOGIN [{RestrictedLogin}];", connection);
        await createUser.ExecuteNonQueryAsync();

        await using SqlCommand grantViewState = new($"GRANT VIEW DATABASE STATE TO [{RestrictedLogin}];", connection);
        await grantViewState.ExecuteNonQueryAsync();

        // sys.dm_db_partition_stats hides a table's rows from a principal with no visibility into
        // it at all, even with VIEW DATABASE STATE: VIEW DEFINITION grants schema-only visibility
        // (this table exists, these are its columns) without granting any access to its data.
        await using SqlCommand grantViewDefinition = new($"GRANT VIEW DEFINITION ON dbo.ShapeCustomers TO [{RestrictedLogin}];", connection);
        await grantViewDefinition.ExecuteNonQueryAsync();

        await using SqlCommand denySelect = new($"DENY SELECT ON dbo.ShapeCustomers TO [{RestrictedLogin}];", connection);
        await denySelect.ExecuteNonQueryAsync();
    }

    private static string WithCredentials(string connectionString, string userId, string password)
    {
        SqlConnectionStringBuilder builder = new(connectionString)
        {
            UserID = userId,
            Password = password,
            IntegratedSecurity = false,
        };

        return builder.ConnectionString;
    }

    private static ShapeCaptureContext CreateContext(string connectionString)
    {
        DbContextOptions<ShapeCaptureContext> options = new DbContextOptionsBuilder<ShapeCaptureContext>()
            .UseSqlServer(connectionString)
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
