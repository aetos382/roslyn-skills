---
name: eval
description: This skill should be used when the user asks to "run an eval", "run the evals", "grade a run", "eval を回す", "評価を実行", or names an eval id to try. Drives the loop in evals/README.md — picking an eval, creating its run directory, grading a finished run, and reading the result — so the command line does not have to be remembered.
argument-hint: <eval id, or a run directory to grade; omit to list what is available>
allowed-tools: Read, Glob, Grep, Bash
disable-model-invocation: true
---

# Eval

Drive one pass of the eval loop.
`evals/README.md` is the reference for what these evals are and how an eval is written; this skill is the loop itself, in the order it is actually run.

This skill does not run the agent under test.
Creating the run costs nothing, and the agent run is the part that costs money and is not deterministic, so it stays a deliberate act by the user.

## 1. Pick an eval

```bash
dotnet run --project evals -- list
```

`list` prints, per eval, the skill, the id, and the one sentence saying what that eval guarantees.
Narrow it with `--skill <name>` when the user named a skill.

If the user named an id, go straight to step 2 — `new-run` names the skill's other ids when given one it does not recognize, so a typo reports itself.

## 2. Create the run

```bash
dotnet run --project evals -- new-run --skill <skill> --id <id>
```

This creates a directory under `Temp/evals/<skill>/`, generates the fixture, commits it in its starting state, and builds it.
`new-run` fails outright if the fixture does not build, which is the point: a fixture that starts broken cannot say anything about the agent's edits.

Report the run directory and the path to `prompt.md`, and stop there.
Tell the user that handing `prompt.md` to an agent that has the skill is the next step, and that `prompt-baseline.md` beside it is the same task with no skill, for the comparison run.

## 3. Grade a finished run

```bash
dotnet run --project evals -- grade <run directory>
```

`grade` re-runs the scan over the fixture, evaluates that skill's assertions, prints them, and writes `grading.json`.
Its exit code is 0 only when every assertion passed.

## 4. Read the result

Read it in this order, and say so in the report rather than only quoting the pass count.

```bash
git -C <run>/fixture diff        # the agent's work, and nothing else
```

The diff comes first because it is the only place the things no assertion decides are visible: whether a title reads as a title, whether the message arguments are the right ones, whether a translated string is a translation or the English pasted twice.

Then `outputs/report.md`, which is the agent's claim about that diff, and in that order — a report is worth reading against the diff, not instead of it.

Then `outputs/questions.md` and `outputs/feedback.md`, which are worth reading even when every assertion passed.
`questions.md` is the only view of the question round the skill produced.
`feedback.md` is what the agent hit while following the skill, and it is written either way, so a file saying nothing came up is a result and a missing file means the run never got that far.
Treat both as leads from one run, not as findings: a fix belongs in the skill only once the same thing is visible in the documents themselves.

The rest is for when something is off: `build.log` says which warning the `build` assertion counted, `scan.json` against `baseline/scan.json` is what a puzzling `json*` failure usually turns on, and `scratch/` being empty at the end means the agent worked somewhere the skill said it should not.

## 5. Compare runs

```bash
dotnet run --project evals -- report
```

Summarizes every graded run under `Temp/evals/`.
Read failures rather than counting them: these are run by hand precisely because a red result might just be the model having an off day.

## Checking the evals themselves

```bash
dotnet run --project evals -- check
```

Verifies every `evals.json` is well formed and that the harness can run what it asks for.
Run this after editing an `evals.json`, not as part of a normal loop.
