# Changelog

All notable changes to this project are documented here. This project follows
[Semantic Versioning](https://semver.org/): a change that alters what a given seed generates is a
breaking change, major version bump, regardless of whether it was also a bug fix.

## [3.0.0]

Two parts: an audit pass over the pipeline (six real bugs, each reproduced against a real model
shape first, then fixed with a dedicated test), and two new opt-in capabilities requested directly
off the back of that audit. Three of the six fixes are breaking because the previous behavior was
either silently wrong or crashed with a raw, unnamed exception; the other three are pure fixes with
no change to already-correct generated data. Neither new capability changes what an existing call
generates.

### Breaking

- **An optional (nullable) foreign key outside a dependency cycle is now actually populated.**
  Previously it was always left `null`, regardless of `AutoSeedOptions.NullRate` or how many valid
  principal rows existed. It now follows the same `NullRate` as any other nullable column (default
  10%), so it is populated on most rows instead of never. Same seed, same shape of model: more rows
  now carry a value than v2.0.1 produced.
- **A required EF Core 8+ complex property (`ComplexProperty`, including a nested one) is now
  rejected with `UnsupportedPropertyException` instead of being silently left at its CLR default.**
  `IEntityType.GetProperties()` never sees a complex property, so every rule-based inference path
  skipped it without anyone noticing; the exception names the full path (`Total.Currency` for a
  nested case), the same way a required scalar property with no matching rule already did.
- **Two entity types genuinely table-split, mapped to the very same table through a shared primary
  key, are now rejected with `UnsupportedEntityTypeException` instead of each getting its own,
  independently generated row.** Generating both independently either collided on the shared primary
  key or silently produced two half-formed rows for what the database expects to be one; this shape
  is structurally different from the already-supported shared-primary-key pattern across two
  *separate* tables (an `OfficeAssignment` keyed by `InstructorId`), which keeps working exactly as
  before.

### Added

- `AUTOSEED001`: a Roslyn analyzer, bundled directly in the `EFCore.AutoSeed` package
  (`analyzers/dotnet/cs`, no separate install), flagging a required self-referencing foreign key
  (`Employee.ManagerId` pointing back at `Employee`) directly in the IDE. `CycleResolver` already
  rejects the same shape at seeding time with `UnresolvableCycleException`; this reports it at
  compile time instead, before a database connection is ever opened.
- `Entity<T>().SeedWith(rows)`: a third option alongside `Exclude()`/`HasRowCount()` for a lookup
  table, seeding it with an exact, literal set of rows instead of generated ones. Inserted exactly
  as given, in order; everything else in the model can still reference the rows as valid foreign key
  targets. `AutoSeedAsync` only for now: `AutoSeedFastAsync` throws `UnsupportedEntityTypeException`
  naming the entity type if `SeedWith()` was configured for it, rather than silently generating
  instead.
- `autoseed diff` (CLI) and `AutoSeedDiff.Compare` (library): compares the seeding plan
  `AutoSeedExplainAsync` would produce against a saved baseline (`AutoSeedPlanSnapshot`,
  `AutoSeedPlanFile`), so a model change that alters what gets seeded, a new required property with
  no matching rule, a newly introduced cycle, an entity type that starts or stops being seedable, a
  row count shift, shows up as an explicit, reviewable diff instead of only surfacing the next time
  something actually seeds a database. The CLI form exits non-zero the moment a difference is found,
  for use as a CI gate; `--update-baseline` accepts the current plan as the new baseline.

### Fixed

- An `OwnsMany` owned collection (as opposed to `OwnsOne`) is now reported in `SkippedEntityTypes`
  and `AutoSeedExplainAsync`'s plan instead of being silently, unexplainedly left empty. It is still
  left empty: populating an owned collection is future work, not part of this fix.
- An implicit many-to-many join table (a skip navigation with no explicit join entity class) no
  longer crashes the entire seeding call with a raw `TargetParameterCountException` from reflection.
  EF Core represents such a join table as a shared-type entity backed by a `Dictionary<string,
  object>`, whose "properties" are actually its indexer; `Persistence` now recognizes and reads or
  writes through that indexer correctly wherever it still applies, and `ModelReader` recognizes the
  join table itself and skips it (also reported in `SkippedEntityTypes`), since populating it needs
  collection-navigation fix-up, a materially different code path from every other entity type.
- A SQL Server temporal table (`.ToTable(b => b.IsTemporal())`) no longer crashes `AutoSeedFastAsync`
  with a raw `SqlBulkCopy` error demanding the two period columns not be written to. Fidelity mode
  (`AutoSeedAsync`) was never affected: EF Core's own `SaveChangesAsync` already excludes a
  database-generated column from the `INSERT` it emits.

### Dependencies

- `Microsoft.Data.SqlClient` bumped from `5.2.2` to `6.1.1` (`6.1.0` shipped a `SqlDataReader` regression,
  fixed in `6.1.1`), and a new `Microsoft.EntityFrameworkCore.SqlServer` reference added, both needed
  for the temporal table fix above. Both packages were already unconditional transitive dependencies
  of `EFCore.AutoSeed` for every consumer, PostgreSQL-only included; this changes their version, not
  whether they are pulled in.

## [2.0.1]

### Fixed

- The README's demo GIF now renders on NuGet.org: it referenced `demo.gif` by a relative path,
  which NuGet.org's README renderer never resolves regardless of whether the file is bundled in
  the package, only an absolute URL from a trusted host (`raw.githubusercontent.com`) works. No
  code change.

## [2.0.0]

### Breaking

- **Generated data changes for models with a required `DateTime`, `DateOnly`, `TimeOnly`,
  `TimeSpan` or `byte[]` property that no name-specific rule recognized.** Previously such a
  property was silently left at its CLR default, or reached the database as a raw, unnamed
  provider exception if the column was `NOT NULL`. Five new generic inference rules now give it a
  real value. Same seed, same shape of model: different generated value than v1.0.0 produced.

### Added

- `AutoSeedAsync` and `AutoSeedFastAsync` accept an optional `configure` callback
  (`Action<SeedConfigurationBuilder>`):
  - `Entity<T>().Exclude()`: reads an entity type's already-existing rows from the database instead
    of generating new ones, and uses them as valid foreign key targets for anything that requires
    one. Only supported for an entity type with no required foreign key of its own.
  - `Entity<T>().HasRowCount(n)`: pins an entity type's row count regardless of `scale`. Same
    restriction as `Exclude()`. `0` is honored as "generate no rows", not treated as unset.
  - `Entity<T>().Property(x => x.Y).GenerateWith((random, generatedValues) => ...)`: replaces every
    built-in rule for one property with a supplied delegate, for a column no rule gets right (a
    PostGIS geography column via a value converter, a code that must match a specific pattern).
- `UnsupportedPropertyException`: thrown when a required property has no database-generated value,
  no rule recognizes it, and no custom generator was supplied for it (or the custom generator
  returned `null` for that row), instead of letting the database reject the row.
- `UnsupportedSeedConfigurationException`: thrown when `configure` excludes or pins the row count
  of an entity type that has a required foreign key of its own.
- `InvalidSeedConfigurationException`: thrown when `configure` refers to a type or property that is
  not part of the model.

### Fixed

- `demo.gif` in the README is now packed into both published packages, and shows a real, repeated,
  deterministic measurement (64,427 rows in 3.0s via `AutoSeedFastAsync`) instead of a placeholder.

## [1.0.0]

Initial release: `AutoSeedAsync`, `AutoSeedExplainAsync`, `AutoSeedCoverageAsync`,
`AutoSeedFastAsync`, `AutoSeedFromShapeAsync`/`CaptureShapeAsync`, `AutoSeedOptions`, the `autoseed`
CLI, SQL Server and PostgreSQL support.
