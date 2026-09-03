---
name: add-diagnostic
description: This skill should be used when the user asks to "add a diagnostic", "add a new rule", "add an analyzer rule", "create a DiagnosticDescriptor", "add a diagnostic ID", "register a new diagnostic", "add a suppression", "add a DiagnosticSuppressor", "診断を追加", or describes a new warning, error, or suppression a Roslyn analyzer, source generator, or suppressor should report. Adds the ID constant, descriptor, resx strings, AnalyzerReleases.Unshipped.md row, and optional rule docs following repository conventions. Not for implementing analysis logic, code fixes, or tests.
argument-hint: <what the diagnostic should report, in plain language>
allowed-tools: Read, Write, Edit, Glob, Grep, Bash, AskUserQuestion
---

# Add a Diagnostic

Add every artifact a new Roslyn diagnostic (or suppression) needs, in one pass, without implementing the
analysis itself. The deliverable is a consistent set of edits:

| Artifact | Diagnostic | Suppression |
|----------|------------|-------------|
| ID constant in the IDs file | `DiagnosticIds.cs` | `SuppressionIds.cs` |
| Descriptor in the reporting class | `DiagnosticDescriptor` + `SupportedDiagnostics` | `SuppressionDescriptor` + `SupportedSuppressions` |
| resx strings (every culture file) | `{Name}Title`, `{Name}Message`, `{Name}Description` | `{Name}Justification` |
| `AnalyzerReleases.Unshipped.md` row | yes | no |
| Documentation page + index row | optional (drives `helpLinkUri`) | only if the repo already documents suppressions |

`$ARGUMENTS` is a plain-language description of what the diagnostic should report. When it is empty, ask
for it before anything else.

## Scripts

Four file-based C# apps live in `scripts/` and need the .NET 10 SDK, 10.0.300 or later (they share
`Common.cs` through `#:include`, which older SDKs do not support). Run them with
`dotnet <file>.cs -- <options>`; the first run compiles and later runs are cached. From Bash:

```bash
S="${CLAUDE_PLUGIN_ROOT}/skills/add-diagnostic/scripts"
dotnet "$S/FindConventions.cs" -- --path .
dotnet "$S/NextId.cs" -- --ids-file src/X/DiagnosticIds.cs --category Usage
dotnet "$S/NextId.cs" -- --ids-file src/X/DiagnosticIds.cs --prefix ABC --band 1   # fresh repository
dotnet "$S/NextId.cs" -- --ids-file src/X/SuppressionIds.cs --suppression
dotnet "$S/AddResxEntries.cs" -- --resx src/X/Resources.resx --resx src/X/Resources.ja.resx --ids-file src/X/DiagnosticIds.cs --entries entries.json
dotnet "$S/DocUrl.cs" -- --doc docs/rules/ABC1001.md
```

(In PowerShell use `$env:CLAUDE_PLUGIN_ROOT`; when the variable is unset, use the directory containing
this SKILL.md.) All four print JSON. Never guess what a script would return; run it and read the output.

## Workflow

### Step 1: Detect conventions

Run `FindConventions.cs` from the repository root and read the JSON. The fields that drive every later
step:

- `diagnosticPrefix`, `diagnosticIds`, `suppressionIds` — prefix, IDs file paths, class visibility, digit
  count, existing IDs, and category bands (`// Design (ABC1xxx)` headers).
- `projects[]` — kind (`analyzer`, `codefix`, `generator`, `test`, `roslyn-component`, `other`) and
  the analyzer / suppressor / generator classes with their files.
- `resx[]` — resource groups per project, all culture files, `resourceClass`, `localizableStringHelper`
  (a hand-written `GetLocalizableResourceString(string)`-style method with its `accessibility`),
  `localizableStringProperties` (existing `LocalizableResourceString` properties, their `style`
  `nested` / `suffix`, `nestedClass`, and `file`), and `requiresVisualStudioRegeneration`.
