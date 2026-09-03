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
which the command-line compiler does not load). Four arrangements are in use. They are named after
**where the IDs live** and how a consumer reaches them, not after the MSBuild item that wires them up,
since three of the four can be built out of `<ProjectReference>` items:

|  | IDs in the analyzer project | IDs outside it |
|--|-----------------------------|----------------|
| **reached by a project reference** | `AnalyzerProject` | `SharedProject` |
| **reached by a linked `<Compile>`** | `LinkedFile` | `SharedFile` (a `.shproj` counts) |

### AnalyzerProject: the analyzer project owns the IDs (default)

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

### LinkedFile: the code fix compiles the IDs file

The IDs still live in the analyzer project, but the code-fix project compiles that file directly and no
reference between the projects exists, so the class can stay `internal`.

```xml
<!-- Contoso.Analyzers.CodeFixes.csproj -->
<ItemGroup>
  <Compile Include="..\Contoso.Analyzers\DiagnosticIds.cs" Link="DiagnosticIds.cs" />
</ItemGroup>
```

Used by Meziantou.Analyzer (`internal static class RuleIdentifiers` linked from the CodeFixers project's
`Directory.Build.props`). The class can stay `internal`, but every shared file has to be linked by hand.

### SharedProject: a third project owns the IDs

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

### SharedFile: no project owns the IDs

The IDs file sits in a directory that is not a project at all, and every project that needs it links the
file, so the constants are compiled into each assembly separately:

```
src/Shared/DiagnosticIds.cs                   <- belongs to no project
src/Contoso.Analyzers/*.csproj                -> <Compile Include="..\Shared\DiagnosticIds.cs" Link="DiagnosticIds.cs" />
src/Contoso.Analyzers.CodeFixes/*.csproj      -> the same item
```

The class can stay `internal`, since each assembly has its own copy. No extra assembly ships, and there
is no reference between analyzer and code fix, but the `<Compile>` item has to be repeated in every
project (a `Directory.Build.props` next to the shared folder can carry it once). The IDs are not part of
any public API, so consumers cannot reference them; that only matters if the package is meant to expose
its ID constants.

A Visual Studio **shared project** (`.shproj` with its `.projitems`) is the same arrangement with the
item list factored out, and is detected as `SharedFile` too:

```xml
<!-- Contoso.Analyzers.csproj and Contoso.Analyzers.CodeFixes.csproj alike -->
<Import Project="..\Shared\Shared.projitems" Label="Shared" />
```

It builds: MSBuild follows the import and the shared files land in each consumer's `Compile` items, so
`dotnet build` on the consuming projects works. Two caveats. The `.shproj` itself is a container that
produces nothing, so building it directly fails with `MSB4040: The project does not have targets` — that
is by design, not a misconfiguration. And `.shproj`/`.projitems` are Visual Studio artifacts in the old
MSBuild namespace, with no `dotnet new` template and uneven support outside Visual Studio. Prefer plain
linked `<Compile>` items for a new repository, and keep the shared project when the repository already
has one.

### What to do

1. Detect: `FindConventions.cs` reports `idSharing` and `diagnosticIdsProject`, the project that owns
   the IDs file. A repository whose analyzer and code fix both reference a neutral project is
   `SharedProject`, even though every wire in it is a `<ProjectReference>`:

   | Value | The IDs file belongs to | The code-fix project | IDs class visibility |
   |-------|-------------------------|----------------------|----------------------|
   | `AnalyzerProject` | the analyzer project | references the analyzer project | `public` |
   | `LinkedFile` | the analyzer project | compiles that file through a linked `<Compile>` item | `internal` is fine |
   | `SharedProject` | a third project (an ordinary class library; no `.shproj` required) | references that third project, as the analyzer does | `public` |
   | `SharedFile` | no project; each side links the file, directly or through a `.shproj`'s `.projitems` | compiles that file through a linked `<Compile>` item | `internal` is fine |
   | `none` | nowhere the code-fix project can see | needs one of the above | — |

   Follow the detected arrangement and keep the visibility consistent with it.
2. If `none` and a code-fix project exists, ask which arrangement to use and recommend `AnalyzerProject`.
   Then add the `<ProjectReference>` or the linked `<Compile>` item to the code-fix project. When the IDs
   file already sits outside every project, `SharedFile` is the arrangement it is asking for: add the
   linked `<Compile>` item rather than moving the file.
3. If no code-fix project exists, do nothing beyond creating the IDs file; visibility follows the config
   (`idSharing`) or defaults to `public`.

Source generators that report diagnostics live in the generator assembly and define their own descriptors
there; they reference `DiagnosticIds` from the same assembly when the generator and analyzers share a
project, or through one of the arrangements above when they do not.
