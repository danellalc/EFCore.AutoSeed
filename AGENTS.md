# AGENTS.md

Guidance for AI coding agents working in this repository.

If you are consuming this library rather than developing it, read [llms.txt](llms.txt) instead.

## What this project is

A library that seeds a database by reading the Entity Framework Core model. The model is the specification — nothing about entities, ordering or relationships is declared by hand.

Everything else in this file follows from that.

## Build and test

```bash
dotnet build
dotnet test                                        # everything
dotnet test --filter Category!=Integration         # no database required
dotnet test --filter FullyQualifiedName~CycleResolver
dotnet run --project tests/AutoSeed.Benchmarks
```

Integration tests use Testcontainers and need Docker running. Unit and property tests do not touch a database.

## The pipeline

New code belongs to exactly one of seven stages:

1. `ModelReader` — reads `IModel`
2. `DependencyGraph` — stable topological sort
3. `CycleResolver` — nullable cycles resolved, required cycles rejected
4. `GenerationPlan` — row counts, cardinality, distribution
5. `ValueGeneration` — semantic inference
6. `ConstraintSatisfaction` — unique, check, length, precision
7. `Persistence` — ordered insert

Before writing something new, find the sibling stage that already solved a similar problem.

## Hard rules

**`AutoSeed.Core` depends on `Microsoft.EntityFrameworkCore` and nothing else.** Not Bogus, not a database driver. If you are about to add a `using Bogus` there, the code belongs in `AutoSeed.Inference`.

**Never reference `Microsoft.EntityFrameworkCore.*.Internal` namespaces.** They break between minor versions.

**Determinism is the central guarantee.** No `Random` without a seed, no `DateTime.Now`, no `Guid.NewGuid()` anywhere in the generation path. Randomness derives hierarchically and positionally: `root → entity → row index → property`. Sorting must be stable — ties break on entity name, never hash order.

Any change that alters generated data for a given seed is a **breaking change** requiring a major version bump.

**Generation is sequential. Insertion may be parallel.** Parallelising generation breaks determinism.

**Fail by name.** An unsupported or unsatisfiable model throws a typed exception naming the entities and properties involved. Never let the database surface the error instead.

## Code style

- No comments. Descriptive names instead.
- No emojis, anywhere.
- Nullable reference types on, warnings as errors, `sealed` by default.
- **XML docs required on every public member** — this ships to NuGet and becomes IntelliSense.
- `ConfigureAwait(false)` on every await. This is a library.
- `CancellationToken` on every public async method.
- Typed exceptions derived from `AutoSeedException`, never bare `Exception`.
- Exception messages in English, actionable, naming the entity and property.

## Testing expectations

Anything touching stages 1–6 needs a property-based test, not just examples. The invariant is: for any model and any seed, every foreign key references an existing row and no constraint is violated.

New inference rules and new distribution shapes ship with tests. Fast mode changes ship with an equivalence test against fidelity mode.

## Commits

Conventional commits in English. Scopes: `core`, `inference`, `distributions`, `coverage`, `shape`, `providers`, `cli`.

```
feat(core): resolve nullable foreign key cycles in two passes
fix(inference): respect max length when generating email addresses
```

Never mention AI assistance, Claude, or co-authorship in commit messages.

## Out of scope

Do not implement, and close issues requesting: production data anonymisation, a hosted service, ORMs other than EF Core, databases other than SQL Server and PostgreSQL.

Two open source projects in this category died from scope creep. Staying small is deliberate.
