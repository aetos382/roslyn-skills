# Diagnostic ID Conventions

## Two parts of an ID

Every diagnostic has a **name** and a **value**. Both are declared once, as a constant, in the IDs file:

```csharp
public const string DisposableFieldShouldBeDisposed = "CTS1001";
//                  ^ name                             ^ value
```

- The **name** is what code refers to (`DiagnosticIds.DisposableFieldShouldBeDisposed`), what the
  DiagnosticDescriptor field is called, and the stem of every resource name
  (`DisposableFieldShouldBeDisposedTitle`, `...Message`, `...Description`).
- The **value** is what users see (`CTS1001`), what goes in `AnalyzerReleases.*.md`, `.editorconfig`,
  `#pragma warning`, and the documentation file name.

## Naming the diagnostic

Write the name in PascalCase English as a **normative statement** about the code: what it *should*,
*must*, *should not*, or *may* do. The name reads as a rule, not as a description of the analyzer.

| Good | Why |
|------|-----|
| `DisposableFieldShouldBeDisposed` | States the rule. |
| `TaskShouldBeAwaited` | Short, normative. |
| `AbstractTypeShouldNotHavePublicConstructor` | Negative rule with `ShouldNot`. |
| `CancellationTokenShouldBeForwarded` | Names the subject first, then the obligation. |
| `AsyncMethodNameMustEndWithAsync` | `Must` for hard requirements (typically Error severity). |

| Avoid | Problem |
|-------|---------|
| `DisposableField` | Describes a subject, not a rule. |
| `DetectUndisposedFields` | Describes the analyzer's action. |
| `UndisposedFieldWarning` | Encodes severity, which can change. |
| `CTS1001Rule` | Encodes the value. |
| `Disposable_Field_Should_Be_Disposed` | Not PascalCase. |

Derive the name from the description the user gave: identify the subject (field, method, call, type),
the obligation (should be X / should not Y), and drop everything else. Prefer `Should` unless the
diagnostic is an Error, where `Must` fits. Keep names under roughly 50 characters; the resource names add
up to 11 more.

## Value format

```
<PREFIX><NUMBER>          diagnostic     e.g. CTS1001
<PREFIX>S<NUMBER>         suppression    e.g. CTSS0001
```

- **Prefix**: uppercase ASCII letters chosen once per product; three letters by convention, and the
  tools accept two to six. Reuse the prefix already present in the IDs file (constants or band headers);
  ask for one only when none can be found. Avoid prefixes owned by well-known
  analyzers (`CA`, `CS`, `IDE`, `RS`, `SA`, `MA`, `VSTHRD`, `xUnit`, `NUnit`, `SYSLIB`, `ASP`, `EF`, `BL`,
  `MVC`, `RZ`, `IL`, `CSE`).
- **Number**: zero-padded; four digits by default. Infer the digit count from existing IDs. Never renumber
  or reuse a value once it has shipped (release tracking treats the value as the identity of the rule).

## Grouping by category band

The leading digit of the number is the **band**, and each band belongs to one category. Related rules
therefore end up adjacent. With four digits:

| Band | Range | Category (example) |
|------|-------|--------------------|
| 1 | 1001–1999 | Design |
| 2 | 2001–2999 | Usage |
| 3 | 3001–3999 | Performance |
| 4 | 4001–4999 | Naming |
| 9 | 9001–9999 | Generator / infrastructure diagnostics |

Rules:

1. The band-to-category mapping lives in comment headers in the IDs file (format below) and optionally in
   `.claude/roslyn-skills.md` under `categories:`. Read the mapping from there; do not invent a new
   mapping when one exists.
2. A new diagnostic takes the next free number **inside its category's band**
   (`NextId.cs --category <name>` computes it). Gaps are fine; do not fill holes left by
   removed rules.
3. A new category takes the next unused band. Add its comment header to the IDs file, its constant to the
   categories class, and (if the config file exists) its entry under `categories:`.
4. If the repository does not use bands (numbers are sequential with no comment headers), keep doing
   what it does: next number overall, and place the constant next to related ones.

## Layout of the IDs file

One file per kind, no `#region`, one comment header per band, constants sorted by number inside each
band:

```csharp
namespace Contoso.Analyzers;

public static class DiagnosticIds
{
    // Design (CTS1xxx)
    public const string DisposableFieldShouldBeDisposed = "CTS1001";
    public const string AbstractTypeShouldNotHavePublicConstructor = "CTS1002";

    // Usage (CTS2xxx)
    public const string TaskShouldBeAwaited = "CTS2001";
}
```

The comment header must match `// <Category> (<PREFIX><band>xxx)` so the scripts can read the mapping.
Variants such as `// ---- Usage: CTS2xxx ----` are also recognized, but keep one style per file.

