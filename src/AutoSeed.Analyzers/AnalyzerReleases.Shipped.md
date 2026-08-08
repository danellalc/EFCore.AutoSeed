; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 3.0.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
AUTOSEED001 | Reliability | Warning | RequiredSelfReferenceAnalyzer, flags a required self-referencing foreign key that AutoSeed's CycleResolver can never satisfy at seeding time
