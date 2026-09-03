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

1. Find the resource group of the target project: `FindConventions.cs` lists each group with its
   `baseName`, all culture files, the detected generator, and the resource class name. A project normally
   has one group (`Resources.resx` plus optional `Resources.ja.resx`, `Resources.de.resx`, ...). When a
   project has several groups, ask which one holds diagnostic strings, or pick the one that already
   contains `*Title` / `*Message` entries.
2. Add an entry to **every culture file of the group**, translated per file. The neutral file
   (`Resources.resx`) holds the source language, normally English. Each `Resources.<culture>.resx` holds
   the same message in that culture's language, which the file name names: `.ja` Japanese, `.de` German,
   `.fr` French, `.zh-Hans` Simplified Chinese. Keep the placeholders (`{0}`, `{1}`) and the quoting
   style identical across languages; translate the prose around them.

   Never write the source text into a satellite file. A missing entry falls back to the neutral resource
   and is visibly untranslated, but source text sitting in `Resources.ja.resx` looks finished and stays
   wrong. When a translation cannot be produced with confidence, leave that file out and say so in the
   report instead.
3. Edit only the `.resx` files. Never edit `*.Designer.cs`.

## Ordering inside the file

Entries are kept in **ID value order**, and within one diagnostic in the order
Title → Message → Description (Justification alone for suppressions). Existing entries are left where
they are; new entries go after the last entry whose ID sorts before them. `AddResxEntries.cs` resolves
resource names to ID values through the IDs file and computes the insertion point; pass `--ids-file` so it
can do that (pass the suppression IDs file when adding a Justification).

## Adding entries

Because each culture file gets different text, run the script once per file with that file's own entries
JSON:

```bash
S="${CLAUDE_PLUGIN_ROOT}/skills/add-diagnostic/scripts"
dotnet "$S/AddResxEntries.cs" -- --resx /repo/src/A/Resources.resx \
  --ids-file /repo/src/A/DiagnosticIds.cs --entries /scratch/entries.en.json
dotnet "$S/AddResxEntries.cs" -- --resx /repo/src/A/Resources.ja.resx \
  --ids-file /repo/src/A/DiagnosticIds.cs --entries /scratch/entries.ja.json
```

Passing several `--resx` files in one call is still supported, and correct only when the text is
identical in all of them, which for diagnostic strings it never is.

Options: `--resx` (repeatable, or comma-separated), `--entries` (JSON array or path to a JSON file),
`--ids-file`, `--force`, `--validate-only`.

The script:

- preserves the file's BOM, line endings, indentation, and existing element order;
- inserts `<data name="..." xml:space="preserve"><value>...</value></data>` at the computed position;
- skips names that already exist (use `--force` to overwrite their values);
- reloads each file and verifies that it is well-formed XML with root `<root>`, that no `data` name is
  duplicated, and that every new entry is present with a `<value>`;
- reports whether a sibling `*.Designer.cs` exists and lacks the new names (`designerStale`);
- exits with code 1 when any file fails validation.

Copy every culture file to the scratchpad before running the script. Read the JSON report; if `valid` is
false, restore from those copies and fix the cause before retrying. Never leave a broken resx behind, and
never recover with `git checkout --`: a resx routinely holds uncommitted work that git would discard
along with the failed insertion.

For a one-off manual edit, insert the same XML by hand and run the script with `--validate-only` afterwards.

## Designer.cs and build errors

The descriptor code uses `nameof(Resources.XxxTitle)`, so the strongly typed resource class must expose
the new property, otherwise the project fails to compile. Who generates that class decides what happens
next:

| Generator (as detected) | Effect of adding a resx entry | Action |
|-------------------------|-------------------------------|--------|
| `ResXFileCodeGenerator` / `PublicResXFileCodeGenerator` (`<Generator>` on the `EmbeddedResource`, `Resources.Designer.cs` checked in) | `Designer.cs` is regenerated only by Visual Studio when the resx is saved in its editor. Until then the build fails with CS0117. | Tell the user: open the resx in Visual Studio and save it (or run the custom tool) to regenerate `Resources.Designer.cs`, then commit both files. Do not hand-edit the Designer file. |
| `Microsoft.CodeAnalysis.ResxSourceGenerator` package reference (a `PackageReference`, or a `GlobalPackageReference` in `Directory.Packages.props`; an empty `<Generator></Generator>` on the `EmbeddedResource` is typical alongside it) | The class is generated at build time from the resx. | Nothing; the next build picks up the entry. A hand-written partial (`resx[].localizableStringHelper`, `resx[].localizableStringProperties`) decides how descriptors obtain the strings; see `descriptors.md`, "Localizable strings". |
| MSBuild `GenerateResource` with `StronglyTypedFileName` / `StronglyTypedClassName` metadata, or `Generator="MSBuild:Compile"` | Generated at build time. | Nothing. |
| Unknown (no Designer file, no generator metadata) | Possibly the project reads resources through a custom `ResourceManager` wrapper. | Look at how existing descriptors reference resources and mirror it; if `nameof(Resources.X)` is used and no generator is found, ask the user how the class is produced. |

`FindConventions.cs` sets `requiresVisualStudioRegeneration` to `true` for the first row. Always
mention this in the final summary when it applies; it is the one step the skill cannot do itself.

## Editing rules for the XML

- Keep `xml:space="preserve"` on every `data` element; Visual Studio adds it and the ResX reader relies on
  it for leading/trailing whitespace.
- Escape `<`, `>`, `&` in values (the script does this through the XML DOM).
- Do not add `<comment>` elements unless the file already uses them for diagnostic strings.
- Do not touch the `resheader` elements or any `metadata`/`assembly` nodes.
- Keep the declaration `<?xml version="1.0" encoding="utf-8"?>` and the UTF-8 BOM if the file has one.
- The script reproduces the file's existing shape, including the absence of a trailing newline after
  `</root>`. That is deliberate: matching Visual Studio's output keeps the diff to the added entries,
  even when `.editorconfig` sets `insert_final_newline`.
