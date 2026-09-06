# TODO

Notes for maintainers.
Nothing here is distributed: only `plugin/` goes into the package.

## Layer 2 — an analyzer that checks analyzers

A NuGet package complementing `Microsoft.CodeAnalysis.Analyzers`, never duplicating what it already reports.
Scope is whatever a single compilation can see:

- literal strings and resx mixed across the descriptors of one project
- a band header comment (`// Usage (FAB1xxx)`) disagreeing with the `category` argument, which is readable because comments are trivia
- descriptor field name against the ID constant name
- duplicate IDs, and numbers outside their category's band
- a descriptor that is reported but missing from `SupportedDiagnostics`
- `helpLinkUri` deviating from the shape the rest of the project uses

Separate the general truths from this repository's house style.
Mixing and a missing `SupportedDiagnostics` entry are defects anywhere; the band scheme is our opinion and should be opt-in through `.editorconfig` analyzer options.

Side effect worth having: the conventions become an executable specification, so `evals/AddDiagnostic/evals.json` could assert "this analyzer reports nothing" instead of matching regexes.

Open: whether RS1007 is enabled by default, which decides whether a mixing rule is redundant.
This could not be verified — Learn has no RS1xxx pages, and neither the GitHub MCP server nor `gh search code` reaches `dotnet/roslyn-analyzers`.

## Layer 3 — a doctor skill

For what no analyzer can see:

- whether the page `helpLinkUri` names actually exists
- `Microsoft.CodeAnalysis.Analyzers` not referenced at all, which is layer 2's own blind spot and the reason layer 3 has to exist
- a missing `AnalyzerReleases.Shipped.md` / `Unshipped.md` pair
- resx registration in the csproj, and the shape of the documentation directory
- whether a documentation URL can be built from the git remote

Build layer 2 first: layer 3's scope depends on what layer 2 catches, not the reverse.
Both wait until `add-diagnostic`'s conventions settle, since those conventions are the specification.

## resx maintenance — mostly not this repository's business

Three things are wanted, and only the first is about Roslyn:

1. find descriptors mixing literal strings and resx, and unify them on resx
2. check that every resx group has every ID in every culture file, and fill the gaps in both directions — an ID the neutral file has and a satellite lacks, and an orphan only a satellite has
3. add a new language to an existing resx group

2 and 3 apply to any .NET project with localized resources, so they do not belong here.
1 splits: detection is layer 2 or layer 3, and the conversion itself belongs with 2 and 3.

2 is worth building because the compiler is silent about it.
Measured: a project with `Resources.resx` (Alpha, Beta) and `Resources.fr.resx` (Alpha, Gamma) builds with no warning at all, under `AnalysisLevel=latest-all` and `WarningLevel=9999` too.
The missing entry falls back to the neutral language at run time, and the orphan ships in `fr/*.resources.dll` where nothing can reference it.

If these move to another plugin, the tool here already holds the general resx parts — `AddResxEntriesCommand`, `Internal/ResxName.cs`, and most of `FindConventionsCommand`'s resx-group detection.
Duplicate them, extract a shared package, or keep one tool that both plugins pin; not decided.

## add-diagnostic and the mix it creates

`add-diagnostic` writes the new descriptor to resx when the request asks for it, even where the project's existing descriptors use literals.
That leaves the project mixed on purpose, and the skill says nothing about it.
Once a doctor skill exists, those cases should also suggest running it.
Until then nothing about this reaches the end user.
