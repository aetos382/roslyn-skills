# Rule Documentation and Configuration

## When to write documentation

Ask every time, and propose *yes*. The absence of a documentation directory is never a reason to skip:
the directory is created along with the page. Only an explicit "skip" from the user (or a request that
already says so) leaves the rule undocumented. The answer decides `helpLinkUri`: URL when a page is
created, omitted otherwise.

Include the target path in the proposal so the user can redirect it in one step, for example
"Create `Documentation/rules/CTS1001.md` and add it to the index?".

## Location and naming

| Item | Default | Config key |
|------|---------|------------|
| Directory | `docs/rules/` at the repository root | `docsDir` |
| Rule page | `<ID>.md`, e.g. `docs/rules/CTS1001.md` | — |
| Index | `README.md` inside the directory (GitHub renders it when the folder is opened) | `docsIndexFile` |

Always prefer what the repository already does. `FindConventions.cs` searches the whole tree and reports,
in decreasing order of certainty:

| Field | Meaning | What to do |
|-------|---------|------------|
| `docs.directory`, `docs.indexFile`, `docs.ruleDocs` | A directory already holds pages named after IDs (`CTS1001.md`, `CTS1001-disposable-field.md`) | Use it. Copy the existing naming scheme exactly, including any slug after the ID; ask when two schemes coexist. |
| `docs.mentionFiles` | Markdown elsewhere names existing IDs, e.g. one `Rules.md` listing every rule or a README table | The repository documents rules in that file. Propose adding a section or row there instead of starting a per-ID layout. |
| `docs.candidateDirectories` | Existing folders called `docs`, `doc`, `Documentation`, `wiki`, `rules`, `analyzers`, `diagnostics` (shallowest first) | No rule pages yet, but the repository has a documentation home. Put the new page under it, following its casing (`Documentation/rules/`, not `docs/rules/`). |
| `docs.suggestedDirectory` | The path derived from the above, or `docs/rules` when the repository has nothing | Use it as the proposed path in the question, and create the directory when the user agrees. |

Naming a new directory: keep the parent's existing casing and add a `rules` subdirectory
(`Documentation/rules`). When the candidate is already called `rules`, use it as is.

## Page content

Start from `examples/rule-doc-template.md` **only when the directory has no pages yet**. When pages exist,
open the newest one and mirror its headings, table layout, and code-block style instead of the template.

Fill every section; do not leave placeholders. The "Violates" and "Fixed" examples must be minimal
compilable snippets that would actually trigger and satisfy the rule. Mention the message arguments in
"Rule description" so readers understand what `{0}` refers to. In "When to suppress", show both the
`#pragma warning disable <ID>` form and the `.editorconfig` line.

## Index

The index lists every rule in ID order. Add the new row in sorted position, matching the existing column
set. When creating the index from scratch use `examples/rules-index-template.md`:

```markdown
| ID | Title | Category | Severity |
|----|-------|----------|----------|
| [CTS1001](CTS1001.md) | Disposable field should be disposed | Design | Warning |
```

Title in the index is the resx Title text.

## Suppressions

`SuppressionDescriptor` has no `helpLinkUri`, and the justification text is shown in the IDE, so a page per
suppression is not needed by default. Behaviour:

1. If the docs directory already contains suppression pages (files named `<PREFIX>S<NUMBER>.md`) or the
   index has a suppressions table, follow that: create the page and/or add the row in the same shape.
2. Otherwise create nothing for the suppression.

## Computing the URL

`helpLinkUri` must be the permanent URL of the page on the default branch. Use the script:

```bash
dotnet "${CLAUDE_PLUGIN_ROOT}/skills/add-diagnostic/scripts/DocUrl.cs" -- --doc docs/rules/CTS1001.md
```

Resolution order for the template: `--template` argument → `docUrlTemplate` in `.claude/roslyn-skills.md`
→ `https://github.com/{owner}/{repo}/blob/{branch}/{path}` when `origin` points at github.com. Owner and
repo come from the `origin` remote; branch from `origin/HEAD`, then `gh repo view`, then the current
branch. The script throws when the host is not GitHub and no template is configured; in that case ask the
user for the template (GitLab: `https://gitlab.com/{owner}/{repo}/-/blob/{branch}/{path}`, Azure DevOps and
others vary) and store it in the config file.

Verify the URL against the existing descriptors: if they use a different base (a docs site, `aka.ms`
links, a tag instead of a branch), the repository has its own convention; follow it and record it as
`docUrlTemplate`.

## Configuration file: `.claude/roslyn-skills.md`

A committed file with YAML front matter that pins conventions detection cannot infer or that the user
wants to override. Every key is optional. See `examples/roslyn-skills.md`.

| Key | Meaning |
|-----|---------|
| `diagnosticPrefix` | Three-letter prefix (`CTS`). |
| `idDigits` | Digits in the number (default 4). |
| `diagnosticIdsFile` / `suppressionIdsFile` / `categoriesFile` | Repository-relative paths. |
| `categories` | Map of category name → band digit. |
| `resxBaseName` | Base name of the resource group holding diagnostic strings (`Resources`). |
| `docsDir` / `docsIndexFile` | Documentation location. |
| `docUrlTemplate` | URL template with `{owner}`, `{repo}`, `{branch}`, `{path}`. |
| `idSharing` | `ProjectReference`, `CompileInclude`, or `SharedProject` (see `id-conventions.md`). Usually left out; detection reports it. |

The markdown body below the front matter holds free-form notes for the skill (for example the name of a
descriptor helper method). Read it and follow it.

Create the file only when a decision was made that detection could not reproduce next time (a new prefix,
a new band mapping, a non-GitHub URL template, an unusual docs layout). Tell the user the file was created
and that it should be committed. Detection alone is enough for repositories that already follow the
conventions.
