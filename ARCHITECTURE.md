# Architecture

## Packaging

Seven projects internally. **Two packages published.**

```
EFCore.AutoSeed        the library. Pulls everything the common case needs.
EFCore.AutoSeed.Cli    dotnet tool, for capture/apply and CI use.
```

Internal modularity is an engineering concern. Package fragmentation is a user problem: nobody should have to work out which of seven packages to install, and an AI assistant will get it wrong.

## Pipeline

Everything happens in seven stages, in order. New code belongs to exactly one of them.

```
IModel
  1. ModelReader            entities, keys, FKs, navigations, inheritance, converters, filters
  2. DependencyGraph        stable topological sort
  3. CycleResolver          nullable cycles resolved, required cycles rejected by name
  4. GenerationPlan         row counts, cardinality, distribution shapes
  5. ValueGeneration        semantic inference, seeded
  6. ConstraintSatisfaction unique, check, length, precision
  7. Persistence            ordered insert, fidelity mode or fast mode
```

Stages 1–4 and 6 live in `AutoSeed.Core`, which depends on `Microsoft.EntityFrameworkCore` and nothing else. That boundary is what lets ten thousand property-test cases run in seconds without a database.

---

## The hard problems

This is the interesting part. Everything else is plumbing.

### Cycles in the dependency graph

`Employee.ManagerId → Employee`. Or `Order.ContactId → Contact` with `Contact.DefaultOrderId → Order`.

- **Nullable FK**: insert with null, second pass issues the `UPDATE`. The common case.
- **Required FK in a cycle**: mathematically unsatisfiable without deferred constraints. AutoSeed detects it, names every entity in the cycle, and throws.
- **Required self-reference**: same, always unsatisfiable at the root.

Failing clearly is the feature. Letting the database throw a foreign key violation is a bug.

### Composite keys and composite foreign keys

The classic mistake is generating `(DepartmentId, CityId)` from two individually valid values that never appear together. A composite FK must reference a tuple that exists.

AutoSeed never generates foreign key values column by column. It picks an already-generated parent row and copies its key.

### Uniqueness without O(n²)

Naive retry against a `HashSet` degrades badly at scale. AutoSeed draws from a pre-shuffled pool with uniqueness guaranteed by construction. Bounded retry handles rare collisions; a deterministic suffix is the last resort.

### Inheritance

- **TPH**: one table, discriminator column, configurable mix of subtypes.
- **TPT and TPC**: several tables per hierarchy, each needing its own position in the global ordering.

### Owned types and value converters

An owned type is **not an entity**. It is a set of columns on the owner. Treating it as a table breaks everything downstream.

Value converters (enum as string, strongly-typed IDs) require generating the CLR value and letting EF convert. This conflicts with bulk insert, which bypasses EF (see the two persistence modes below).

### Global query filters

If the model declares `HasQueryFilter(x => !x.IsDeleted)` and AutoSeed generates 50% deleted rows, the application opens and sees almost nothing.

AutoSeed parses the filter's expression tree and, for the common single-property shapes (`!x.IsDeleted`, `x.IsActive`, `x.Flag == true`/`== false`, `x.DeletedAt == null`), biases that property so about 90% of rows pass the filter. A filter of any other shape (compound, multi-property) is left alone: the property falls through to whatever other rule would otherwise infer it. Not configurable yet; see the roadmap.

### Distributions

`AutoSeed.Distributions` holds the statistical shapes other stages draw from, kept separate so they can be swapped or tuned without touching the rules that use them:

- **Temporal clustering**: a timestamp drawn uniformly across a multi-year window looks nothing like real traffic, which clusters on weekdays during business hours. Every `CreatedAt`/`UpdatedAt`/`DeletedAt`-style draw goes through a sampler biased toward Monday-Friday, 09:00-18:00, then clamps back into the caller's window so the existing chronological-ordering guarantee never breaks.
- **Null rate**: a nullable column that is populated on every single generated row is not realistic; production data has gaps. After inference runs, any nullable, non-foreign-key property gets its value discarded on about 10% of rows, unless the rule that claimed it already controls its own nullability (the query filter rule, which already decides pass-or-fail per row).
- **Correlated properties**: `AutoSeed.Inference` reads sibling values through the same `generatedValues` dictionary the query filter and email rules already use. A `Total`/`Subtotal`/`LineTotal` property is computed from a same-row `Price`-suffixed and `Quantity`-suffixed value when both exist, instead of an independent draw that would never add up.

None of this is configurable by the caller yet: the rates and windows are fixed, sensible defaults. Configurability needs the `AutoSeedOptions` overload first; see the roadmap.

### Existing data

Seeding into a database that already has rows means foreign keys may reference either new rows or pre-existing ones. AutoSeed reads existing keys before planning.

### Determinism under parallelism

Same seed, same data, always. This is the central guarantee and what makes the library usable in tests.

Randomness flows through a seeded generator derived **hierarchically and positionally**: `root → entity → row index → property`. Generating row 500 in isolation produces the same value it would produce inside a batch.

