# roslyn-skills

Claude Code plugin with skills for developing Roslyn analyzers, code fixes, source generators, and
diagnostic suppressors.

## Skills

| Skill | Invocation | Purpose |
|-------|------------|---------|
| `add-diagnostic` | `/roslyn-skills:add-diagnostic <what the diagnostic should report>` or a natural-language request such as "add a diagnostic that warns when a Task is not awaited" | Adds the ID constant, DiagnosticDescriptor (or SuppressionDescriptor), resx strings, `AnalyzerReleases.Unshipped.md` row, and optional rule documentation, following the repository's existing conventions. |

The skill does not implement analysis logic, code fixes, or tests; it prepares everything around them.

## Prerequisites

- .NET SDK 10.0.300 or later. The skill's helper tools are file-based C# apps (`dotnet Script.cs`), so
  nothing else needs to be installed on Windows, macOS, or Linux. They run from a temporary directory
  and take repository paths as arguments, so a `global.json` at your repository root, which `dotnet`
  applies to every folder beneath it, cannot pin them to an SDK that is older or not installed.
- `git` on `PATH` (used to derive the documentation URL from the `origin` remote). `gh` is used when
  available to look up the default branch.

## Installation

This repository is its own plugin marketplace. In Claude Code:

```
/plugin marketplace add aetos382/roslyn-skills
/plugin install roslyn-skills@roslyn-skills
```

Pick up later changes with:

```
/plugin marketplace update roslyn-skills
```

To work on the plugin itself, load a clone directly instead:

```bash
claude --plugin-dir /path/to/roslyn-skills
```

## Conventions the skill enforces

- **ID**: PascalCase normative name (`TaskShouldBeAwaited`) with a value of a three-letter prefix plus a
  zero-padded number (`CTS2001`). The leading digit is a category band, so related rules stay adjacent.
  IDs live in a dedicated `DiagnosticIds.cs`; suppressions in `SuppressionIds.cs` with values of the form
  `CTSS0001`.
- **Descriptor**: defined in the analyzer / generator / suppressor class, same name as the ID, with
  `helpLinkUri` pointing at the rule's documentation page when one exists.
- **Resources**: `{Name}Title`, `{Name}Message`, `{Name}Description` (or `{Name}Justification`), added to
  every culture file in ID order. Only `.resx` files are edited; when the project uses Visual Studio's
  `ResXFileCodeGenerator`, the skill reminds you to regenerate `Resources.Designer.cs`.
- **Release tracking**: a row in `AnalyzerReleases.Unshipped.md` with a short description in the Notes
  column (suppressions are not tracked).
- **Documentation**: `docs/rules/<ID>.md` plus an index `README.md`, with the GitHub blob URL as the help
  link.

## Configuration

Detection from the repository is usually enough. To pin conventions, commit
`.claude/roslyn-skills/add-diagnostic.md` (the directory is the plugin name, the file the skill name):

````markdown
Free-form notes for the skill go here.

```json
{
  "diagnosticPrefix": "CTS",
  "idDigits": 4,
  "diagnosticIdsFile": "src/Contoso.Analyzers/DiagnosticIds.cs",
  "suppressionIdsFile": "src/Contoso.Analyzers/SuppressionIds.cs",
  // Category name -> band (the leading digit of the number).
  "categories": { "Design": 1, "Usage": 2, "Performance": 3 },
  "docsDir": "docs/rules",
  "docsIndexFile": "README.md",
  "docUrlTemplate": "https://github.com/{owner}/{repo}/blob/{branch}/{path}",
  "idSharing": "AnalyzerProject",
}
```
````

The settings live in the first fenced `json` block, so the file stays an ordinary Markdown document that
renders and highlights anywhere. `//` comments and trailing commas are allowed. A malformed block is
reported with a line number rather than silently ignored, so a mistyped key cannot pass for a missing one.
Everything outside the block is free-form notes the skill reads and follows.

Every key is optional. See `skills/add-diagnostic/examples/add-diagnostic.md` for the full list.

## Layout

```
roslyn-skills/
├── .claude-plugin/
│   ├── plugin.json        # plugin manifest
│   └── marketplace.json   # makes this repository installable as a marketplace
├── README.md
└── skills/
    └── add-diagnostic/
        ├── SKILL.md
        ├── references/   # conventions, descriptor patterns, resx, release tracking, docs
        ├── examples/     # canonical files and templates
        └── scripts/      # FindConventions.cs, NextId.cs, AddResxEntries.cs, DocUrl.cs, Common.cs
```

## License

MIT