- `docs` — `directory` and `indexFile` when rule pages already exist, plus `candidateDirectories`
  (existing `docs` / `doc` / `Documentation` / `wiki` / `rules` folders, shallowest first),
  `mentionFiles` (Markdown elsewhere that names existing IDs), and `suggestedDirectory` (where a new
  page should go, `docs/rules` when nothing exists).
- `analyzerReleases[]`, `git.docUrlTemplate`, `idSharing` (`ProjectReference`, `CompileInclude`,
  `SharedProject`, or `none`), `diagnosticIdsProject`, `config`.

When something is missing, create only that piece: ask for the prefix only when `diagnosticPrefix` is
null (no config, no existing IDs, no band header carrying one; an empty IDs file is not enough); ask
for the analyzer project only when none is detected; create resx / categories / releases files alongside
the existing ones. New IDs files follow the *structure* of `examples/DiagnosticIds.cs`,
`examples/SuppressionIds.cs`, and `examples/DiagnosticCategories.cs` with the user's namespace and prefix,
one band header for the new category, and none of the sample constants (defaults in
`references/id-conventions.md`).

Read one existing descriptor in the target project (if any) before writing a new one; the new code must
look like its neighbours (helper methods, argument style, resource class). Read `config.notes` and follow
any instructions found there.

### Step 2: Classify the request

Decide whether the description asks for a **diagnostic** (report something) or a **suppression** (stop
another rule from reporting in a situation). Words such as "suppress", "should not warn about", "CA1515
is wrong for test classes" mean suppression. When unclear, ask.

### Step 3: Design the diagnostic

Draft before asking, so the user reviews concrete text rather than open questions:

1. **Name**: PascalCase normative statement (`TaskShouldBeAwaited`,
   `AbstractTypeShouldNotHavePublicConstructor`; `TestClassesMayBePublic` for suppressions). Rules and
   examples are in `references/id-conventions.md`.
2. **Category**: infer from the description when obvious (naming, usage, design, performance, ...) and
   match it to the repository's existing categories; leave it open when not obvious.
3. **Title / Message / Description** (or **Justification**): draft following the format rules in
   `references/descriptors.md` (title without trailing period, message with `'{0}'` placeholders and no
   trailing period, description as full sentences).
4. **Target class**: the analyzer, generator, or suppressor class that will report it; propose an existing
   one when the description matches, otherwise propose a new class name.
5. **Documentation**: propose *yes* with the target path, always. Use `docs.directory` when it exists,
   otherwise `docs.suggestedDirectory` (a missing folder is created, not a reason to skip). Check
   `docs.mentionFiles` first: when rules are documented in one shared page or a README table, propose
   extending that file instead of creating a new layout.

Then print the proposal as a short table in the message (name, ID band/category, title, message,
description, target class, documentation yes/no) and, directly after it, ask in one `AskUserQuestion`
round (at most four questions) only for what the user must decide: severity (unless the request
already states it), category (when not inferred), message arguments (which values fill `{0}`, `{1}`),
documentation (always, offering the proposed path and "skip"; never decide it silently), and
`suppressedDiagnosticId` for suppressions. Keep question texts short; the table already carries
the context, and the user can correct any row of it in the "Other" answer. Skip the round entirely
when the request already settles everything. Re-draft once from the answers; do not loop.

Apply `customTags` only when required (`CompilationEnd`, `Unnecessary`, `NotConfigurable`; see
`references/descriptors.md`), otherwise omit the argument.

### Step 4: Allocate the ID

Run `NextId.cs` with `--ids-file` and `--category <name>` (diagnostic) or `--suppression` (suppression).
When the output has `"unresolvedCategory": true`, the category has no band yet: choose the next unused
band digit, add the `// <Category> (<PREFIX><n>xxx)` header to the IDs file first, then re-run with
`--category <name>`. The script reads both the band and the prefix from that header, so an empty IDs
file needs nothing more once the header is written; `--prefix` / `--band` are only for running before
the header exists. Never renumber or reuse an existing value.

### Step 5: Apply the edits

