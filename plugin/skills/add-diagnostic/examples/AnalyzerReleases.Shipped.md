; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md
;
; This example shows the shape of the file after a release.
; A file created from scratch holds the two comment lines above and nothing else: every rule listed here must have a descriptor, or RS2002 fires.

## Release 1.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CTS1001 | Design | Warning | Dispose IDisposable fields in the owning type's Dispose method
CTS1002 | Design | Info | Abstract types should expose protected, not public, constructors
CTS2001 | Usage | Warning | Await or observe returned Task instances
