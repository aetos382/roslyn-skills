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
2. Add the **same English text to every culture file of the group** (the base file and each
   `Resources.<culture>.resx`). Translation is a separate task; a missing entry in a satellite file falls
   back to the neutral resource, but keeping the files structurally identical avoids surprises with
   tooling that diffs them.
3. Edit only the `.resx` files. Never edit `*.Designer.cs`.

## Ordering inside the file

Entries are kept in **ID value order**, and within one diagnostic in the order
Title → Message → Description (Justification alone for suppressions). Existing entries are left where
they are; new entries go after the last entry whose ID sorts before them. `AddResxEntries.cs` resolves
resource names to ID values through the IDs file and computes the insertion point; pass `--ids-file` so it
can do that (pass the suppression IDs file when adding a Justification).

## Adding entries

Write the entries to a JSON file (or pass the JSON inline) and run the script once per resource group,
listing all culture files:

```bash
dotnet "${CLAUDE_PLUGIN_ROOT}/skills/add-diagnostic/scripts/AddResxEntries.cs" -- \
  --resx src/Contoso.Analyzers/Resources.resx --resx src/Contoso.Analyzers/Resources.ja.resx \
  --ids-file src/Contoso.Analyzers/DiagnosticIds.cs \
  --entries entries.json
```

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

Read the JSON report. If `valid` is false, restore the file from git (`git checkout -- <file>`) and fix the
cause before retrying; never leave a broken resx behind.

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
