# DiagnosticDescriptor and SuppressionDescriptor Patterns

## Where the descriptor lives

The `DiagnosticDescriptor` is a `private static readonly` field **inside the analyzer or generator class
that reports it**, never in the IDs file. Its field name equals the ID name. The IDs file stays a flat
list of constants so it remains a one-page overview.

```csharp
private static readonly DiagnosticDescriptor DisposableFieldShouldBeDisposed = new(
    id: DiagnosticIds.DisposableFieldShouldBeDisposed,
    title: new LocalizableResourceString(nameof(Resources.DisposableFieldShouldBeDisposedTitle), Resources.ResourceManager, typeof(Resources)),
    messageFormat: new LocalizableResourceString(nameof(Resources.DisposableFieldShouldBeDisposedMessage), Resources.ResourceManager, typeof(Resources)),
    category: DiagnosticCategories.Design,
    defaultSeverity: DiagnosticSeverity.Warning,
    isEnabledByDefault: true,
    description: new LocalizableResourceString(nameof(Resources.DisposableFieldShouldBeDisposedDescription), Resources.ResourceManager, typeof(Resources)),
    helpLinkUri: "https://github.com/contoso/analyzers/blob/main/docs/rules/CTS1001.md");
```

Use named arguments so the parameter order is obvious. Omit `helpLinkUri` when no documentation file is
created (the parameter defaults to `null`). Omit `customTags` unless one is required (below).

After adding the field, add it to the `SupportedDiagnostics` array of the same class. The analyzer will
throw at runtime if it reports a descriptor that is not listed there. For source generators there is no
`SupportedDiagnostics`; the descriptor only needs to exist where `context.ReportDiagnostic` uses it.

## Follow the repository's existing pattern

Before writing a descriptor, read one existing descriptor in the target project and copy its shape:

| Observed pattern | What to do |
|------------------|------------|
| Direct `new DiagnosticDescriptor(...)` / target-typed `new(...)` | Same, with the same argument style (named vs positional). |
| Helper such as `DescriptorFactory.Create(...)`, `CreateDescriptor(...)`, `Rule(...)` | Call the helper with the same arguments the neighbours use. |
| Descriptors collected in a central `Descriptors` class (xunit.analyzers style) | Add the property there instead of in the analyzer class, and reference it from the analyzer. |
| `LocalizableResourceString` with a different resource class name (`Strings`, `SR`, `AnalyzerResources`) | Use that class; `find-conventions` reports it as `resourceClass`. |
| `Resources.ResourceManager` accessed through a helper (`ResourceHelper.GetLocalizable(...)`) | Use the helper. |
| No descriptor exists yet, but the resource class has a hand-written partial (`localizableStringHelper` / `localizableStringProperties`) | Follow "Localizable strings" below. |
| Initialising `SupportedDiagnostics` | Use `ImmutableArray.Create(...)`. A collection expression `[ a, b ]` needs `LangVersion` 12+ **and** a `System.Collections.Immutable` with `CollectionBuilderAttribute`, which a bare `netstandard2.0` project may not have. Switch only when the project's analyzers ask (IDE0303) and it compiles. |

## Localizable strings

Where the `LocalizableResourceString` for title / message / description is built depends on what the
resource class already offers. `find-conventions` reports, per resx group, `localizableStringHelper`
(method, `accessibility`, file) and `localizableStringProperties` (existing properties, `style`,
`nestedClass`, file). Pick the first matching row:

| Situation | What to write |
|-----------|---------------|
| `localizableStringProperties` exists | Add properties for the new strings to the same class in the same file, mirroring the existing ones (name, initializer form, comment grouping). Reference them from the descriptor: `title: Resources.Localizable.{Name}Title` for `style: nested`, `Resources.{Name}Title{suffix}` for `style: suffix`. |
| Helper exists and is **`private`**, no properties yet | The author intends the properties pattern. Create `public static class Localizable` nested in the partial `Resources` class **in the helper's file**, add the properties there, and reference them as above. See `examples/Resources.Roslyn.cs`. Do not change the helper's accessibility. |
| Helper exists and is `internal` / `public` | Call it: `title: Resources.GetLocalizableResourceString(nameof(Resources.{Name}Title))`. |
| Neither | Mirror the neighbouring descriptor; with no neighbour use `new LocalizableResourceString(nameof(Resources.{Name}Title), Resources.ResourceManager, typeof(Resources))`. |

