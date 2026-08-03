using System.Diagnostics;
using EFCore.AutoSeed;
using EFCore.AutoSeed.Examples.FastModeAndShape;
using EFCore.AutoSeed.Shape;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

Console.WriteLine("Starting a SQL Server container (needs Docker running)...");
await using MsSqlContainer sqlServer = new MsSqlBuilder().Build();
await sqlServer.StartAsync();
string connectionString = sqlServer.GetConnectionString();
Console.WriteLine("Container ready.");
Console.WriteLine();

Console.WriteLine("== AutoSeedAsync vs AutoSeedFastAsync: same data, different insertion path ==");
Console.WriteLine();

const int Scale = 2_000;

await using (StoreContext fidelityContext = await NewDatabaseAsync("Fidelity"))
{
    Stopwatch stopwatch = Stopwatch.StartNew();
    await fidelityContext.AutoSeedAsync(seed: 42, scale: Scale);
    Console.WriteLine($"AutoSeedAsync:     {stopwatch.Elapsed.TotalSeconds:0.00}s for {Scale:N0} customers and their orders.");
}

await using (StoreContext fastContext = await NewDatabaseAsync("Fast"))
{
    Stopwatch stopwatch = Stopwatch.StartNew();
    await fastContext.AutoSeedFastAsync(seed: 42, scale: Scale);
    Console.WriteLine($"AutoSeedFastAsync: {stopwatch.Elapsed.TotalSeconds:0.00}s for {Scale:N0} customers and their orders.");
}

Console.WriteLine();
Console.WriteLine("== CaptureShapeAsync + AutoSeedFromShapeAsync: reproduce production's relative table sizes ==");
Console.WriteLine();

await using (StoreContext productionLikeContext = await NewDatabaseAsync("ProductionLike"))
{
    // A lopsided model: far more orders than customers, standing in for a real production database
    // where every table has grown at its own rate.
    await productionLikeContext.AutoSeedAsync(seed: 1, scale: 500);
    ShapeCapture shape = await productionLikeContext.CaptureShapeAsync();

    Console.WriteLine("Captured shape (row counts only, straight from the database's own statistics, never a data row):");
    foreach (TableShape table in shape.Tables.OrderBy(table => table.EntityTypeName, StringComparer.Ordinal))
    {
        Console.WriteLine($"  {table.EntityTypeName.Split('.')[^1]}: {table.RowCount:N0} rows");
    }

    Console.WriteLine();

    await using StoreContext appliedContext = await NewDatabaseAsync("FromShape");
    IReadOnlyDictionary<string, int> appliedRowCounts = await appliedContext.AutoSeedFromShapeAsync(seed: 42, shape, scale: 100);

    Console.WriteLine("Applied locally with scale: 100 (the largest captured table gets 100 rows, others stay proportional):");
    foreach (KeyValuePair<string, int> entry in appliedRowCounts.OrderBy(entry => entry.Key, StringComparer.Ordinal))
    {
        Console.WriteLine($"  {entry.Key.Split('.')[^1]}: {entry.Value:N0} rows");
    }
}

async Task<StoreContext> NewDatabaseAsync(string suffix)
{
    string connectionStringForDatabase = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = $"FastModeAndShape_{suffix}" }.ConnectionString;
    StoreContext context = new(connectionStringForDatabase);
    await context.Database.EnsureCreatedAsync();
    return context;
}
