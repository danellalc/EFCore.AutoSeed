# Examples

Runnable demonstrations of `EFCore.AutoSeed`. Each subfolder is a standalone console project you can run directly.

## CustomerOrders

The library's canonical model, `Customer` -> `Order` -> `OrderItem`, the same shape used throughout `README.md` and `ARCHITECTURE.md`.

```bash
dotnet run --project examples/CustomerOrders
```

The whole demo is one call:

```csharp
IReadOnlyDictionary<string, int> rowCounts = await context.AutoSeedAsync(seed: 42, scale: 1_000);
```

That reads the `DbContext`'s model, works out that `Order` depends on `Customer` and `OrderItem` depends on `Order`, generates realistic names, emails, phone numbers, postal codes and monetary amounts, and writes referentially valid rows in the right order, all from a plain EF Core model with no configuration beyond the usual `DbSet` properties and navigations.

The project targets `Microsoft.EntityFrameworkCore.InMemory` rather than a real database engine so it runs with nothing installed beyond the .NET SDK, no native driver, no container, no connection string. It uses `ProjectReference` to `src/AutoSeed.Core` and `src/AutoSeed.Inference` directly rather than a NuGet package reference, since `EFCore.AutoSeed` is not yet published.

It also shows `AutoSeedExplainAsync` (the same plan, printed, nothing written) and `AutoSeedCoverageAsync` (the smallest dataset that touches every code path) on the same model.

## FastModeAndShape

A smaller `Customer` -> `Order` model, used to demonstrate the two things that need a real database engine rather than `InMemory`.

```bash
dotnet run --project examples/FastModeAndShape
```

```csharp
await context.AutoSeedAsync(seed: 42, scale: 2_000);      // fidelity mode: goes through EF, always correct
await context.AutoSeedFastAsync(seed: 42, scale: 2_000);  // fast mode: SqlBulkCopy, same data, faster

ShapeCapture shape = await productionLikeContext.CaptureShapeAsync();               // row counts only, never a data row
await localContext.AutoSeedFromShapeAsync(seed: 42, shape, scale: 100);             // relative table sizes preserved
```

Starts a real SQL Server via [Testcontainers](https://dotnet.testcontainers.org/) (needs Docker running, nothing else), times `AutoSeedAsync` against `AutoSeedFastAsync` on the same model and seed, then captures a shape from one database and applies it to another, printing the row counts each step produces so the proportional scaling is visible.