Property shape for the nested class (one per resx entry, same name as the entry; `nameof` resolves to
the generated `string` property of the outer class):

```csharp
public static class Localizable
{
    // DisposableFieldShouldBeDisposed (CTS1001)
    public static LocalizableResourceString DisposableFieldShouldBeDisposedTitle { get; } =
        GetLocalizableResourceString(nameof(DisposableFieldShouldBeDisposedTitle));
    // ...Message, ...Description follow
}
```

Keep the properties grouped per diagnostic under a `// {Name} ({ID})` comment, in ID order, matching the
resx. Use `{ get; } =` initializers (created once) rather than `=>` bodies.

`using` directives follow the file they are added to: match the neighbours' order and grouping (commonly
`System` first, then third-party, then `Microsoft.CodeAnalysis`), and honour
`dotnet_separate_import_directive_groups` in `.editorconfig` or `.globalconfig` when it is set. A new
file copies the layout of the closest existing one in the same project.

If the target analyzer class does not exist yet, create the smallest valid class (see
`examples/AnalyzerWithDescriptor.cs`): the `[DiagnosticAnalyzer(LanguageNames.CSharp)]` attribute, the
descriptor, `SupportedDiagnostics`, and an `Initialize` that calls `EnableConcurrentExecution()` and
`ConfigureGeneratedCodeAnalysis(...)` and nothing else. Do not implement the analysis; that is a separate
task.

## Descriptor fields

### title, messageFormat, description

All three are `LocalizableResourceString` instances pointing at resx entries named
`{Name}Title`, `{Name}Message`, `{Name}Description`. Never pass plain string literals (RS1007). Content
guidelines that keep `Microsoft.CodeAnalysis.Analyzers` quiet:

| Field | Guideline | Analyzer rule |
|-------|-----------|---------------|
| Title | Short noun phrase or normative sentence without a trailing period, no line breaks. Sentence case. Example: `Disposable field should be disposed` | RS1031 |
| Message | One sentence, no trailing period, no line breaks; a multi-sentence message may end with a period. Use `{0}`, `{1}` placeholders for arguments and quote symbol names as `'{0}'`. Example: `Field '{0}' is IDisposable but is never disposed` | RS1032 |
| Description | One or more full sentences ending with punctuation. Explains *why* and hints at the fix. Example: `Types that own IDisposable fields should dispose them in their own Dispose method.` | RS1033 |

Ask the user which arguments the message takes (symbol name, type name, member name, count). Record the
argument order in a comment above the descriptor when there are two or more arguments, because the
`Diagnostic.Create` call sites must pass them in the same order.

### category

A string constant from the categories class (`DiagnosticCategories.Design`). Infer the category from the
description when it is obvious (naming rule → Naming, allocation → Performance, misuse of an API → Usage,
type shape → Design, security → Security, maintainability → Maintainability, reliability → Reliability).
Ask when unsure or when the repository's categories do not match those names. The category also picks
the ID band (see `id-conventions.md`).

### defaultSeverity

Always ask. Offer this guidance when the user wants a recommendation:

| Severity | Use for |
|----------|---------|
| `Error` | Code that will fail at runtime or violates a hard contract; the build must break. |
| `Warning` | Likely bugs and important guidelines; the default for most rules. |
| `Info` | Style and suggestions shown as a lightbulb without squiggles. |
| `Hidden` | Diagnostics that only drive a code fix or refactoring and should not be shown. |

### isEnabledByDefault

