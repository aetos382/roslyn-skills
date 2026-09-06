---
name: add-skill
description: This skill should be used when the user asks to "add a skill", "create a new skill", "スキルを追加", "新しいスキルを作る", or names a capability this plugin should ship as a skill. Creates the skill's directory under plugin/skills/, its evals directory under evals/, and the row in Skills.cs that joins the two, then verifies the result.
argument-hint: <the skill's name, such as add-codefix>
allowed-tools: Read, Write, Edit, Glob, Grep, Bash, AskUserQuestion
---

# Add a Skill

Add one skill to this plugin, with everything that has to exist for it to ship and to be measured.
A skill that is only a SKILL.md is not finished: nothing grades it, and the harness cannot see it.

The deliverable is three things that name each other consistently:

| Artifact | Where | What it is |
|----------|-------|------------|
| The skill itself | `plugin/skills/<name>/` | SKILL.md, plus `references/` and `examples/` beside it |
| Its evals | `evals/<Directory>/` | `README.md`, `evals.json`, `Fixture*.cs` |
| The registry row | `evals/Skills.cs` | what joins the two, and names the scan command |

## 1. Settle the two names

A skill has two names, and they follow different rules.

- **The skill's name** is its directory under `plugin/skills/`, spelled the way a slash command is: lower-case, hyphenated (`add-diagnostic`).
  This is what the prompt points an agent at and where the pinned tool version is read from.
- **The evals directory** is a folder of C# inside a project, so it is spelled the way C# folders are (`AddDiagnostic`).

Confirm both with the user before creating anything, along with one sentence saying what the skill does and what it deliberately does not do.
That sentence becomes the last clause of the `description`, and it is what stops the skill from being invoked for work it does not cover.

## 2. Write the skill

Create `plugin/skills/<name>/SKILL.md` with frontmatter of the same shape as `plugin/skills/add-diagnostic/SKILL.md`:

```yaml
---
name: <name>
description: This skill should be used when the user asks to "…", "…", "…日本語の言い方…", or describes …. <What it does.> Not for <what it leaves alone>.
argument-hint: <what the user types after the command>
allowed-tools: <only the tools the skill actually uses>
---
```

The `description` is the only thing that decides whether the skill is reached, so it names the phrasings a user would actually type, in both English and Japanese, and ends by saying what the skill is not for.

Split anything that only matters in unusual cases into `references/`, and anything the agent should copy or read as a sample into `examples/`.
`tests/Aetos.RoslynSkills.Tools.Tests/SkillDocumentTests.cs` checks both directions: every path a document names has to exist, and every file under `references/` and `examples/` has to be named by some document.
A file nobody points at is dead weight, and a pointer to a file that is not there is a step the agent silently skips.

## 3. Pin the tool version

If the skill invokes the helper tool, it pins the exact version, the same one every other pin in `plugin/` names:

```bash
rg -n 'Aetos\.RoslynSkills\.Tools@' plugin
jq -r .version plugin/.claude-plugin/plugin.json
```

Use that version verbatim.
Do not bump it here — `/release` rewrites every pin at once, and a pin that disagrees with the others fails `draft-release.yml` before it packs anything.

New helper commands belong in the one package, as a command group named after the skill (`src/Aetos.RoslynSkills.Tools/<Group>/`), not as a second package.

## 4. Write the evals

Create `evals/<Directory>/` holding:

- `README.md` — what these evals cover, and what they deliberately leave to a human.
- `evals.json` — the prompts and their assertions. `evals/README.md` carries the schema and the assertion kinds.
- `Fixture*.cs` — the repositories they run against, each carrying its own `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props` and `global.json`, so MSBuild never walks up into this repository's.

Then add the row to `evals/Skills.cs`:

```csharp
["<name>"] = new(
    "<name>",
    "<Directory>",
    <Directory>Fixtures.All,
    ["<name>", "<scan subcommand>", "--path", "{repo}", "--summary"]),
```

The last field is the tool command that takes a structured reading of a repository, and it is what the `json*` and `noLeftovers` assertions read.
Pass `null` when the skill has no such command; its evals then have to work from files alone.

## 5. Verify

```bash
claude plugin validate ./plugin --strict
dotnet run --project evals -- check
dotnet run --project evals -- list --skill <name>
dotnet test
```

`check` catches an `evals.json` the harness cannot run, including a `json*` assertion on a skill that declares no scan command.
`list` printing the new evals is the proof the registry row and the directory found each other.

## 6. Report

Show `git diff --stat` and say what is left for the user:

- Running the evals at least once, since nothing has yet shown that an agent holding this skill actually does the right thing.
- Committing, which this skill does not do.
