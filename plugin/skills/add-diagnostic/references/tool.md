# The helper tool

Everything about running `Aetos.RoslynSkills.Tools` that only matters when something is unusual: how the command line has to be spelled, where it must be run from, what its output promises, and what to do when the package itself will not resolve.
SKILL.md carries the invocation; this file carries the rest.

## Why a pinned tool and not a script

The helpers are a versioned .NET tool rather than scripts shipped beside this skill, because they are what the workflow's correctness rests on: convention detection, ID allocation and resx insertion are all "get this exactly right or corrupt the repository" work.
A tool has tests, a release, and a version number this skill pins, so the instructions written here and the code that carries them out ship as one thing.
Do not reimplement a subcommand inline when it fails; that is the one thing the pin exists to prevent.

## Spelling the command line

**Do not use `dnx`**, the documented shorthand for the same command.
It is a script, not an executable, so Bash on Windows — which is where this skill runs its commands — resolves it only as `dnx.cmd` and reports `dnx: command not found` otherwise.
`dotnet` is an executable and needs no such workaround.

Keep the `--`: `dotnet tool exec` forwards unknown arguments to the tool, but it claims `--help`, `--version`, `-v`, `--source`, `--add-source`, `--configfile`, `--prerelease` and `--interactive` for itself, so `... doc-url --help` prints its own help instead.
Everything after `--` is passed through untouched.
The tool carries one command group per skill, so `add-diagnostic` always comes before the subcommand.

Only `--doc` is repository-relative, because it becomes part of a URL; everything else is an absolute path.

## Where to run it

**Run it from the scratchpad**, not from anywhere inside the target repository.
Create it once and keep its absolute path in the run's notes, because the variable holding it does not survive to the next call either, and an empty one turns `cd` into a no-op that leaves the tool running wherever the previous call stopped.
`dotnet` resolves its SDK from the first `global.json` found in the working directory **or any ancestor of it**, so a pin at the repository root applies just as much when the working directory is several folders below it, and a pinned version that is not installed fails outright with "A compatible .NET SDK was not found".
Every subcommand takes the repository path as an argument for this reason.
The constraint applies only to the tool; build the analyzer project itself from the repository, where its pinned SDK is the correct one.

## Two switches every dotnet command needs

Pass `-nodeReuse:false` to every `dotnet build` or `dotnet msbuild` the workflow runs, so no MSBuild worker process outlives it holding file locks.
`find-conventions` already does this internally.
The switch is spelled `-nodeReuse:false` or `-nr:false`; `/node-reuse:false` is rejected with MSB1001.

Export `DOTNET_CLI_UI_LANGUAGE=en` and `VSLANG=1033` at the top of **every** call that runs `dotnet`, not once at the start: each Bash call is a fresh shell that keeps the working directory and nothing else, so an export made in an earlier call is gone by the next one.
The SDK and MSBuild otherwise speak the machine's language, and these documents name failures — MSB1001, CS0117, IDE0090, the NuGet codes below — in English only, so a localized build log is one the workflow cannot match against anything written here.
The first covers the CLI, the second the MSBuild engine and the compilers it starts; the tool sets both for the processes it starts itself, which does not reach the commands the workflow runs.

## What the output promises

Every subcommand prints JSON on stdout, including for expected failures (`{"error": ..., "hint": ...}` with exit code 1) and for a mistyped command line, so read the output rather than guessing what it would return.
Paths in that JSON are repository-relative, so prefix the repository root before passing one back to a command or opening it; only `--doc` takes a relative path.
Exit code 2 is a bug in the tool rather than a bad argument: the same two fields plus `"unexpected": true`, `exception` and `stackTrace`.
Report it and stop; re-running the same command will not help.

## When the SDK is too old

`dotnet tool exec` needs the .NET 10 SDK or later, and an older one fails before NuGet is ever consulted, so the failure carries no `NU####` code and none of the rows below apply.
It arrives as `error: Unrecognized command or argument 'exec'` on an SDK whose `dotnet tool` has no `exec` verb, or as an `MSB` / SDK-resolver message naming the required version when a `global.json` pins one that is not installed.
Report it with `dotnet --version` and stop.
It is the machine that is short of a prerequisite, not the repository or the pin, so do not fall back to `dotnet tool install`, `dnx`, or an older version of the package: the pin is what keeps these documents and the tool one release.

## When the package will not resolve

A failure that names the package rather than the subcommand — `Aetos.RoslynSkills.Tools` could not be resolved, or no version matches the pin — comes from NuGet before the tool ever starts, so it arrives as plain SDK text rather than the JSON above.
Being absent from the nuget.org website or from `dotnet package search` is a separate index that lags far longer and never affects `dotnet tool exec`.

Which failure it is, is told by the `NU####` code rather than by the message around it: the codes are the same in every language, the prose is not.

| Code | What happened | What to do |
|------|---------------|------------|
| `NU1101`, `NU1102`, `NU1103` | nuget.org answered; the package, that version, or a stable version of it is not there | Do not re-run the command. Report it, and tell the user to wait a few minutes and run the skill again, since a version published minutes ago arrives on the download endpoint with a delay. |
| `NU1301`, `NU1302` | the feed itself could not be loaded — DNS, proxy, TLS, a 5xx from the source | Wait a few seconds and run the same command once more. When it fails again, report it and stop. |
| anything else, including HTTP 401 / 403 | not a case this file knows | Report it with the output and ask the user how to proceed. |

That single retry on `NU1301` / `NU1302` is the only waiting this workflow does.
Everywhere else, re-running is the user's decision: an agent that sleeps and retries on its own turns a clear failure into a session that looks busy while nothing is happening.

**Never lower the pin, float it, or reach for whatever version does resolve**: the pinned number is what makes these documents and the tool one release, and an older tool either rejects an argument written here or, worse, accepts it and behaves differently, which reaches the user as a wrong edit rather than as an error.
A version that will not resolve means the release it belongs to is not on NuGet.org, which is a defect in this skill's own release and has nothing to do with the repository being worked on.
Say that and name the version.
Do **not** ask the user to publish the package: whoever installed this skill is not necessarily the person who releases it, and for anyone but its maintainer that is a request they cannot act on.
Reporting it to the skill's repository is worth suggesting once the wait has not cleared it — say, after ten minutes — and it is theirs to decide on.