The topological sort must be **stable**: ties break on entity name, never on hash order.

Parallelising generation would break this. AutoSeed generates sequentially and parallelises insertion instead: insertion order does not affect content.

Any change that alters generated data for a given seed is a **breaking change**.

---

## Design decisions

### Why the EF model instead of DDL introspection

DDL sees tables and columns. The EF model additionally carries navigation properties, owned types, inheritance strategy, value converters, shadow properties and query filters.

Generating from DDL produces data the database accepts. Generating from the model produces data the **application** can read. It also works before the database exists.

The cost is being locked to EF Core. Deliberate trade, stated in the README.

### Why automatic ordering instead of declared priority

Every existing .NET seeder asks the user to declare order: through attribute priority, file naming, or call sequence. That information is already in the model as foreign keys.

Asking for it again is asking the user to maintain a second, manual copy of something the framework already knows, which drifts the moment a relationship changes.

### Why Bogus lives in `AutoSeed.Inference`, not in `AutoSeed.Core`

Value generation is one stage of seven. Keeping it isolated means the engine can be tested without it, and a different value generator can be substituted without touching the graph, planner or constraint layer.

### Why two persistence modes

`SaveChanges` with a million rows is not viable. Bulk copy is, but it bypasses EF, so value converters, shadow properties and key generation must be reimplemented by hand.

- **Fidelity mode** (`AutoSeedAsync`): goes through EF. Always correct, slower. The default.
- **Fast mode** (`AutoSeedFastAsync`): `SqlBulkCopy` or binary `COPY`. Opt-in.

An equivalence test asserts both modes produce identical data for the same seed. Fast mode did not ship without it: it reuses the same model reading, cycle resolution, cardinality and value generation as fidelity mode, only swapping how rows reach the database.

Bypassing EF means AutoSeed itself must assign identity primary keys (sequential integers, matching what an auto-increment column would produce against an empty table) so foreign keys can be wired before the insert happens. That, in turn, is why fast mode only supports entity types simple enough for this to be safe: a single-column `int` identity key, no inheritance, no owned types, no foreign-key cycles. Anything else throws a named exception pointing back at `AutoSeedAsync` rather than risk quietly writing wrong data.

`tests/AutoSeed.Benchmarks` (BenchmarkDotNet) measures both modes against a real, containerized SQL Server on the same three-table schema the equivalence test uses. One local run: fast mode finished about 10x faster at 1,000 root rows (0.6 s versus 6.0 s) and about 9x faster at 5,000 (3.0 s versus 25.9 s), allocating roughly 60% less managed memory both times. Numbers in the [README](README.md#benchmarks); rerun locally before quoting them as anything more than directional.

### Why statistics-only capture

Shape capture reads row counts, cardinality and distribution histograms. It never reads a row.

This is privacy by construction rather than by policy: the captured file contains no personal data because no personal data is ever read. That distinction is what makes the feature usable inside a regulated company.

### Why `net8.0` and `net10.0` only, for now

`netstandard2.0` reaches further, but pins the package to EF Core 3.1, the last netstandard-targeting release. Multi-targeting across EF Core majors doubles the test matrix for a .NET Framework audience that may not exist for this library.

The cheap path is to wait for someone to open an issue asking. Adding a target later is easy; supporting one nobody uses is not.

---

## Roadmap

**v1: the core** (shipped)
Model reading, dependency graph, nullable cycle resolution, composite keys and FKs, owned types, TPH/TPT/TPC inheritance, semantic inference, global query filter bias for simple single-property filters, weekday/business-hour temporal clustering, a default null rate for nullable columns, `Total`/`Quantity` correlation, basic long tail, deterministic seed, SQL Server and PostgreSQL in fidelity and fast mode, `AutoSeedAsync`, `AutoSeedExplainAsync`, `AutoSeedFastAsync`, `autoseed explain` as a `dotnet tool`, `AutoSeedCoverageAsync`, a published benchmark comparing fidelity and fast mode.

**v2: depth**
Fast mode support for inheritance, owned types, foreign-key cycles and non-`int` identity keys; an `AutoSeedOptions` overload making the query filter bias, null rate and temporal clustering caller-configurable; compound filter shapes; more correlated-property pairs.

**v3: control**
Per-property rule overrides without losing inference for the rest. Named profiles ("small shop", "large marketplace", "stress base"). Seeding over existing data. Snapshot and restore for fast integration tests.

**v4: shape**
Production shape capture and apply.

**v5: refinements**
Dirty data mode (accents, trailing whitespace, inconsistent casing). xUnit and Testcontainers integration. A Roslyn analyzer flagging unseedable models at compile time. First-class pt-BR locale with valid CPF, CNPJ and postal codes. `netstandard2.0`, if asked for.

### Explicitly out of scope

Production data anonymisation. Cloud or hosted service. ORMs other than EF Core. Databases other than SQL Server and PostgreSQL.

Two open source tools in this category died from scope creep. Staying small is the plan, not a limitation.
