# Evals for the add-diagnostic skill

The tests under `tests/` prove the helper tool behaves and that the skill's documents point at files that exist.
Neither of them can tell you whether an agent holding SKILL.md actually edits a repository correctly, and that is what every change to the skill is really trying to move.
These evals close that gap: they build a throwaway analyzer repository, hand an agent a task in it, and then check what the agent left behind.

They are run by hand, not in CI.
An agent run is neither free nor deterministic, and a red build that might just be the model having an off day is a build people learn to ignore.
Run them when you change the skill, and read the failures rather than counting them.

## The loop

```bash
dotnet run evals/eval.cs -- list                     # the eval ids
dotnet run evals/eval.cs -- check                    # evals.json is well formed
dotnet run evals/eval.cs -- new-run mature-new-category
# hand the printed prompt.md to an agent, wait for it to finish
dotnet run evals/eval.cs -- grade <run directory>
dotnet run evals/eval.cs -- report                   # every graded run so far
```

`new-run` creates a directory under the machine's temp folder — outside this repository, so nothing it contains joins this build — and fills it with:

| Path | What it is |
|------|------------|
| `fixture/` | the generated repository, already a git repository with a remote so `doc-url` resolves |
| `prompt.md` | the task to hand an agent that has the skill |
| `prompt-baseline.md` | the same task with no skill, for the comparison run |
| `baseline/conventions.json` | what `find-conventions` saw before the agent touched anything |
| `baseline/build.log` | proof the fixture compiled clean to begin with |
| `run.json` | the eval id, the project to build, and the warnings the baseline build produced |
| `outputs/` | where the agent writes its report and the questions it answered for itself |
| `scratch/` | the scratch directory the prompt tells the agent to work from |

`new-run` fails outright if the fixture does not build.
A fixture that starts broken cannot say anything about the agent's edits, so it is better to hear about it before spending an agent run.

`grade` re-runs `find-conventions` over the fixture, evaluates the assertions in `evals.json`, prints them, and writes `grading.json`.
Its exit code is 0 only when every assertion passed.

### Running the agent

Both prompts are written for an agent with no human in front of it, which matters more here than it sounds.
The skill asks questions by design — Step 4 gathers everything undecided into one round — and a run that stops on a question measures nothing.
So the prompt tells the agent to write each question it would have asked to `outputs/questions.md`, take the option the skill recommends, and carry on.

That file is worth reading even when every assertion passes.
It is the only view of the question round the skill actually produced: how many questions survived the four-question cap, whether the options read clearly, and whether anything got asked mid-edit that Step 4 should have caught.

## The fixtures

Three repository shapes, generated from `fixture-*.cs`.
They exist because the workflow branches on the state of the repository rather than on the request, so a single fixture would leave most of the skill untested.

| Fixture | Prefix | What it makes the workflow do |
|---------|--------|-------------------------------|
| `mature` | `ACM` | The main path: existing bands, a categories class, resx in two cultures generated at build time, release tracking, rule pages with an index. Its code-fix project has no reference back to the analyzer, so `idSharing` is `none` and 6g has work to do. |
| `greenfield` | none | Nothing exists yet: no IDs file, no categories, no descriptor to follow, no resx, no release files, no documentation. Every "create it when missing" branch runs, and the strings question has to be asked because there is no neighbour to copy. |
| `literal` | `NWD` | Descriptors that pass literal strings, no resx anywhere, no documentation directory, no suppressions yet. The literal route is what the neighbours decide, so 6d has nothing to do. Its band mapping is deliberately not the conventional one — `Usage` is band 1 — so a workflow that assumes the table in `id-conventions.md` allocates the wrong number. |

All three start with no compiler or analyzer warnings at all — only the SDK's own `EnableGenerateDocumentationFile` notice, which is a property setting rather than anything about the code.
That is what makes the `build` assertion meaningful: a warning after the agent's edits is one the agent introduced.
Keeping it that way is a constraint on changes to the fixtures.

None of them registers `AnalyzerReleases.*.md` as an `AdditionalFiles` item, because none of them has to: `Microsoft.CodeAnalysis.Analyzers` ships a targets file that adds both, conditioned on the files existing in the project directory.
That is why 6e can create the pair and get RS2000 out of the very next build without touching the project file, and it is worth knowing before "the csproj was never updated" gets written down as a finding.

## Writing an eval

An eval in `evals.json` is a prompt against a fixture, plus the assertions its result has to satisfy.

```json
{
  "id": "literal-strings-and-docs",
  "fixture": "literal",
  "prompt": "…what the repository's owner would have typed…",
  "expected_output": "…what should happen, in prose, for the human reading the results…",
  "assertions": [ … ]
}
```

Assertions are graded mechanically, against the convention scan and the files themselves.
`text` is what the report shows, so write it as the thing being checked rather than as the mechanism.

| Kind | Fields | Passes when |
|------|--------|-------------|
| `jsonContains` / `jsonNotContains` | `path`, `value` | the scan's `path` does / does not yield that value |
| `jsonCount` | `path`, `count` | `path` yields exactly that many nodes |
| `jsonEquals` | `path`, `value` | `path` yields exactly one node, equal to `value` |
| `fileExists` / `fileMissing` | `glob` | something / nothing matches |
| `contains` | `glob`, `pattern` | at least one file matches the glob and **every** match contains the pattern |
| `anyContains` | `glob`, `pattern` | at least one matching file contains the pattern |
| `notContains` | `glob`, `pattern` | no matching file contains it (a glob matching nothing passes) |
| `resxEntryCount` | `glob`, `count` | every matching resx holds exactly that many `data` entries |
| `resxParity` | `glob` | every matching resx declares the same entry names |
| `noLeftovers` | — | the scan reports no `leftovers` |
| `build` | — | the project builds, and no warning code appears more often than it did in the baseline |

`path` is a dotted path into the scan, where a segment ending in `[]` expands the array it names: `diagnosticIds.ids[].value` is every ID value found.
A `glob` is resolved against the fixture, except when it starts with `@`, which resolves it against the run directory — `@outputs/report.md` is the agent's own report.

Patterns may contain `{name:ACM3001}`, which stands for whatever constant name the IDs file gave that value.
The descriptor field, the resource stem and the ID constant all share a name the agent chooses, so no assertion can spell it out; the ID value is the only part that is fixed in advance.

### What the assertions deliberately do not cover

Wording is left to human review.
Whether a title reads well, whether a message's placeholders are the right ones, whether the Japanese resx is a translation rather than a copy of the English — a regex that tried to decide those would fail on good output as readily as on bad.
Read `outputs/report.md` and the diff.

`greenfield-bootstrap` has no `build` assertion.
Creating a resx from nothing is the one place where the skill can legitimately end with a project that does not compile: on the `ResXFileCodeGenerator` route the resource class is Visual Studio's to generate, and the skill correctly skips the build and says so.
Which route the agent takes is a judgement call, so the build is a thing to look at rather than a thing to assert.

The `build` assertion compares how many times each warning code appears, not the warnings themselves.
A new warning that shares a code with one the agent fixed in the same run therefore cancels out.
Both are visible in `build.log`.

## Feeding the results to the skill-creator viewer

`grading.json` uses the `text` / `passed` / `evidence` field names the skill-creator's eval viewer expects, so a run directory can be dropped into its workspace layout when you want the side-by-side output review as well.
Nothing here depends on that; the viewer is for looking at prose, and these evals are for the parts that can be decided without an opinion.
