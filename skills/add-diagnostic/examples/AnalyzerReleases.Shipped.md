; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 1.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CTS1001 | Design | Warning | Dispose IDisposable fields in the owning type's Dispose method
CTS1002 | Design | Info | Abstract types should expose protected, not public, constructors
CTS2001 | Usage | Warning | Await or observe returned Task instances
