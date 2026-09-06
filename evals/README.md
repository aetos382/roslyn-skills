# Evals

The tests under `tests/` prove the helper tool behaves and that a skill's documents point at files that exist.
Neither of them can tell you whether an agent holding a SKILL.md actually edits a repository correctly, and that is what every change to a skill is really trying to move.
These evals close that gap: they build a throwaway repository, hand an agent a task in it, and then check what the agent left behind.

They are run by hand, not in CI.
An agent run is neither free nor deterministic, and a red build that might just be the model having an off day is a build people learn to ignore.
Run them when you change a skill, and read the failures rather than counting them.

## Layout

```
evals/                       # Aetos.RoslynSkills.Evals, the harness, which knows nothing about any one skill
├── Program.cs, Commands/     # the CLI
├── Harness.cs, Assertions.cs # building a fixture, running the scan and the compiler, grading the result
├── Skills.cs                 # the registry every skill adds a row to
└── AddDiagnostic/            # one directory per skill
    ├── README.md             # what these evals cover, and what they leave to a human
    ├── evals.json            # the prompts and their assertions
    └── Fixture*.cs           # the repositories they run against
```

Only `evals.json` and the fixtures are a skill's own; everything else is shared.
The harness is a normal project in the solution, so a break in it fails the build like anything else, and `tests/Aetos.RoslynSkills.Evals.Tests` covers the parts that decide whether an assertion passed — a bug there would not announce itself, since the run would simply report PASS.
Adding a skill is a directory of the same shape plus one row in `Skills.cs`, naming the skill, the directory holding its evals, its fixtures and, optionally, the tool command that takes a structured reading of a repository for the `json*` assertions to work from.
The skill's name is its directory under `plugin/skills/`, which is where the prompt points the agent and where the pinned tool version is read from; the directory here is named the way the rest of the C# is, so the two are separate fields rather than one.

## The loop

```bash
dotnet run --project evals -- list                     # every skill's evals; --skill narrows it to one
dotnet run --project evals -- new-run --skill add-diagnostic --id mature-new-category
# hand the printed prompt.md to an agent, wait for it to finish
dotnet run --project evals -- grade <run directory>
dotnet run --project evals -- report                   # every graded run so far
dotnet run --project evals -- check                    # the evals.json files are well formed
```

`list` prints, per eval, the two fields that name it and the one sentence saying what it guarantees.
Which fixture it uses and what will be said to the agent are not there: choosing an eval is a question of what it establishes, and the run itself carries the rest.
`new-run` names the skill's other ids when given one it does not recognize.

`new-run` creates a directory under `Temp/evals/<skill>/` in this workspace and fills it with:

| Path | What it is |
|------|------------|
| `fixture/` | the generated repository, committed in its starting state, so `git diff` in it is exactly what the agent did. Set up as a clone — a remote, a remote-tracking branch and an `origin/HEAD` — so URL resolution has what it reads |
| `prompt.md` | the task to hand an agent that has the skill |
| `prompt-baseline.md` | the same task with no skill, for the comparison run |
| `baseline/scan.json` | what the skill's scan command saw before the agent touched anything |
| `baseline/build.log` | proof the fixture compiled clean to begin with |
| `run.json` | the skill, the eval id, the project to build, and the warnings the baseline build produced |
| `outputs/` | where the agent writes its report, the questions it answered for itself, and anything it hit while following the skill |
| `scratch/` | the scratch directory the prompt tells the agent to work from |

`Temp/` is already ignored by git, and the solution names its projects one by one rather than by glob, so a generated repository never joins this build.
A fixture is also expected to carry its own `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props` and `global.json`, because MSBuild walks up from a project until it finds each of those and would otherwise reach this repository's — central package management alone would turn every `Version` attribute below into an NU1008 error, and an unpinned SDK would let the toolchain move under the baseline.
They are generated with everything else and are not files to maintain.

`new-run` fails outright if the fixture does not build.
A fixture that starts broken cannot say anything about the agent's edits, so it is better to hear about it before spending an agent run.

`grade` re-runs the scan over the fixture, evaluates the assertions in that skill's `evals.json`, prints them, and writes `grading.json`.
Its exit code is 0 only when every assertion passed.

### Running the agent

