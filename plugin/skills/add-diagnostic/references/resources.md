# String Resources (resx)

## Entries per diagnostic

| Resource name | Used by | Content |
|---------------|---------|---------|
| `{Name}Title` | `DiagnosticDescriptor.title` | Short phrase, no trailing period |
| `{Name}Message` | `DiagnosticDescriptor.messageFormat` | One sentence with `{0}` placeholders, no trailing period |
| `{Name}Description` | `DiagnosticDescriptor.description` | Full sentence(s) ending with punctuation |
| `{Name}Justification` | `SuppressionDescriptor.justification` (suppressions only) | Full sentence |

`{Name}` is the ID constant name (`DisposableFieldShouldBeDisposed`), not the value.

## Which files to edit

1. Find the resource group of the target project: `find-conventions` lists each group with its `baseName`, all culture files, the detected generator, and the resource class name.
   A project normally has one group (`Resources.resx` plus optional `Resources.ja.resx`, `Resources.de.resx`, ...).
   When a project has several groups, ask which one holds diagnostic strings, or pick the one that already contains `*Title` / `*Message` entries.
2. Add an entry to **every culture file of the group**, translated per file.
   Each `Resources.<culture>.resx` holds the message in that culture's language, which the file name names: `.ja` Japanese, `.de` German, `.fr` French, `.zh-Hans` Simplified Chinese.
   Keep the placeholders (`{0}`, `{1}`) and the quoting style identical across languages; translate the prose around them.

   The neutral file's language is **not** assumed to be English.
   `find-conventions` reports it as `resx[].neutralLanguage`, resolved in this order:

   | Source | Example |
   |--------|---------|
   | `<NeutralLanguage>` in the csproj or a `Directory.Build.props` above it | `<NeutralLanguage>ja</NeutralLanguage>` |
   | `<AssemblyAttribute Include="System.Resources.NeutralResourcesLanguageAttribute">` with `_Parameter1` | generated attribute written as an item |
   | `[assembly: NeutralResourcesLanguage("...")]` in any source file of the project | usually `AssemblyInfo.cs` |

   Write the neutral file in that language.
   When it is null the project has never declared one (CA1824 suggests adding it), so match the language of the entries already in the file, and fall back to English only when the file is empty.
   A project whose neutral language is `ja` and which also has a `Resources.ja.resx` is duplicating itself: mention it in the report rather than silently writing both.

   Two sources would answer this more authoritatively and are deliberately not used, because both require the project to have been built and the skill routinely runs on one that has not:

   | Source | Why it is not read |
   |--------|--------------------|
   | `obj/<project>.AssemblyInfo.cs`, where every declaration form ends up as `[assembly: NeutralResourcesLanguage(...)]` | Written by a build-time target, so it is absent before the first build. Measured: even after a build it never appears among an evaluation's `Compile` items, because the target that generates the file is also the one that adds it. Reading it would mean running a build or reaching into `obj/`, which the scan excludes as build output. |
   | The compiled assembly, read with `System.Reflection.Metadata` | The most reliable answer of all, since property, item and source attribute have collapsed into one by then, and it would even catch an attribute added by a source generator or a weaving step. It still needs a successful build, and `TargetPath` is empty for the outer build of a multi-targeting project, so locating the assembly is its own problem. |

   The three declaration sites cover every way the attribute is written by hand, which is what matters here.
   Reading the build output would only add cases where something else generated the attribute.

   Never write the source text into a satellite file.
   A missing entry falls back to the neutral resource and is visibly untranslated, but source text sitting in `Resources.ja.resx` looks finished and stays wrong.
   When a translation cannot be produced with confidence, leave that file out and say so in the report instead.
3. Edit only the `.resx` files.
   Never edit `*.Designer.cs`.

## Ordering inside the file