Perform the edits in this order (5a–5h) so later edits can rely on earlier ones:

- **5a. IDs file**: insert `public const string {Name} = "{Value}";` inside the category block, sorted by
  number (`references/id-conventions.md`, "Layout of the IDs file"). No `#region`. Create
  `DiagnosticIds.cs` / `SuppressionIds.cs` when missing (see Step 1 for how to derive them from
  `examples/`).
- **5b. Categories class** (diagnostics only): add the constant when the category is new.
- **5c. Descriptor**: when documentation was requested, run `DocUrl.cs --doc <intended page path>` now
  (it needs only the path, not the file) and pass the URL as `helpLinkUri`; omit the argument otherwise.
  Decide how the strings reach the descriptor, in this order (`references/descriptors.md`,
  "Localizable strings"):
    1. `localizableStringProperties` exists → add the three properties (one for a suppression) in the
       same file, class, and style, then reference them (`Resources.Localizable.{Name}Title`).
    2. Otherwise `localizableStringHelper` exists and is `private` → create the nested
       `public static class Localizable` in the helper's file (`examples/Resources.Roslyn.cs`) with the
       properties, and reference them. Never widen the helper's accessibility.
    3. Otherwise the helper is `internal`/`public` → call it directly.
    4. Otherwise mirror the neighbouring descriptor, or fall back to `new LocalizableResourceString(...)`.
  Add the `private static readonly` field named `{Name}` to the target class, mirroring the neighbouring
  descriptor's style. Add the field to `SupportedDiagnostics` / `SupportedSuppressions` (source
  generators have neither; the field alone suffices), using a collection expression `[ ... ]` when
  `LangVersion` is 12 or later. When the message has two or more placeholders, add a comment
  above the descriptor listing the argument order. Create the class from
  `examples/AnalyzerWithDescriptor.cs` or `examples/SuppressorWithDescriptor.cs` when it does not exist;
  leave `Initialize` / `ReportSuppressions` without analysis logic.
- **5d. resx**: identify the resource group that holds diagnostic strings (the one already containing
  `*Title` / `*Message` entries; ask when several qualify), write the entries to a JSON file in the
  scratchpad, and run `AddResxEntries.cs` once, listing **all culture files** of that group, with
  `--ids-file` pointing at the IDs file edited in 5a. Same English text in every culture file. Read the
  report: stop and restore the file with `git checkout --` if `valid` is false. Never edit
  `*.Designer.cs` (`references/resources.md`).
- **5e. AnalyzerReleases.Unshipped.md** (diagnostics only): append the row
  `ID | Category | Severity | <short sentence describing the rule>` under `### New Rules`; create the
  Shipped/Unshipped pair from `examples/` when missing (`references/analyzer-releases.md`). Skip for
  suppressions.
- **5f. Documentation** (when requested): create the directory when it does not exist, then the page at
  the path used in 5c, following the newest existing page or `examples/rule-doc-template.md` when there
  is none; add the index row in sorted position, creating the index from
  `examples/rules-index-template.md` when the directory is new (`references/documentation.md`). For
  suppressions, create pages only when `docs.suppressionDocs` or a suppressions table already exists.
- **5g. ID sharing** (only when a code-fix project exists and `idSharing` is `none`; skip for
  `ProjectReference`, `CompileInclude`, and `SharedProject`): ask ProjectReference (recommended) or
  Compile Include, then add the reference to the code-fix project and set the IDs class visibility
  accordingly.
- **5h. Config file**: create or update `.claude/roslyn-skills.md` (`examples/roslyn-skills.md`) only
  when a decision was made that detection cannot reproduce next time (new prefix, new band, non-GitHub
  URL template, unusual docs layout).

Match each file's existing indentation, line endings, and blank-line pattern. Use `Edit` for insertions
into existing files; use `Write` only for new files.

### Step 6: Verify

- Re-run `FindConventions.cs` and confirm the new ID appears under `diagnosticIds.ids` /
  `suppressionIds.ids`, and the resx report was `valid`.
