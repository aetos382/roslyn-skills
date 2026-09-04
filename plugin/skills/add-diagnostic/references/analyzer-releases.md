# AnalyzerReleases.Shipped.md / AnalyzerReleases.Unshipped.md

`Microsoft.CodeAnalysis.Analyzers` ships a release-tracking analyzer (rules RS2000–RS2008). It reads two
additional files next to the analyzer project and reports RS2000 for every `DiagnosticDescriptor` whose ID
is not listed in either. The package's build props add both files as `AdditionalFiles` automatically when
they exist in the project directory, so no csproj change is normally needed.

## Files

| File | Purpose | Edited by |
|------|---------|-----------|
| `AnalyzerReleases.Unshipped.md` | Rules added, changed, or removed since the last release | This skill (append new rules) |
| `AnalyzerReleases.Shipped.md` | History of every released version | Release process only (moves Unshipped content under a `## Release x.y` heading) |

One pair per **analyzer assembly** (project), placed in the project directory. A code-fix project has none.
A source generator project that reports diagnostics has its own pair.

## Format

```markdown
; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CTS2002 | Usage | Warning | Forward the CancellationToken parameter to awaited calls that accept one
```

- Lines starting with `;` are comments.
- Section headings are exactly `### New Rules`, `### Removed Rules`, `### Changed Rules`.
- The table header row and separator row are required; column order is fixed.
- `Rule ID` is the value (`CTS2002`), `Category` and `Severity` must match the descriptor's `category`
  string and `defaultSeverity` name (`Error`, `Warning`, `Info`, `Hidden`). A mismatch triggers RS2001 /
  RS2003.
- **Notes**: a short plain-language sentence describing what the rule enforces (this repository's
  convention). Not the analyzer class name, not the title verbatim. Keep it on one line and avoid `|`.
- Disabled-by-default rules are still listed; nothing in the table marks them.

`### Changed Rules` (for reference; not written by this skill):

```markdown
### Changed Rules

Rule ID | New Category | New Severity | Old Category | Old Severity | Notes
--------|--------------|--------------|--------------|--------------|-------
CTS1001 | Design | Warning | Design | Info | Promoted after stabilizing
```

## Adding a rule

1. Locate `AnalyzerReleases.Unshipped.md` in the project that owns the descriptor
   (`FindConventions.cs` → `analyzerReleases[].unshipped`).
2. If the `### New Rules` section exists, append one row after the last row of its table, keeping rows
   sorted by Rule ID when the existing rows are sorted.
3. If the section is missing (file only has the comment header), add a blank line, the heading, a blank
   line, the two header rows, and the new row.
4. If the file is missing, create **both** files:
   - `AnalyzerReleases.Shipped.md` with only the two comment lines (`; Shipped analyzer releases` and the
     help URL).
   - `AnalyzerReleases.Unshipped.md` from `examples/AnalyzerReleases.Unshipped.md`, replacing the rows.
   Do not add `<AdditionalFiles>` items for them. Recent SDKs register `AnalyzerReleases.*.md` as
   additional files implicitly, so their absence from the project file means nothing; adding the items
   by hand is noise, and in the worst case a duplicate.

Whether tracking actually runs is only observable, never inferable: build once while the descriptor
exists and its row does not, and look for RS2000 (SKILL.md 5e). A clean build proves nothing, because a
tracking analyzer that never loads also produces one. If RS2000 does not appear, reference
`Microsoft.CodeAnalysis.Analyzers` directly with `PrivateAssets="all"`.

Use CRLF or LF to match the existing file. End the file with a single newline.

## Suppressions

`SuppressionDescriptor` instances are not tracked by the release-tracking analyzer. Do not add suppression
IDs to either file.

## Related rules

| Rule | Trigger | Fix |
|------|---------|-----|
| RS2000 | Descriptor ID missing from both files | Add the row to Unshipped. |
| RS2001 | Category or severity in the table differs from the descriptor | Correct the row. |
| RS2002 | Rule listed in the files but no descriptor uses it | Remove the row (or the rule was renamed by mistake). |
| RS2003 | Rule listed twice | Remove the duplicate. |
| RS2004 | Table row malformed | Fix the row (four columns for New Rules). |
| RS2005 | Section heading malformed | Use the exact heading text. |
| RS2006 | Release heading malformed in Shipped | `## Release x.y` |
| RS2007 | Unshipped file contains a release heading | Move it to Shipped. |
| RS2008 | No release-tracking files found | Create the pair. |
