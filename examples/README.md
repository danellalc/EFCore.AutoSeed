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