Entries are kept in **ID value order**, and within one diagnostic in the order Title → Message → Description (Justification alone for suppressions).
Existing entries are left where they are; new entries go after the last entry whose ID sorts before them.
`add-resx-entries` resolves resource names to ID values through the IDs file and computes the insertion point; pass `--ids-file` so it can do that (pass the suppression IDs file when adding a Justification).

## Adding entries

Because each culture file gets different text, run the command once per file with that file's own entries JSON:

```bash
T="dotnet tool exec Aetos.RoslynSkills.Tools@0.1.6 -- add-diagnostic"
$T add-resx-entries --resx /repo/src/A/Resources.resx \
  --ids-file /repo/src/A/DiagnosticIds.cs --entries /scratch/entries.en.json
$T add-resx-entries --resx /repo/src/A/Resources.ja.resx \
  --ids-file /repo/src/A/DiagnosticIds.cs --entries /scratch/entries.ja.json
```

Passing several `--resx` files in one call is still supported, and correct only when the text is identical in all of them, which for diagnostic strings it never is.

Options: `--resx` (repeatable, or comma-separated), `--entries` (JSON array or path to a JSON file), `--ids-file`, `--force`, `--validate-only`.

The command:

- preserves the file's BOM, line endings, indentation, and existing element order;
- inserts `<data name="..." xml:space="preserve"><value>...</value></data>` at the computed position;
- skips names that already exist (use `--force` to overwrite their values);
- reloads each file and verifies that it is well-formed XML with root `<root>`, that no `data` name is duplicated, and that every new entry is present with a `<value>`;
- reports whether a sibling `*.Designer.cs` exists and lacks the new names (`designerStale`);
- exits with code 1 when any file fails validation.

Copy every culture file to the scratchpad before running the command.
Read the JSON report; if `valid` is false, restore from those copies and fix the cause before retrying.
Never leave a broken resx behind, and never recover with `git checkout --`: a resx routinely holds uncommitted work that git would discard along with the failed insertion.

For a one-off manual edit, insert the same XML by hand and run the command with `--validate-only` afterwards.

## Placeholder comments

A translator sees only the string, so `Field '{0}' is IDisposable but is never disposed` gives no hint about what `{0}` holds or whether the order can change in another language.
The neutral file carries that information in the `comment` field of each entry that has a placeholder:

```json
{
  "name": "CancellationTokenShouldBeForwardedMessage",
  "value": "Forward the cancellation token '{0}' to the call to '{1}'",
  "comment": "{0} is the cancellation token parameter name. {1} is the name of the method being called."
}
```

Rules:

- One sentence per placeholder, in numeric order, naming what the argument holds, not its type (`{0} is the field name`, not `{0} is a string`).
  Say which symbol it comes from when that is not obvious from the message.
- Neutral file only, whatever language that file is in.
  Satellite files get no comments: the comment describes the source string, and duplicating it into every culture leaves copies to drift.
- No placeholder, no comment.
  `Title` and `Description` usually take none.
- The comment is not written into `AnalyzerReleases.*.md` or the documentation page; those describe the rule, not its message arguments.
  It does, however, match the argument-order comment placed above the descriptor in 6c, so keep the two consistent.

## Designer.cs and build errors

The descriptor code uses `nameof(Resources.XxxTitle)`, so the strongly typed resource class must expose the new property, otherwise the project fails to compile.
Who generates that class decides what happens next:

| Generator (as detected) | Effect of adding a resx entry | Action |
|-------------------------|-------------------------------|--------|
| `ResXFileCodeGenerator` / `PublicResXFileCodeGenerator` (`<Generator>` on the `EmbeddedResource`, `Resources.Designer.cs` checked in) | `Designer.cs` is regenerated only by Visual Studio when the resx is saved in its editor. Until then the build fails with CS0117. | Tell the user: open the resx in Visual Studio and save it (or run the custom tool) to regenerate `Resources.Designer.cs`, then commit both files. Do not hand-edit the Designer file. |
| `Microsoft.CodeAnalysis.ResxSourceGenerator` package reference (a `PackageReference`, or a `GlobalPackageReference` in `Directory.Packages.props`; an empty `<Generator></Generator>` on the `EmbeddedResource` is typical alongside it) | The class is generated at build time from the resx. | Nothing; the next build picks up the entry. A hand-written partial (`resx[].localizableStringHelper`, `resx[].localizableStringProperties`) decides how descriptors obtain the strings; see `descriptors.md`, "Localizable strings". |
| MSBuild `GenerateResource` with `StronglyTypedFileName` / `StronglyTypedClassName` metadata, or `Generator="MSBuild:Compile"` | Generated at build time into `obj/`. | Nothing. See "Creating a new resx file" for the metadata and its two naming traps. |
| Unknown (no Designer file, no generator metadata) | Possibly the project reads resources through a custom `ResourceManager` wrapper. | Look at how existing descriptors reference resources and mirror it; if `nameof(Resources.X)` is used and no generator is found, ask the user how the class is produced. |

`find-conventions` sets `requiresVisualStudioRegeneration` to `true` for the first row.
Always mention this in the final summary when it applies; it is the one step the skill cannot do itself.

## Creating a new resx file

Which file the resx route uses, once SKILL.md Step 4.6 has settled that the strings go to resx:

| The target project has | The file |
|------------------------|----------|
| no resx at all | a new `Resources.resx` beside the descriptor — created without asking only when the request itself asked for resx, and otherwise the question, since creating a file and registering it in the csproj is not something a reading of the repository can consent to |
| resx, but no group holding diagnostic strings | ask: share that file, or create a new `Resources.resx`. Moving in with somebody else’s strings is the user’s call, never assumed |
| exactly one group holding diagnostic strings (`*Title` / `*Message` entries), whether or not other groups exist | that group, without asking |
| several groups with diagnostic strings spread across them | ask, offering the likeliest and saying the others exist |

The likeliest is the group holding the strings of existing diagnostics in the **same category** as the new one, and failing that the group holding the most diagnostic strings.
Never pick silently when they are spread: `add-resx-entries` runs once per culture file, so a wrong silent pick has written entries into every culture file of the wrong group before anyone sees them.
Name the file in the option text whenever a row above asks, so the answer is also the consent to create or share it.

Create a new file only where that table calls for one; never on the skill's own reading of the repository.
Write the file itself from the neutral file of an existing group (same `resheader` block, same declaration), or from the minimal ResX skeleton when the repository has none, then **register it in the csproj**.
The SDK's default glob already embeds it, so the item exists to carry metadata and is written as `Update`, never `Include`:

```xml
<ItemGroup>
  <EmbeddedResource Update="Resources.resx">
    ...metadata...
  </EmbeddedResource>
</ItemGroup>
```

Which metadata depends on what the **repository** already does — look at every group in `resx[]`, not only the target project's, since the answer is a house style rather than a per-project one:

| The repository already uses | Metadata | What it costs |
|-----------------------------|----------|---------------|
| `ResXFileCodeGenerator` / `PublicResXFileCodeGenerator` (`resx[].generator`) | `<Generator>ResXFileCodeGenerator</Generator>` and `<LastGenOutput>Resources.Designer.cs</LastGenOutput>` | The class does not exist until Visual Studio writes it, so the project fails with CS0117 until then: treat it exactly like `requiresVisualStudioRegeneration`, skip the build, and say so in the report. |
| `Microsoft.CodeAnalysis.ResxSourceGenerator` (`projects[].usesResxSourceGenerator`, and an empty `resx[].generator`) | `<Generator></Generator>`, left empty so no custom tool is attached later | Nothing, provided that project already references the package. When it does not, adding it is a package reference the user has to agree to: ask rather than adding one. |
| neither | the `StronglyTyped*` metadata below | Nothing beyond the caveats below; MSBuild's own `GenerateResource` writes the class into `obj/` at build time, so no IDE and no package is involved. |

Culture files take no `StronglyTyped*` metadata and no generator: one class is generated from the neutral file and serves them all.

