# add-diagnostic evals

Five prompts against three generated repositories.
See `../README.md` for how a run is created, handed to an agent, and graded; this file is only about what these particular evals are trying to find out.

## The fixtures

The workflow branches on the state of the repository rather than on the request, so a single fixture would leave most of the skill untested.

| Fixture | Prefix | What it makes the workflow do |
|---------|--------|-------------------------------|
| `mature` | `ACM` | The main path: existing bands, a categories class, resx in two cultures generated at build time, release tracking, rule pages with an index. Its code-fix project has no reference back to the analyzer, so `idSharing` is `none` and 6g has work to do. |
| `greenfield` | none | Nothing exists yet: no IDs file, no categories, no descriptor to follow, no resx, no release files, no documentation. Every "create it when missing" branch runs, and the strings question has to be asked because there is no neighbour to copy. |
| `literal` | `NWD` | Descriptors that pass literal strings, no resx anywhere, no documentation directory, no suppressions yet. The literal route is what the neighbours decide, so 6d has nothing to do. Its band mapping is deliberately not the conventional one — `Usage` is band 1 — so a workflow that assumes the table in `id-conventions.md` allocates the wrong number. |

All three start with **zero** build warnings.
That is what makes the `build` assertion meaningful: a warning after the agent's edits is one the agent introduced.
Keeping it that way is a constraint on changes to the fixtures.

None of them registers `AnalyzerReleases.*.md` as an `AdditionalFiles` item, because none of them has to: `Microsoft.CodeAnalysis.Analyzers` ships a targets file that adds both, conditioned on the files existing in the project directory.
That is why 6e can create the pair and get RS2000 out of the very next build without touching the project file, and it is worth knowing before "the csproj was never updated" gets written down as a finding.

## What the assertions deliberately do not cover

Wording is left to human review.
Whether a title reads well, whether a message's placeholders are the right ones, whether the Japanese resx is a translation rather than a copy of the English — a regex that tried to decide those would fail on good output as readily as on bad.
Read `outputs/report.md` and the diff.

`greenfield-bootstrap` has no `build` assertion.
Creating a resx from nothing is the one place where the skill can legitimately end with a project that does not compile: on the `ResXFileCodeGenerator` route the resource class is Visual Studio's to generate, and the skill correctly skips the build and says so.
Which route the agent takes is a judgement call, so the build is a thing to look at rather than a thing to assert.

The `build` assertion compares how many times each warning code appears, not the warnings themselves.
A new warning that shares a code with one the agent fixed in the same run therefore cancels out.
Both are visible in `build.log`.