- Grep the target project for the new name: the IDs file, the descriptor, `SupportedDiagnostics` (or
  `SupportedSuppressions`), and every resx must contain it.
- Build the analyzer project (`dotnet build <csproj>`) **only when** `requiresVisualStudioRegeneration` is
  false; when it is true the build is expected to fail with CS0117 until Visual Studio regenerates
  `Resources.Designer.cs`, so skip the build and say so. Fix every warning the new code introduced
  (compare against a build before the change when unsure); under `EnforceCodeStyleInBuild` with
  `AnalysisLevel` `latest-all`, typical ones are IDE0303 (use a collection expression) and CS1574
  (unresolvable `cref` in a project without Microsoft.CodeAnalysis).

### Step 7: Report

Summarize in a short list: the ID and name, every file changed or created, the `helpLinkUri` (or that it
was omitted), and the next steps the skill cannot do: regenerate `Resources.Designer.cs` in Visual Studio
when flagged, implement the analysis / suppression logic, add tests, and translate the satellite resx
files.

## Rules that always hold

- The IDs file contains only constants and band headers; descriptors live in the reporting class.
- The descriptor field, the ID constant, and the resx stem share one name.
- Every culture file of the resource group gets the same entries; only `.resx` files are edited.
- Never change the accessibility, signature, or name of an existing member to make new code compile; a
  `private` helper means the entry point belongs next to it, in the same class.
- Suppressions: separate IDs file, independent sequence, `Justification` only, no AnalyzerReleases row.
- `helpLinkUri` points at a page that exists, or is omitted.
- Documentation is always offered, never skipped silently; a missing documentation directory is created,
  not treated as a decision.
- Ask for severity, category, and message arguments unless the request already states them.
- Do not implement analysis logic, code fixes, or tests; offer them as follow-ups.

## Additional resources

### Reference files

- **`references/id-conventions.md`** — naming rules, prefix and band scheme, IDs file layout, suppression
  IDs, and how real projects (StyleCop, xunit, Roslynator, Meziantou) share IDs with code-fix projects.
- **`references/descriptors.md`** — DiagnosticDescriptor / SuppressionDescriptor patterns, text format
  rules (RS1031–RS1033), severity guidance, required `customTags`, source generator descriptors.
- **`references/resources.md`** — resx entry naming and ordering, `AddResxEntries.cs` usage, Designer.cs
  regeneration matrix.
- **`references/analyzer-releases.md`** — AnalyzerReleases file format, Notes column convention, RS2000
  family.
- **`references/documentation.md`** — documentation layout, page and index templates, URL resolution,
  and the `.claude/roslyn-skills.md` configuration schema.

### Examples

- **`examples/DiagnosticIds.cs`**, **`examples/SuppressionIds.cs`**, **`examples/DiagnosticCategories.cs`**
  — canonical IDs and categories files.
- **`examples/AnalyzerWithDescriptor.cs`**, **`examples/SuppressorWithDescriptor.cs`** — descriptor
  placement and `SupportedDiagnostics` / `SupportedSuppressions` wiring.
- **`examples/Resources.Roslyn.cs`** — hand-written partial of the resource class with a private helper
  and the nested `Localizable` property class.
- **`examples/AnalyzerReleases.Shipped.md`**, **`examples/AnalyzerReleases.Unshipped.md`** — release
  tracking files.
- **`examples/rule-doc-template.md`**, **`examples/rules-index-template.md`** — documentation templates.
- **`examples/roslyn-skills.md`** — configuration file with every supported key.
- **`examples/resx-entries.json`** — input format for `AddResxEntries.cs`.

### Scripts

- **`scripts/FindConventions.cs`** — repository convention detection (JSON).
- **`scripts/NextId.cs`** — next free ID in a category band or suppression sequence.
- **`scripts/AddResxEntries.cs`** — ordered resx insertion with validation.
- **`scripts/DocUrl.cs`** — documentation URL from the git remote or a template.
- **`scripts/Common.cs`** — shared helpers included by the four entry points (not run directly).
