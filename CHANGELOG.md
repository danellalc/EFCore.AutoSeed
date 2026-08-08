# Changelog

All notable changes to this project are documented here. This project follows
[Semantic Versioning](https://semver.org/): a change that alters what a given seed generates is a
breaking change, major version bump, regardless of whether it was also a bug fix.

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
