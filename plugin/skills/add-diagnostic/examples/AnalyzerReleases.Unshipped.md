; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CTS2002 | Usage | Warning | Forward the CancellationToken parameter to awaited calls that accept one
CTS3001 | Performance | Info | Build strings in loops with StringBuilder instead of concatenation
