# EFCore.AutoSeed

Seed your database from your EF Core model. One line, full referential integrity, realistic distribution. No factories, no CSV files, no manual ordering.

[![NuGet](https://img.shields.io/nuget/v/EFCore.AutoSeed.svg)](https://www.nuget.org/packages/EFCore.AutoSeed)
[![Downloads](https://img.shields.io/nuget/dt/EFCore.AutoSeed.svg)](https://www.nuget.org/packages/EFCore.AutoSeed)
[![Build](https://img.shields.io/github/actions/workflow/status/danellalc/efcore-autoseed/ci.yml)](https://github.com/danellalc/efcore-autoseed/actions)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

> [GIF: empty database to 20.000 referentially valid rows in 8 seconds]

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

Weekday/business-hour clustering, a configurable null rate and correlated properties are next; see the [roadmap](#roadmap).

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

### Coverage mode

The opposite of bulk. The *smallest* dataset that exercises everything:

```csharp
await db.AutoSeedCoverageAsync();
```

Every enum value. Every nullable property in both states. Every relationship at zero, one and many. Every string at empty, one character and maximum length. No seed or scale to configure: the row counts are structural, not scaled.

Usually under 50 rows. The dataset unit tests want and nobody assembles by hand without forgetting half of it.

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

- **It does not anonymise production data.** Not a masking tool. Production-shape capture (statistics only, never a data row) is planned; see the [roadmap](#roadmap).
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

Shipped: the model reader, cycle resolution, ~20 property inference rules, long-tail cardinality for related rows, composite keys, owned types, TPH/TPT/TPC inheritance, global query filter bias, weekday/business-hour temporal clustering, a default null rate for nullable columns, a `Total`/`Quantity` correlation, bulk insert (`SqlBulkCopy`, PostgreSQL binary `COPY`) with an equivalence test against `AutoSeedAsync`, `AutoSeedAsync`/`AutoSeedExplainAsync`/`AutoSeedCoverageAsync`/`AutoSeedFastAsync`, and `autoseed explain`.

Not shipped yet:

- **Bulk insert coverage**: `AutoSeedFastAsync` rejects TPH/TPT/TPC inheritance, owned types, foreign-key cycles and non-`int` identity keys today, falling back to `AutoSeedAsync` for those.
- **Configurable distributions**: today's null rate and temporal clustering use a fixed, sensible default; making them caller-configurable needs the `AutoSeedOptions` overload first.
- **Production shape capture and apply** (`autoseed capture`/`autoseed apply`): reproduce production's row counts and value distribution locally from statistics only, never a data row.

Details and rationale in [ARCHITECTURE.md](ARCHITECTURE.md#roadmap).

## Documentation

- [Architecture and design decisions](ARCHITECTURE.md)
- [Roadmap](ARCHITECTURE.md#roadmap)
- [For AI coding assistants](llms.txt)

## License

MIT
