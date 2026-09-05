# roslyn-skills

Claude Code plugin with skills for developing Roslyn analyzers, code fixes, source generators, and diagnostic suppressors.

## Skills

| Skill | Invocation | Purpose |
|-------|------------|---------|
| `add-diagnostic` | `/roslyn-skills:add-diagnostic <what the diagnostic should report>` or a natural-language request such as "add a diagnostic that warns when a Task is not awaited" | Adds the ID constant, DiagnosticDescriptor (or SuppressionDescriptor), resx strings, `AnalyzerReleases.Unshipped.md` row, and optional rule documentation, following the repository's existing conventions. |

The skill does not implement analysis logic, code fixes, or tests; it prepares everything around them.

## Prerequisites

- .NET SDK 10.0 or later.
  The skill's helpers live under the `add-diagnostic` command group of the [`Aetos.RoslynSkills.Tools`](https://www.nuget.org/packages/Aetos.RoslynSkills.Tools) .NET tool, which the skill runs from NuGet.org with `dotnet tool exec` at a pinned version, so nothing has to be installed on Windows, macOS, or Linux.
  Not with `dnx`: that shorthand is a script, which Bash on Windows resolves only as `dnx.cmd`.
  One package covers every skill in the plugin: a new skill adds a command group rather than another package.
  It runs from a temporary directory and takes repository paths as arguments, so a `global.json` at your repository root, which `dotnet` applies to every folder beneath it, cannot pin it to an SDK that is not installed.
- `git` on `PATH` (used to derive the documentation URL from the `origin` remote).
  `gh` is used when available to look up the default branch.

## Installation

This repository is its own plugin marketplace.
In Claude Code:

```
/plugin marketplace add aetos382/roslyn-skills
/plugin install roslyn-skills@roslyn-skills
```

Pick up later changes with:

```
/plugin marketplace update roslyn-skills
```

To work on the plugin itself, load a clone directly instead.
The plugin lives in the `plugin/` subdirectory, so that is the path to pass:

```bash
claude --plugin-dir /path/to/roslyn-skills/plugin
```

## Conventions the skill enforces

- **ID**: PascalCase normative name (`TaskShouldBeAwaited`) with a value of a three-letter prefix plus a zero-padded number (`CTS2001`).
  The leading digit is a category band, so related rules stay adjacent.
  IDs live in a dedicated `DiagnosticIds.cs`; suppressions in `SuppressionIds.cs` with values of the form `CTSS0001`.
- **Descriptor**: defined in the analyzer / generator / suppressor class, same name as the ID, with `helpLinkUri` pointing at the rule's documentation page when one exists.
- **Resources**: `{Name}Title`, `{Name}Message`, `{Name}Description` (or `{Name}Justification`), added to every culture file in ID order.
  Only `.resx` files are edited; when the project uses Visual Studio's `ResXFileCodeGenerator`, the skill reminds you to regenerate `Resources.Designer.cs`.
- **Release tracking**: a row in `AnalyzerReleases.Unshipped.md` with a short description in the Notes column (suppressions are not tracked).
- **Documentation**: `docs/rules/<ID>.md` plus an index `README.md`, with the GitHub blob URL as the help link.

## Configuration

Detection from the repository is usually enough.
To pin conventions, commit `.claude/roslyn-skills/add-diagnostic.md` (the directory is the plugin name, the file the skill name):

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

The settings live in the first fenced `json` block, so the file stays an ordinary Markdown document that renders and highlights anywhere.
`//` comments and trailing commas are allowed.
A malformed block is reported with a line number rather than silently ignored, so a mistyped key cannot pass for a missing one.
Everything outside the block is free-form notes the skill reads and follows.

Every key is optional.
See `plugin/skills/add-diagnostic/examples/add-diagnostic.md` for the full list.

## Layout

```
roslyn-skills/
├── .claude-plugin/   # the marketplace manifest that makes this repository installable
├── .github/          # Dependabot, and the CI, draft-release and publish workflows
├── plugin/           # what an install delivers, and nothing else
│   └── skills/       # one directory per skill, with its references and examples beside it
├── src/              # the Aetos.RoslynSkills.Tools tool the skills invoke
├── tests/            # unit tests for the tool
└── evals/            # end-to-end evals: an agent holding the skill, against generated repositories
```

`marketplace.json` names `plugin/` as the plugin's source, so an install copies that directory alone into the plugin cache; the tool and its tests never reach it.
The license is duplicated into `plugin/` because that copy is the only one an install carries.

## Releasing

The skill pins the tool's exact version, so an install carries one number: the pins in `SKILL.md` and `references/*.md` and the `version` in `plugin/.claude-plugin/plugin.json` all name the version being released.
The package version is not in the project file at all; `draft-release.yml` passes it to `dotnet pack`.

`/release <version>` (the repository-local skill in `.claude/skills/release/`) rewrites those pins, commits them, and starts `draft-release.yml` at the same number.
That workflow re-checks the pins and the manifest against its input, so a release whose skills would invoke a package it never built fails before it packs anything.
It then tests, packs, runs the packed tool, pushes to GitHub Packages, and leaves a **draft** GitHub release.

Publishing that draft is the last step, and a manual one: it triggers `publish.yml`, which pushes the release's assets to NuGet.org, where a version can never be replaced.

## Tests

```bash
dotnet test tests/Aetos.RoslynSkills.Tools.Tests
```

The test project references the tool project and sees its internals through `InternalsVisibleTo`.
What is covered is the parsing the tool does on input it does not control: the settings file, the command line, the ID constants and band headers, and the file-shape detection that decides which directories are skipped.

## Evals

The tests prove the tool behaves; they cannot tell whether an agent holding the skill edits a repository correctly, which is what a change to the skill is actually trying to move.

```bash
dotnet run evals/eval.cs -- list
```

`evals/` generates throwaway analyzer repositories in three different states, hands an agent a task in one of them, and grades what it left behind against the convention scan and the compiler.
They are run by hand rather than in CI, because an agent run is neither free nor deterministic.
See `evals/README.md` for the loop and for what the assertions deliberately leave to human review.

## License

MIT