`true` unless the user says otherwise. Opt-in rules (`false`) still need the AnalyzerReleases entry; note
the opt-in status in the documentation.

### helpLinkUri

Set to the full URL of the rule's documentation file (from `doc-url`) when documentation is
created. Omit the argument when it is not. Do not point at a page that does not exist.

### customTags

Pass a tag only when Roslyn or the IDE requires it for correct behaviour; otherwise omit the argument.

| Tag | Required when |
|-----|---------------|
| `WellKnownDiagnosticTags.CompilationEnd` | The diagnostic is reported from a `RegisterCompilationEndAction` callback. Without it the IDE does not show the diagnostic in live analysis and RS1037 warns. |
| `WellKnownDiagnosticTags.Unnecessary` | The diagnostic marks code that is unnecessary and should be faded in the IDE (unused usings, redundant casts). Must be combined with reporting `Unnecessary` locations properly. |
| `WellKnownDiagnosticTags.NotConfigurable` | The severity must not be changeable through `.editorconfig` or rule sets; used for diagnostics that guard correctness of generated code. Use sparingly. |
| `WellKnownDiagnosticTags.CustomObsolete` | The diagnostic represents an obsoletion and should be treated like `[Obsolete]` by the IDE. |

Never use `Compiler`, `Telemetry`, `AnalyzerException`, `Build`, or `EditAndContinue`; those are reserved for
the compiler and the host.

## Descriptors in source generators

Generators report diagnostics with `context.ReportDiagnostic(Diagnostic.Create(descriptor, location, args))`.
Define the descriptor the same way, as a `private static readonly` field in the generator class (or in a
`static class Diagnostics` nested in it when the generator has many). Generator diagnostics conventionally
live in their own band (for example `9xxx`) and are usually `Error` severity because they describe input
the generator cannot process. The `DiagnosticDescriptor` goes into `AnalyzerReleases.Unshipped.md` exactly
like an analyzer's.

## SuppressionDescriptor

```csharp
private static readonly SuppressionDescriptor TestClassesMayBePublic = new(
    id: SuppressionIds.TestClassesMayBePublic,
    suppressedDiagnosticId: "CA1515",
    justification: new LocalizableResourceString(nameof(Resources.TestClassesMayBePublicJustification), Resources.ResourceManager, typeof(Resources)));

public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions { get; } =
    ImmutableArray.Create(TestClassesMayBePublic);
```

- Field name equals the `SuppressionIds` constant name.
- `suppressedDiagnosticId` is the ID of the *other* rule (`CA1515`, `IDE0051`, `CS8618`, or one of the
  repository's own diagnostics). Ask for it if the description does not say.
- `justification` is a full sentence explaining why the suppressed rule does not apply; it appears in the
  IDE. It is the only resource: `{Name}Justification`.
- No `helpLinkUri`, no category, no severity, no AnalyzerReleases entry.
- Add the descriptor to `SupportedSuppressions`.

See `examples/SuppressorWithDescriptor.cs`.

## Rules from Microsoft.CodeAnalysis.Analyzers that touch descriptors

| Rule | Meaning | How the skill avoids it |
|------|---------|-------------------------|
| RS1007 | Provide localizable arguments to descriptor constructor | Always `LocalizableResourceString`. |
| RS1015 | Provide non-null `helpLinkUri` | Off by default; expected when docs are skipped. |
| RS1017 | `DiagnosticId` must be a non-null constant | Use the `const string` from the IDs file. |
| RS1031 / RS1032 / RS1033 | Title / message / description format | Follow the table above. |
| RS1037 | Add `CompilationEnd` tag to compilation-end diagnostics | `customTags` table above. |
| RS2000 | Add analyzer diagnostic IDs to analyzer release | Append to `AnalyzerReleases.Unshipped.md` (see `analyzer-releases.md`). |
| RS2008 | Enable analyzer release tracking | Create the `AnalyzerReleases.*.md` pair when missing. |
