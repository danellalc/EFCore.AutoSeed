# EFCore.AutoSeed

Seed your database from your EF Core model. One line, full referential integrity, realistic distribution. No factories, no CSV files, no manual ordering.

[![NuGet](https://img.shields.io/nuget/v/EFCore.AutoSeed.svg)](https://www.nuget.org/packages/EFCore.AutoSeed)
[![Downloads](https://img.shields.io/nuget/dt/EFCore.AutoSeed.svg)](https://www.nuget.org/packages/EFCore.AutoSeed)
[![Build](https://img.shields.io/github/actions/workflow/status/danellalc/EFCore.AutoSeed/ci.yml)](https://github.com/danellalc/EFCore.AutoSeed/actions)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

![Empty database to 64,427 referentially valid rows in 3.0 seconds with AutoSeedFastAsync](https://raw.githubusercontent.com/danellalc/EFCore.AutoSeed/main/demo.gif)

## The problem

Every EF Core seeder available today asks **you** to describe the model again, in CSV files, in factory classes, in attributes with manual priority numbers, in JSON config. You already declared all of it in your `DbContext`. Then a migration changes it and your seeder breaks.

And the data you end up with is uniform: every customer with three orders. In production one customer has 500.000. The query planner picks a different plan for each shape, so **your performance test passes while lying to you**.

## Usage

```csharp
await db.AutoSeedAsync(seed: 42, scale: 1_000);
```

That is the whole API for the common case. AutoSeed reads your model, works out the insertion order, resolves cycles, infers what each property means, and writes referentially valid rows.

Same seed, same data. Always.

```bash
dotnet add package EFCore.AutoSeed
```

One package. Nothing to configure before the first run.

## What makes it different

### It reads the EF Core model, not the database schema

DDL introspection sees tables and columns. The EF model also carries navigation properties, composite keys, self-references, owned types and inheritance.

AutoSeed generates data your **database accepts**: composite primary keys and composite foreign keys need no extra configuration, owned types (`OwnsOne`, including nested owned types) get their own columns populated instead of being left null, and TPH, TPT and TPC inheritance all just work, discriminator column included.

It also works before the database exists.

### It works out the order itself

Foreign keys form a graph. AutoSeed topologically sorts it, detects cycles, and resolves the nullable ones with a second pass.

No priority numbers. No ordering by hand.

When a cycle is genuinely unsatisfiable (a required foreign key with no nullable link), AutoSeed names the entities involved and stops, instead of letting your database throw a constraint violation.

### It generates realistic distributions, not just realistic values

Most generators aim for "the name looks like a name". AutoSeed also aims for the right shape:

```
Customer:   1.000 rows
Order:      3.847 rows   long tail: mean 3.8, max 512, one customer holds 13%
OrderItem: 19.203 rows
```

Most customers have one order. A few have hundreds, and the mean and the outliers stay the same across runs with the same seed.

Weekday/business-hour clustering, null rate, the query filter bias and locale are all configurable through an optional `AutoSeedOptions`:

```csharp
await db.AutoSeedAsync(seed: 42, scale: 1_000, options: new AutoSeedOptions(NullRate: 0.2, Locale: "pt_BR"));
```

### It steps out of the way for a table you already own

A lookup table (`Status`, `Category`) does not need `scale` rows: it needs the handful of values your app actually checks against, seeded once by a migration or by hand. Exclude it, and AutoSeed reads its existing rows and uses them as valid foreign key targets for everything else, instead of trying to insert more:

```csharp
await db.AutoSeedAsync(seed: 42, scale: 1_000, configure: seed =>
{
    seed.Entity<Status>().Exclude();
    seed.Entity<Category>().HasRowCount(10);
});
```

`HasRowCount` pins an entity type's row count regardless of `scale`, for the opposite case: a table that should always have exactly this many rows.

The same `configure` callback also takes over a single property, for the rare column no inference rule gets right: a PostGIS `geography` column, say, or a code that has to match a specific pattern:

```csharp
seed.Entity<Product>().Property(product => product.Sku).GenerateWith((random, values) => $"SKU-{random.Next(0, 100_000):D6}");
```

Runs before, and wins over, every built-in rule for that property. Takes the row's own seeded random source and the values already generated for its other properties, same as a built-in rule does.

### It explains itself before it writes anything

```csharp
var plan = await db.AutoSeedExplainAsync(seed: 42, scale: 1_000);
Console.WriteLine(plan.ToReport());
```

Prints the insertion order, the row count per entity type, which cycles got deferred to a second pass, and which entity types were skipped and why. Nothing is written to the database.

The same thing is available from the command line:

```bash
dotnet tool install -g EFCore.AutoSeed.Cli
autoseed explain --context MyApp.AppDbContext --assembly bin/Release/net10.0/publish/MyApp.dll
```

### It catches one unseedable shape before you even run it

A required foreign key that points back at its own entity type (`ManagerId` on `Employee`, pointing at `Employee`) can never be satisfied: the very first row has no earlier row of the same type to reference. `AutoSeedAsync` already rejects it at seeding time with a named `UnresolvableCycleException`, but a bundled Roslyn analyzer, `AUTOSEED001`, flags the same shape directly in the IDE, on the C# model class, before a database connection is ever opened:

```
warning AUTOSEED001: 'Employee.ManagerId' is a required (non-nullable) foreign key back to
'Employee' itself, via navigation 'Manager'; AutoSeed can never generate a value for the first
row of a self-referencing entity when the key is required. Make 'ManagerId' nullable, or exclude
'Employee' from seeding.
```

No setup: it ships inside the `EFCore.AutoSeed` package and activates as soon as the package is installed.

### Coverage mode

The opposite of bulk. The *smallest* dataset that exercises everything:

```csharp
await db.AutoSeedCoverageAsync();
```

Every enum value. Every nullable property in both states. Every relationship at zero, one and many. Every string at empty, one character and maximum length. No seed or scale to configure: the row counts are structural, not scaled.

Usually under 50 rows. The dataset unit tests want and nobody assembles by hand without forgetting half of it.

### Fast mode

```csharp
await db.AutoSeedFastAsync(seed: 42, scale: 1_000);
```

Same generated data as `AutoSeedAsync` for the same seed, an equivalence test proves it, but written with `SqlBulkCopy` (SQL Server) or a binary `COPY` (PostgreSQL) instead of `SaveChanges`. See [Benchmarks](#benchmarks) below for actual numbers.

Only entity types simple enough to make bypassing EF Core safe are supported: an inherited entity type or a foreign-key cycle fails loudly and points back at `AutoSeedAsync`, instead of risking silently wrong data. Owned types and `int`/`long`/`Guid` identity primary keys are supported.

### Production shape

```csharp
ShapeCapture shape = await productionReplicaDb.CaptureShapeAsync();
await db.AutoSeedFromShapeAsync(seed: 42, shape, scale: 1_000);
```

`CaptureShapeAsync` reads a row count for every table straight from the database engine's own maintained statistics (`sys.dm_db_partition_stats` on SQL Server, `pg_class.reltuples` on PostgreSQL): never a query against an actual row. `AutoSeedFromShapeAsync` then seeds a table present in the shape at a scale proportional to its captured row count relative to the largest one, so a local database's relative table sizes resemble where the shape came from, instead of every independent table getting the same flat `scale`.

The same thing from the command line:

```bash
autoseed capture --context MyApp.AppDbContext --assembly bin/Release/net10.0/publish/MyApp.dll --output shape.json
autoseed apply --context MyApp.AppDbContext --assembly bin/Release/net10.0/publish/MyApp.dll --shape shape.json
```

## Supported frameworks

| Target | Status |
|---|---|
| `net10.0` | supported |
| `net8.0` | supported |
| `netstandard2.0` | not yet, [open an issue](../../issues) if you need it |
| `net45` and older | **not possible**: EF Core does not exist there |

EF Core itself requires .NET 8 or later from version 8 onwards, so those are the targets that matter. .NET Framework 2.0 through 4.5 is Entity Framework 6 territory, which has a different model API entirely.

Supported EF Core versions: the two most recent majors.

## What it does not do

- **It does not anonymise production data.** Not a masking tool. Production-shape capture (`autoseed capture`) reads row counts only, from the database's own statistics, never a data row.
- **It is not a service.** No cloud, no account, no server to keep running.
- **EF Core only.** Not Dapper, not raw ADO.NET, not other ORMs.
- **SQL Server and PostgreSQL only.**
- **It refuses models it cannot satisfy**, loudly and by name.

## Validated

Property-based tests assert that for **any** model and **any** seed, every foreign key points at an existing row and no constraint is violated. They run on every commit.

Also tested against 4 real, public schemas: Northwind, Chinook, Contoso University and a lite AdventureWorks OLTP subset. Composite keys, self-references, shared-primary-key one-to-ones and many-to-many join tables included.

## Benchmarks

A single local run, containerized SQL Server, three-table schema (`Customer` &rarr; `Order` &rarr; `OrderItem`), `AutoSeedAsync` (fidelity mode) against `AutoSeedFastAsync` (bulk insert):

| Root rows | Fidelity mode | Fast mode | Speedup |
|---|---|---|---|
| 1,000 | 6.0 s | 0.6 s | ~10x |
| 5,000 | 25.9 s | 3.0 s | ~9x |

Fast mode also allocates about 60% less managed memory at both scales. These are directional numbers from one machine, one schema, three iterations each, not a rigorous multi-environment study: the point is the order of magnitude, not the second decimal place. Reproduce them yourself, or run your own shape, with `dotnet run -c Release` in `tests/AutoSeed.Benchmarks`.

## Compared to

| Package | Approach | Last release |
|---|---|---|
| **Bogus** | generates values for objects. AutoSeed is built on it and does not replace it | active |
| **EFCore.Seeder** | you write CSV files, embed them as resources, implement `IEquatable` | 2020 |
| **EntityFrameworkCore.Seeder** | you write a factory class with rules per entity | 1.0.1 |
| **Ef.Seeder** | you set a numeric priority per entity to get the order right | 1.0.2 |
| **BulkDataSeeder.EfCore** | CSV files plus JSON configuration | 1.0.4 |
| **AutoFixture** | fills objects by reflection, breaks on foreign keys | active |
| **EF Core `HasData`** | you type each row | built in |

They are all seeders. They are all manual. That is the gap this fills.

Outside .NET, **SynthDB** and **Seedfast** take a similar approach for PostgreSQL, reading DDL rather than an ORM model.

## Roadmap

Shipped: the model reader, cycle resolution, ~20 property inference rules including a `Discount`/`AmountDue` correlation alongside `Total`/`Quantity`, long-tail cardinality for related rows, composite keys, owned types (fidelity and fast mode), TPH/TPT/TPC inheritance, global query filter bias, weekday/business-hour temporal clustering, a null rate for nullable columns, optional dirty-data noise (casing, whitespace, diacritics) for free-text values, all four of those configurable through `AutoSeedOptions`, bulk insert (`SqlBulkCopy`, PostgreSQL binary `COPY`) with `int`/`long`/`Guid` identity keys and an equivalence test against `AutoSeedAsync`, production row-count capture and apply (`autoseed capture`/`autoseed apply`, `CaptureShapeAsync`/`AutoSeedFromShapeAsync`), `AutoSeedAsync`/`AutoSeedExplainAsync`/`AutoSeedCoverageAsync`/`AutoSeedFastAsync`, `autoseed explain`, and an optional `configure` callback that excludes an entity type, pins its row count, or replaces a single property's generator (`AutoSeedAsync`/`AutoSeedFastAsync` only, `AutoSeedExplainAsync`/`AutoSeedFromShapeAsync`/`AutoSeedCoverageAsync` not yet).

Not shipped yet:

- **Bulk insert coverage**: `AutoSeedFastAsync` still rejects TPH/TPT/TPC inheritance and foreign-key cycles, falling back to `AutoSeedAsync` for those.
- **Per-column shape statistics**: a captured shape holds row counts only today; null fraction, distinct count and value histograms are not captured, so `AutoSeedFromShapeAsync` shapes relative table sizes, not value distributions. `AutoSeedFromShapeAsync` is also fidelity-mode only, no fast-mode equivalent yet.
- **`configure` on every seeding method**: `AutoSeedAsync` and `AutoSeedFastAsync` only, for now.

Details and rationale in [ARCHITECTURE.md](ARCHITECTURE.md#roadmap).

## Documentation

- [Architecture and design decisions](ARCHITECTURE.md)
- [Roadmap](ARCHITECTURE.md#roadmap)
- [Changelog](CHANGELOG.md)
- [For AI coding assistants](llms.txt)

## License

MIT