Nothing here launches the agent: `new-run` writes `prompt.md` and stops, and you hand that to a session of your own.
So a run inherits whatever that session is configured to do, and the harness cannot see it.
One configuration matters enough to name: an instruction to prefer Bash — `cat`, `sed` — over `Read`, `Write` and `Edit` bypasses what a skill declares in `allowed-tools`, and with it whatever that skill's steps were protecting by naming a tool.
A clean run under that instruction does not say the declared path works; the prompt therefore tells the agent the skill wins and asks for the clash in `feedback.md`, which is the only place it can surface.

Both prompts are written for an agent with no human in front of it, which matters more here than it sounds.
A skill that asks questions by design — as add-diagnostic does, gathering everything undecided into one round — turns a run that stops on a question into a run that measures nothing.
So the prompt tells the agent to write each question it would have asked to `outputs/questions.md`, take the option the skill recommends, and carry on.

That file is worth reading even when every assertion passes.
It is the only view of the question round the skill actually produced: how many questions survived whatever cap the skill sets, whether the options read clearly, and whether anything got asked mid-edit that the design step should have caught.

The prompt also asks for `outputs/feedback.md`: anything the agent ran into while following the skill — an instruction it could not follow, two documents that disagreed, a step it had to read twice, an error it worked around — named against the document and step it belongs to.
The agent that just followed the skill is the only witness to any of that, and no assertion can see it.
It is written either way, saying so in as many words when nothing came up, because a missing file cannot tell "the skill gave me no trouble" apart from "the run never got this far".
That leaves the other risk, which is an agent producing feedback because it was asked rather than because there was any, so the prompt says plainly that inventing something is what would make the file worthless.
Read what turns up as a lead rather than as a finding — it is one run's impression, and the fix belongs in the skill only once you can see the same thing in the documents yourself.

### Reading a finished run

Start with the diff, not with `outputs/`:

```bash
git -C <run>/fixture diff
```

The fixture is committed in its starting state, so that diff is the agent's work and nothing else — and it is the only place the things no assertion decides are visible: whether a title reads as a title, whether the message arguments are the right ones, whether a Japanese resx entry is a translation or the English pasted twice.
What the agent says it did is in `outputs/report.md`, and the two are worth reading in that order, since a report is a claim about the diff.

The rest is for when something is off rather than for every run.
`build.log` says which warning the `build` assertion counted; `scan.json` against `baseline/scan.json` says what the skill's own scan made of the result, which is what a puzzling `json*` failure usually turns on; `grading.json` is the console output kept, for comparing one run against another later.

`scratch/` is worth a look now and then, because no assertion goes near it.
It holds what the agent worked with: the scan output it fetched, the entries JSON it passed to the tool, the copies 6d is told to take before touching a resx.
Those copies being there is the only evidence that step ran at all, and a scratch directory that is empty at the end of a run means the work happened somewhere the skill said it should not.

## Writing an eval

An eval in that skill's `evals.json` is a prompt against a fixture, plus the assertions its result has to satisfy.

```json
{
  "id": "literal-strings-and-docs",
  "summary": "…what this eval guarantees, in one sentence…",
  "fixture": "literal",
  "prompt": "…what the repository's owner would have typed…",
  "expected_output": "…what should happen, in prose, for the human reading the results…",
  "assertions": [ … ]
}
```

`summary` is what `list` shows, so it is one sentence and `check` keeps it that way; `expected_output` is the long form, for reading a finished run rather than for picking one.

Assertions are graded mechanically, against the scan and the files themselves.
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
The `json*` kinds and `noLeftovers` only work for a skill whose row in `Skills.cs` declares a scan command; `check` says so rather than letting the run find out.

A `glob` is resolved against the fixture, except when it starts with `@`, which resolves it against the run directory — `@outputs/report.md` is the agent's own report.

Patterns may contain `{name:ACM3001}`, which stands for whatever constant name the scan reported for that ID value.
A skill's artifacts often share a name the agent chooses, so no assertion can spell it out; the ID value is the only part fixed in advance.

## Feeding the results to the skill-creator viewer

`grading.json` uses the `text` / `passed` / `evidence` field names the skill-creator's eval viewer expects, so a run directory can be dropped into its workspace layout when you want the side-by-side output review as well.
Nothing here depends on that; the viewer is for looking at prose, and these evals are for the parts that can be decided without an opinion.