To insert a constant: find the band's header, walk its constants, and insert before the first constant
with a larger number (or at the end of the block, before the blank line that precedes the next header).
Match the existing indentation, access modifier, and blank-line pattern exactly.

Default file names and locations when creating from scratch:

| File | Location | Contents |
|------|----------|----------|
| `DiagnosticIds.cs` | Root of the analyzer project | `static class DiagnosticIds` |
| `SuppressionIds.cs` | Root of the analyzer project | `static class SuppressionIds` |
| `DiagnosticCategories.cs` | Root of the analyzer project | `static class DiagnosticCategories` with one `const string` per category |

See `examples/DiagnosticIds.cs`, `examples/SuppressionIds.cs`, and `examples/DiagnosticCategories.cs`.

## Suppression IDs

- Value: `<PREFIX>S<NUMBER>` with the same digit count as diagnostics. The sequence is independent from
  diagnostics and has no bands: take the highest existing number plus one
  (`NextId.cs --suppression`).
- Name: PascalCase statement of what is **allowed**, from the point of view of the suppressed rule:
  `TestClassesMayBePublic`, `EventHandlersMayBeUnused`, `GeneratedCodeMayOmitDocumentation`.
  `May` reads naturally for suppressions.
- File: `SuppressionIds.cs`, separate from `DiagnosticIds.cs`, same namespace and visibility.
- Only one resource: `{Name}Justification`.

## Sharing IDs between the analyzer and code-fix projects

Code fixes need the ID values for `FixableDiagnosticIds`, and the Roslyn SDK layout keeps analyzers and
code fixes in separate assemblies (the code-fix assembly references `Microsoft.CodeAnalysis.Workspaces`,
which the command-line compiler does not load). Two arrangements are in use:

### A. ProjectReference with a public IDs class (default)

The code-fix project references the analyzer project and `DiagnosticIds` is `public`.

```xml
<!-- Contoso.Analyzers.CodeFixes.csproj -->
<ItemGroup>
  <ProjectReference Include="..\Contoso.Analyzers\Contoso.Analyzers.csproj" />
</ItemGroup>
```

Used by StyleCopAnalyzers (`StyleCop.Analyzers.CodeFixes` → `StyleCop.Analyzers`), xunit.analyzers
(`xunit.analyzers.fixes` → `xunit.analyzers`, IDs live in `public static partial class Descriptors`),
Roslynator (`Analyzers.CodeFixes` → `Analyzers`), and the Roslyn SDK "Analyzer with Code Fix" template.

Trade-off: the IDs class becomes public API of the analyzer assembly. That is harmless in practice because
both assemblies ship in the same package.

### B. Linked source file with an internal IDs class

The code-fix project compiles the IDs file directly and no ProjectReference exists.

```xml
<!-- Contoso.Analyzers.CodeFixes.csproj -->
<ItemGroup>
  <Compile Include="..\Contoso.Analyzers\DiagnosticIds.cs" Link="DiagnosticIds.cs" />
</ItemGroup>
```

Used by Meziantou.Analyzer (`internal static class RuleIdentifiers` linked from the CodeFixers project's
`Directory.Build.props`). The class can stay `internal`, but every shared file has to be linked by hand.

### C. Shared project referenced by both sides

The IDs file (and often the categories file) lives in a small class library with no Roslyn dependency,
and the analyzer, generator, and code-fix projects all reference it:

```
Tests/AnalyzerShared/AnalyzerShared.csproj   <- DiagnosticIds.cs, DiagnosticCategories.cs
Tests/TestAnalyzer/TestAnalyzer.csproj       -> ProjectReference AnalyzerShared
Tests/TestCodeFix/TestCodeFix.csproj         -> ProjectReference AnalyzerShared
```

The IDs class is `public`. Files in the shared project must not use `<see cref="...">` to Roslyn types
(CS1574), which is why `examples/DiagnosticCategories.cs` uses plain-text doc comments. The shared
assembly ships in the package next to the analyzer assemblies.

### What to do

1. Detect: `FindConventions.cs` reports `idSharing` as `ProjectReference`, `CompileInclude`,
   `SharedProject`, or `none`, and `diagnosticIdsProject` names the project that owns the IDs file.
   Follow the detected arrangement; keep the IDs class visibility consistent with it (`public` for A
   and C, `internal` acceptable for B).
2. If `none` and a code-fix project exists, ask which arrangement to use and recommend A. Then add the
   ProjectReference (A) or the `Compile Include` (B) to the code-fix project.
3. If no code-fix project exists, do nothing beyond creating the IDs file; visibility follows the config
   (`idSharing`) or defaults to `public`.

Source generators that report diagnostics live in the generator assembly and define their own descriptors
there; they reference `DiagnosticIds` from the same assembly when the generator and analyzers share a
project, or through arrangement A/B when they do not.
