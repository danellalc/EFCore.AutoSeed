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

AutoSeed reads the filter and respects its intent: the overwhelming majority of rows are visible, with a small configurable proportion filtered out.

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

- **Fidelity mode**: goes through EF. Always correct, slower. The default.
- **Fast mode**: bulk copy. Opt-in.

An equivalence test asserts both modes produce identical data for the same seed. Fast mode does not ship without it.

### Why statistics-only capture

Shape capture reads row counts, cardinality and distribution histograms. It never reads a row.

This is privacy by construction rather than by policy: the captured file contains no personal data because no personal data is ever read. That distinction is what makes the feature usable inside a regulated company.

### Why `net8.0` and `net10.0` only, for now

`netstandard2.0` reaches further, but pins the package to EF Core 3.1, the last netstandard-targeting release. Multi-targeting across EF Core majors doubles the test matrix for a .NET Framework audience that may not exist for this library.

The cheap path is to wait for someone to open an issue asking. Adding a target later is easy; supporting one nobody uses is not.

---

## Roadmap

**v1: the core** (shipped)
Model reading, dependency graph, nullable cycle resolution, composite keys and FKs, owned types, semantic inference, basic long tail, deterministic seed, SQL Server and PostgreSQL in fidelity mode, `AutoSeedAsync`, `AutoSeedExplainAsync`, `autoseed explain` as a `dotnet tool`, `AutoSeedCoverageAsync`.

**v2: depth**
Bulk insert with equivalence test, TPH/TPT/TPC, query filters, full distribution engine (temporal clustering, null rates, correlation), published benchmarks.

**v3: control**
Per-property rule overrides without losing inference for the rest. Named profiles ("small shop", "large marketplace", "stress base"). Seeding over existing data. Snapshot and restore for fast integration tests.

**v4: shape**
Production shape capture and apply.

**v5: refinements**
Dirty data mode (accents, trailing whitespace, inconsistent casing). xUnit and Testcontainers integration. A Roslyn analyzer flagging unseedable models at compile time. First-class pt-BR locale with valid CPF, CNPJ and postal codes. `netstandard2.0`, if asked for.

### Explicitly out of scope

Production data anonymisation. Cloud or hosted service. ORMs other than EF Core. Databases other than SQL Server and PostgreSQL.

Two open source tools in this category died from scope creep. Staying small is the plan, not a limitation.