### The MSBuild fallback

```xml
<EmbeddedResource Update="Resources.resx">
  <Generator></Generator>
  <StronglyTypedLanguage>CSharp</StronglyTypedLanguage>
  <StronglyTypedNamespace>Sample.Analyzers</StronglyTypedNamespace>
  <StronglyTypedClassName>Resources</StronglyTypedClassName>
  <StronglyTypedFileName>$(IntermediateOutputPath)Resources.Designer.cs</StronglyTypedFileName>
</EmbeddedResource>
```

Three things about it decide how the descriptor is written and whether the resources are found at all.

**The generated class is `internal class`, neither `partial` nor `static`.**
So the hand-written partial patterns are unavailable on a file created this way: the descriptor reaches the strings as `new LocalizableResourceString(nameof(Resources.{Name}Title), Resources.ResourceManager, typeof(Resources))`, which works because `ResourceManager` is `internal` and the descriptor lives in the same assembly (`descriptors.md`, "Localizable strings", last row).
`<PublicClass>true</PublicClass>` beside the other metadata makes the class and its members public, the `PublicResXFileCodeGenerator` equivalent.
Leave it out: the descriptor lives in the same assembly, and a public resource class is API the repository has to keep.

**The name the generated code looks up is `StronglyTypedNamespace` + class name, while the embedded name is `RootNamespace` + folder + file name.**
They agree only when the resx sits directly in the project directory.
For `Sub/Resources.resx` the build still succeeds and the first lookup throws `MissingManifestResourceException` at run time, which for an analyzer means it throws inside the host.
Either put the file in the project directory, or add `<LogicalName>Sample.Analyzers.Resources.resources</LogicalName>` to force the embedded name to match.

**A culture file's name is never checked at all.**
`Sub/Resources.ja.resx` embeds as `...Sub.Resources.ja.resources`, the `ja` lookup finds nothing, and `ResourceManager` silently falls back to the neutral string — a wrong language with no error anywhere.
When `LogicalName` is used for the neutral file, every culture file needs its own (`Sample.Analyzers.Resources.ja.resources`).

`<Generator></Generator>` is written empty on purpose: it stops Visual Studio from attaching `ResXFileCodeGenerator` the first time somebody opens the file, which would add a second, checked-in copy of the same class.

Generation happens during the build, and the generated file lives in `obj/`, so nothing is checked in and **the class does not exist until the project is built once**.
Before that first build the editor reports the descriptor's `Resources` as undefined, and so does a fresh clone that has not been built.
That resolves itself rather than needing a separate step: `GenerateResource` runs as part of `PrepareResources`, ahead of `CoreCompile`, so one build both writes the class and compiles against it — measured from a cleaned `obj/` and `bin/`, with the descriptor already referencing the class.
The workflow builds in 6e (or, for suppressions, in Step 7), which is that build.
Say so in the Step 8 report anyway, because the next person to open the repository sees the red editor before they see a build.
`Microsoft.CodeAnalysis.ResxSourceGenerator` behaves the same way; only `ResXFileCodeGenerator`, whose `Designer.cs` is checked in, does not.

## Editing rules for the XML

- Keep `xml:space="preserve"` on every `data` element; Visual Studio adds it and the ResX reader relies on it for leading/trailing whitespace.
- Escape `<`, `>`, `&` in values (the tool does this through the XML DOM).
- Add a `<comment>` to every entry in the **neutral** file whose text contains a placeholder; see "Placeholder comments" below.
  Leave satellite files without comments, and do not add comments to entries that have no placeholder.
- Do not touch the `resheader` elements or any `metadata`/`assembly` nodes.
- Keep the declaration `<?xml version="1.0" encoding="utf-8"?>` and the UTF-8 BOM if the file has one.
- The tool reproduces the file's existing shape, including the absence of a trailing newline after `</root>`.
  That is deliberate: matching Visual Studio's output keeps the diff to the added entries, even when `.editorconfig` sets `insert_final_newline`.
