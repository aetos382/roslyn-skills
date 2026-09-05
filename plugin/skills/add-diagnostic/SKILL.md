---
name: add-diagnostic
description: This skill should be used when the user asks to "add a diagnostic", "add a new rule", "add an analyzer rule", "create a DiagnosticDescriptor", "add a diagnostic ID", "register a new diagnostic", "add a suppression", "add a DiagnosticSuppressor", "診断を追加", or describes a new warning, error, or suppression a Roslyn analyzer, source generator, or suppressor should report. Adds the ID constant, descriptor, its strings as resx entries or literals, AnalyzerReleases.Unshipped.md row, and optional rule docs following repository conventions. Not for implementing analysis logic, code fixes, or tests.
argument-hint: <what the diagnostic should report, in plain language>
allowed-tools: Read, Write, Edit, Glob, Grep, Bash, AskUserQuestion
---

# Add a Diagnostic

Add every artifact a new Roslyn diagnostic (or suppression) needs, in one pass, without implementing the analysis itself.
The deliverable is a consistent set of edits:

| Artifact | Diagnostic | Suppression |
|----------|------------|-------------|
| ID constant in the IDs file | `DiagnosticIds.cs` | `SuppressionIds.cs` |
| Descriptor in the reporting class | `DiagnosticDescriptor` + `SupportedDiagnostics` | `SuppressionDescriptor` + `SupportedSuppressions` |
| Strings, as literals or resx entries in every culture file (Step 3) | `{Name}Title`, `{Name}Message`, `{Name}Description` | `{Name}Justification` |
| `AnalyzerReleases.Unshipped.md` row | yes | no |
| Documentation page + index row | optional (drives `helpLinkUri`) | only if the repo already documents suppressions |

`$ARGUMENTS` is a plain-language description of what the diagnostic should report.
When it is empty, ask for it before anything else.

## The helper tool

The four helpers live under the `add-diagnostic` command group of the `Aetos.RoslynSkills.Tools` .NET tool, run straight from NuGet.org without installing anything.
It needs the .NET 10 SDK or later.
`find-conventions` runs `dotnet msbuild` once per project, in parallel, so project data comes from a real evaluation rather than from reading XML: custom `.props`/`.targets`, central package management and conditions are all resolved.
Expect a second or two on a small repository, plus a few seconds on the first run while the tool package downloads.
From Bash:

```bash
cd "$SCRATCH"                                       # working directory: outside the repository
export DOTNET_CLI_UI_LANGUAGE=en VSLANG=1033        # English output from every dotnet command, see below
T="dotnet tool exec Aetos.RoslynSkills.Tools@0.1.5 -- add-diagnostic"   # not dnx, see below
R="/absolute/path/to/the/repository"

$T find-conventions --path "$R" --summary > conventions.json    # then read that file
$T next-id --ids-file "$R/src/X/DiagnosticIds.cs" --category Usage
$T next-id --ids-file "$R/src/X/DiagnosticIds.cs" --prefix ABC --band 1     # fresh repository
$T next-id --ids-file "$R/src/X/SuppressionIds.cs" --suppression
$T add-resx-entries --ids-file "$R/src/X/DiagnosticIds.cs" --resx "$R/src/X/Resources.resx"    --entries entries.en.json
$T add-resx-entries --ids-file "$R/src/X/DiagnosticIds.cs" --resx "$R/src/X/Resources.ja.resx" --entries entries.ja.json
$T doc-url --doc docs/rules/ABC1001.md --path "$R"             # --doc stays repository-relative
```

**Do not use `dnx`**, the documented shorthand for the same command.
It is a script, not an executable, so Bash on Windows — which is where this skill runs its commands — resolves it only as `dnx.cmd` and reports `dnx: command not found` otherwise.
`dotnet` is an executable and needs no such workaround.

Keep the `--`: `dotnet tool exec` forwards unknown arguments to the tool, but it claims `--help`, `--version`, `-v`, `--source`, `--add-source`, `--configfile`, `--prerelease` and `--interactive` for itself, so `... doc-url --help` prints its own help instead.
Everything after `--` is passed through untouched.
The tool carries one command group per skill, so `add-diagnostic` always comes before the subcommand.
Pin the version as shown, so the skill and the tool it invokes stay in step.
Only `--doc` is repository-relative, because it becomes part of a URL; everything else is an absolute path.

Pass `-nodeReuse:false` to every `dotnet build` or `dotnet msbuild` the workflow runs, so no MSBuild worker process outlives it holding file locks.
`find-conventions` already does this internally.
The switch is spelled `-nodeReuse:false` or `-nr:false`; `/node-reuse:false` is rejected with MSB1001.

Export `DOTNET_CLI_UI_LANGUAGE=en` and `VSLANG=1033` once, before the first command, so every `dotnet` invocation of the workflow answers in English.
The SDK and MSBuild otherwise speak the machine's language, and this file documents its failures — MSB1001, CS0117, IDE0090, the NuGet text below — in English only, so a localized build log is one the workflow cannot match against anything written here.
The first covers the CLI, the second the MSBuild engine and the compilers it starts; the tool sets both for the processes it starts itself, which does not reach the commands run here.

Every subcommand prints JSON on stdout, including for expected failures (`{"error": ..., "hint": ...}` with exit code 1) and for a mistyped command line, so read the output rather than guessing what it would return.
Exit code 2 is a bug in the tool rather than a bad argument: the same two fields plus `"unexpected": true`, `exception` and `stackTrace`.
Report it and stop; re-running the same command will not help.

A failure that names the package rather than the subcommand — `Aetos.RoslynSkills.Tools` could not be resolved, or no version matches the pin — comes from NuGet before the tool ever starts, so it arrives as plain SDK text rather than the JSON above.
When the pinned version was published minutes earlier it is usually the download endpoint still catching up, which clears on its own within a few minutes; that is a thing to tell the user, not a reason to run the command again.
Being absent from the nuget.org website or from `dotnet package search` is a separate index that lags far longer and never affects `dotnet tool exec`.

Which failure it is, is told by the `NU####` code rather than by the message around it: the codes are the same in every language, the prose is not.

| Code | What happened | What to do |
|------|---------------|------------|
| `NU1101`, `NU1102`, `NU1103` | nuget.org answered; the package, that version, or a stable version of it is not there | Do not re-run the command. Report it, and tell the user to wait a few minutes and run the skill again, since a version published minutes ago arrives on the download endpoint with a delay. |
| `NU1301`, `NU1302` | the feed itself could not be loaded — DNS, proxy, TLS, a 5xx from the source | Wait a few seconds and run the same command once more. When it fails again, report it and stop. |
| anything else, including HTTP 401 / 403 | not a case this file knows | Report it with the output and ask the user how to proceed. |

That single retry on `NU1301` / `NU1302` is the only waiting this workflow does.
Everywhere else, re-running is the user's decision: an agent that sleeps and retries on its own turns a clear failure into a session that looks busy while nothing is happening.

**Never lower the pin, float it, or reach for whatever version does resolve**: the pinned number is what makes this file and the tool one release, and an older tool either rejects an argument written here or, worse, accepts it and behaves differently, which reaches the user as a wrong edit rather than as an error.
A version that will not resolve means the release it belongs to is not on NuGet.org, which is a defect in this skill's own release and has nothing to do with the repository being worked on.
Say that and name the version.
Do **not** ask the user to publish the package: whoever installed this skill is not necessarily the person who releases it, and for anyone but its maintainer that is a request they cannot act on.
Reporting it to the skill's repository is worth suggesting once the wait has not cleared it — say, after ten minutes — and it is theirs to decide on.

**Run it from the scratchpad**, not from anywhere inside the target repository, and pass absolute paths for `--path`, `--ids-file`, `--resx`, and `--entries`.
`dotnet` resolves its SDK from the first `global.json` found in the working directory **or any ancestor of it**, so a pin at the repository root applies just as much when the working directory is several folders below it, and a pinned version that is not installed fails outright with "A compatible .NET SDK was not found".
Every subcommand takes the repository path as an argument for this reason.
The constraint applies only to the tool; build the analyzer project itself from the repository, where its pinned SDK is the correct one.

## Workflow

### Step 1: Detect conventions

Run `find-conventions` with `--path <repository>` and `--summary`, from the scratchpad, redirecting to a file there.
Read the fields listed below; the rest of the JSON describes projects and resource groups the workflow does not touch, so skip it.
Ask nothing in this step: collect what is undecided and put it in the single question round of Step 3.

- `diagnosticPrefix`, `diagnosticIds`, `suppressionIds` — prefix, IDs file paths, class visibility, digit count, existing IDs, and category bands (`// Design (ABC1xxx)` headers).
- `diagnosticCategories` — the class holding the `category` constants (path, class name, visibility, existing values).
  Never search for it by hand; when it is null the repository has none.
- `projects[]` — kind (`analyzer`, `codefix`, `generator`, `test`, `roslyn-component`, `other`), the analyzer / suppressor / generator classes with their files, and `langVersion`, `targetFrameworks`, `neutralLanguage`.
  A non-null `evaluationError` means MSBuild could not evaluate that project, so its package, reference and resource data is missing: say so rather than treating the project as empty.
- `resx[]` — resource groups per project, all culture files, `generator` (the `<Generator>` on the EmbeddedResource, which says who writes the strongly typed class), `resourceClass`, `neutralLanguage` (the language of the neutral file, null when the project declares none), `localizableStringHelper` (a hand-written `GetLocalizableResourceString(string)`-style method with its `accessibility`), `localizableStringProperties` (existing `LocalizableResourceString` properties, their `style` `nested` / `suffix`, `nestedClass`, and `file`), and `requiresVisualStudioRegeneration`.
- `docs` — `directory` and `indexFile` when rule pages already exist, plus `candidateDirectories` (existing `docs` / `doc` / `Documentation` / `wiki` / `rules` folders, shallowest first, each with the `markdownFiles` it holds and the `files` of any kind — a directory with pages, one holding source, and one an interrupted run left empty are three different things), `mentionFiles` (Markdown elsewhere that names existing IDs), and `suggestedDirectory` (where a new page should go, `docs/rules` when nothing exists).
- `leftovers` — things that exist and hold nothing, each with `kind`, `path` and `detail`: `emptyDocumentationDirectory`, `categoriesClassWithoutConstants`, `analyzerReleasesWithoutRules`.
  Step 2.5 reports them; nothing here is a reason to skip a step.
- `analyzerReleases[]` — the Shipped/Unshipped pair per project and `analyzersPackage` (`direct` / `viaCodeAnalysis` / `none`), a hint about the release-tracking analyzer that only 5e proves.
- `git.docUrlTemplate`, `idSharing` (`AnalyzerProject`, `LinkedFile`, `SharedProject`, `SharedFile`, `none`, or `mixed` when the code-fix projects differ — where the IDs live, see `references/id-conventions.md`), `idSharingReliable` (false when a project MSBuild could not evaluate leaves `none` meaning "unknown"), `diagnosticIdsProject` (null when the IDs file belongs to no project), `config`.
  The repository-wide value is a summary; the value 5g acts on is `projects[].idSharing`, which every code-fix project carries.
- `excludedPluginDirectories` — Claude Code plugin trees found inside the repository and skipped, this plugin included when it is installed there.
  Their sample files are documentation and must never be read as the repository's conventions; do not go looking in them by hand either.

When something is missing, create only that piece: ask for the prefix only when `diagnosticPrefix` is null (no config, no existing IDs, no band header carrying one; an empty IDs file is not enough); ask for the analyzer project only when none is detected; create categories / releases files alongside the existing ones, and a resx only when Step 3 asked for one.
New IDs files follow the *structure* of `examples/DiagnosticIds.cs`, `examples/SuppressionIds.cs`, and `examples/DiagnosticCategories.cs` with the user's namespace and prefix, one band header for the new category, and none of the sample constants (defaults in `references/id-conventions.md`).

Read one existing descriptor in the target project (if any) before writing a new one; the new code must look like its neighbours (helper methods, argument style, resource class).
Read `config.notes` and follow any instructions found there.

### Step 2: Classify the request

Decide whether the description asks for a **diagnostic** (report something) or a **suppression** (stop another rule from reporting in a situation).
Words such as "suppress", "should not warn about", "CA1515 is wrong for test classes" mean suppression.
When unclear, ask.

### Step 2.5: Check whether it already exists

Never assume the request is new.
Repeated runs against the same repository, including this skill's own testing, routinely land on a diagnostic that is already there.

Compare the request against `diagnosticIds.ids` / `suppressionIds.ids`: look for the same name, an obvious synonym, or a constant whose descriptor reports the same pattern (grep the analyzer project for candidates when a name looks close).

- **Exact match** — do not add anything.
  Build the artifact checklist for that ID (constant, descriptor and `SupportedDiagnostics` entry, its strings — the three resx entries in every culture file, when that is the route the descriptor takes, the AnalyzerReleases row, the documentation page and index row, `helpLinkUri`), mark each present or missing, show the table, and run Steps 3 to 7 for the missing artifacts only.
  Say plainly that the diagnostic already existed.
- **Partial match** — a different name reporting the same pattern, or the same name with different wording.
  Ask whether to add a new diagnostic or amend the existing one, and stop until answered.
- **No match** — continue normally.

Also look for leftovers from an interrupted earlier run, which are not ID collisions and would otherwise go unnoticed.
Step 1's `leftovers` already lists the empty documentation directory, the categories class with no constants, and the `AnalyzerReleases.Unshipped.md` with no rows; add resx entries whose ID constant does not exist, which detection does not cover.
List what you find in the report so the user can decide whether it is stale.

### Step 3: Design the diagnostic

Draft before asking, so the user reviews concrete text rather than open questions:

1. **Name**: PascalCase normative statement (`TaskShouldBeAwaited`, `AbstractTypeShouldNotHavePublicConstructor`; `TestClassesMayBePublic` for suppressions).
   Rules and examples are in `references/id-conventions.md`.
2. **Category**: infer from the description when obvious (naming, usage, design, performance, ...) and match it to the repository's existing categories; leave it open when not obvious.
3. **Title / Message / Description** (or **Justification**): draft following the format rules in `references/descriptors.md` (title without trailing period, message with `'{0}'` placeholders and no trailing period, description as full sentences).
4. **Target class**: the analyzer, generator, or suppressor class that will report it; propose an existing one when the description matches, otherwise propose a new class name.
5. **Documentation**: propose *yes* with the target path, always.
   Use `docs.directory` when it exists, otherwise `docs.suggestedDirectory` (a missing folder is created, not a reason to skip).
   Check `docs.mentionFiles` first: when rules are documented in one shared page or a README table, propose extending that file instead of creating a new layout.
6. **Where the strings live**: read one existing `DiagnosticDescriptor` in the target project and follow it, whatever it does.
   Literal `title` / `messageFormat` / `description` arguments → write literals too.
   `LocalizableResourceString` (however it is reached) → write the new entries into the same resx group the neighbours use.
   Do not weigh RS1007 against the neighbour and do not check whether the project enables it: the existing descriptors are the decision, and a project mixing both forms is worse than either.
   Only when the project has **no descriptor at all** is this open, and then it becomes a question with the resx route recommended.
   Draft the option the resx route would take, since it decides what the question offers:

   | The project has | The resx option is |
   |-----------------|--------------------|
   | a resx group holding diagnostic strings (`*Title` / `*Message` entries) | that group, named in the option |
   | a resx group holding other strings only | that file, named in the option — moving in with somebody else's strings is the user's call, never assumed |
   | no resx at all | a new `Resources.resx` beside the descriptor, named in the option — creating it, and registering it in the csproj, is the user's call too |

   Name the file in the option text either way, so answering it is also the consent to create or share that file.
   When several resx groups qualify, offer the likeliest and say the others exist.

Then print the proposal as a short table in the message (name, ID band/category, title, message, description, target class, where the strings go, documentation yes/no) and, directly after it, ask **one** `AskUserQuestion` round.
It is the only round in the whole workflow: everything left open by Step 1 (the prefix, when `diagnosticPrefix` is null) and by this step goes into it.
Candidates, in priority order when more than four exist:

1. prefix (only when null; it names every future ID)
2. where the strings live (only when the project has no descriptor to follow; it is also the consent to create or share a resx file)
3. severity
4. `suppressedDiagnosticId` (suppressions only)
5. message arguments
6. category
7. documentation

The strings question ranks high because it is a consent, not a preference: without an answer there is no file to write the entries to.
Category and message arguments come first among the rest because a drafted guess for them is often wrong.
Documentation is last because the proposal is always *yes* with a concrete path: it counts as offered once it appears in the table, whether or not it also gets a question.
The same holds for anything else pushed out by the four-question cap, since the user can correct any row in an "Other" answer.
Keep question texts short; the table carries the context.
An option's description says what that option means and nothing else: the ID and its band belong to the table, and spelling them out under each option is worse than useless in a repository with no bands yet, where the next unused band is 1 whichever category wins and all four options end up claiming the same number.
Skip the round entirely when the request already settles everything.
Re-draft once from the answers; do not loop.

Apply `customTags` only when required (`CompilationEnd`, `Unnecessary`, `NotConfigurable`; see `references/descriptors.md`), otherwise omit the argument.

### Step 4: Allocate the ID

Run `next-id` with `--ids-file` and `--category <name>` (diagnostic) or `--suppression` (suppression).
This step decides a value and writes nothing; every file edit belongs to Step 5.

When Step 1 reported `diagnosticPrefix` as null, do not try `--category` at all: run `--prefix <PREFIX> --band <n>` straight away, using the prefix answered in Step 3.
Without it the command returns `{"error": ..., "hint": ...}` and no ID, which is a normal outcome for a repository with no diagnostics yet, not a broken command.

The IDs file may not exist yet either — 5a is what creates it — so passing `--prefix` (and `--band <n>` for a diagnostic) allocates the first ID of a file that is not there.
The output then says `"idsFileExists": false`, which is the reminder that 5a has a file to create and not just a line to insert.
Without `--prefix` a path that does not exist is reported as an error rather than read as an empty file: a mistyped path would otherwise restart the numbering and hand out an ID the repository has already shipped.

When the output has `"unresolvedCategory": true`, the category has no band yet: choose the next unused band digit and re-run with `--prefix <PREFIX> --band <n>`, then note that 5a must also write the `// <Category> (<PREFIX><n>xxx)` header.
Once such a header exists, `--category <name>` alone is enough, because the command reads both the band and the prefix from it.
Never renumber or reuse an existing value.

### Step 5: Apply the edits

Perform the edits in this order (5a–5h) so later edits can rely on earlier ones:

- **5a.
  IDs file**: add the `// <Category> (<PREFIX><n>xxx)` band header when Step 4 found none, then insert `public const string {Name} = "{Value}";` inside that block, sorted by number (`references/id-conventions.md`, "Layout of the IDs file").
  No `#region`.
  Create `DiagnosticIds.cs` / `SuppressionIds.cs` when missing (see Step 1 for how to derive them from `examples/`).
- **5b.
  Categories class** (diagnostics only): add the constant when the category is new, to the class named by `diagnosticCategories.path`.
  When that is null, create `DiagnosticCategories.cs` next to the IDs file from `examples/DiagnosticCategories.cs`, matching the IDs class visibility.
- **5c.
  Descriptor**: when documentation was requested, run `doc-url --doc <intended page path>` now (it needs only the path, not the file) and pass the URL as `helpLinkUri`; omit the argument otherwise.
  The **route the strings take** was settled in Step 3 (see "Where the strings live" there): either literals passed straight to the constructor, or a resx entry reached through a `LocalizableResourceString`.
  For literals, write the text into the descriptor and skip 5d entirely.
  For the resx route, decide how the entries are reached, in this order (`references/descriptors.md`, "Localizable strings"):
    1. `localizableStringProperties` exists → add the three properties (one for a suppression) in the same file, class, and style, then reference them (`Resources.Localizable.{Name}Title`).
    2. Otherwise `localizableStringHelper` exists and is `private` → create the nested `public static class Localizable` in the helper's file (`examples/Resources.Roslyn.cs`) with the properties, and reference them.
       Never widen the helper's accessibility.
    3. Otherwise the helper is `internal`/`public` → call it directly.
    4. Otherwise mirror the neighbouring descriptor, or fall back to `new LocalizableResourceString(...)`.
       Add the `private static readonly` field named `{Name}` to the target class, mirroring the neighbouring descriptor's style.
       A scaffolded analyzer often declares `SupportedDiagnostics { get; }` with **no initializer**, which throws at runtime; adding the first descriptor means initialising it rather than appending to an existing list (see below).
       Severity is one decision spelled three ways, so convert it consistently here and in 5e:

  | User says | `defaultSeverity` | AnalyzerReleases `Severity` | `.editorconfig` |
  |-----------|-------------------|-----------------------------|-----------------|
  | error | `DiagnosticSeverity.Error` | `Error` | `error` |
  | warning | `DiagnosticSeverity.Warning` | `Warning` | `warning` |
  | suggestion, info | `DiagnosticSeverity.Info` | `Info` | `suggestion` |
  | hidden, silent | `DiagnosticSeverity.Hidden` | `Hidden` | `silent` |

  Write the new code in its **conservative** form and let the build ask for the shorter one: the explicit `new DiagnosticDescriptor(...)` rather than a target-typed `new(...)`, and `ImmutableArray.Create(...)` rather than a collection expression.
  Both shorter forms depend on the project (`LangVersion`, a `System.Collections.Immutable` new enough to carry `CollectionBuilderAttribute`, possibly a polyfill package), and detection cannot see all of that; the conservative form compiles either way, and where the repository wants the other one the build says so as IDE0090 or IDE0303, which 5e fixes.
  Add the field to `SupportedDiagnostics` / `SupportedSuppressions` (source generators have neither; the field alone suffices), in whichever of these two situations the class is in:

  | The property | What to write |
  |--------------|---------------|
  | already lists descriptors | Add the new one the way the existing ones are listed — collection expression, `ImmutableArray.Create`, `CreateRange`, a separate array field. Whatever compiles there compiles for one more entry, and the property is one expression: half of it in another form reads as an accident. |
  | is declared with no initializer | Initialise it conservatively: `= ImmutableArray.Create<DiagnosticDescriptor>(Xxx);`, `LangVersion` 12 or later included. |
  When the message has two or more placeholders, add a comment above the descriptor listing the argument order.
  Create the class from `examples/AnalyzerWithDescriptor.cs` or `examples/SuppressorWithDescriptor.cs` when it does not exist; leave `Initialize` / `ReportSuppressions` without analysis logic.
- **5d. resx** (resx route only; the literal route has nothing to do here): write to the resource group Step 3 settled on.
  Never write diagnostic strings into a resx that holds none, or create a resx, on your own reading of the repository: that file was named in the Step 3 option the user answered.
  A new file is written from the neutral file of an existing group and then **registered in the csproj**, with the metadata the repository's own resx files imply (`references/resources.md`, "Creating a new resx file"): `<Generator>ResXFileCodeGenerator</Generator>` where the repository uses that generator, an empty `<Generator></Generator>` where it uses `Microsoft.CodeAnalysis.ResxSourceGenerator`, and the `StronglyTyped*` metadata when it uses neither.
  The first of those makes the class Visual Studio's to generate, so treat it as `requiresVisualStudioRegeneration` from here on; the second needs that project to already reference the package, which is a package reference to ask about rather than add.
  **Copy every culture file of that group to the scratchpad first**; the files may already carry uncommitted work, so `git checkout --` is not a recovery path and must not be used.
  Then run `add-resx-entries` **once per culture file**, each with its own entries JSON in the scratchpad and `--ids-file` pointing at the IDs file edited in 5a.
  The neutral file (`Resources.resx`) is written in `resx[].neutralLanguage` (the project's `<NeutralLanguage>`, English only when that is null and the existing entries are English); every `Resources.<culture>.resx` carries a translation into that culture's language, taken from the file name, so `Resources.ja.resx` gets Japanese.
  Never copy the source text into a satellite file: it looks translated and never gets fixed.
  In the **neutral file only**, give every entry whose text contains a placeholder a `comment` saying what each one holds, in order (`{0} is the field name. {1} is the declaring type.`), so a translator can move them safely.
  Read each report: when `valid` is false, restore from the scratchpad copies and stop.
  Never edit `*.Designer.cs` (`references/resources.md`).
- **5e.
  AnalyzerReleases.Unshipped.md** (diagnostics only): build the analyzer project **before** adding the row, with `dotnet build <csproj> -nodeReuse:false` from inside the repository.
  The descriptor from 5c now exists with no row to match it, so a working release-tracking analyzer reports **RS2000** for the new ID, or **RS2008** ("enable analyzer release tracking") when the release files do not exist yet.
  Either one proves tracking runs, and it costs one build with no file to restore; a clean build after the row is added proves nothing, since it is equally consistent with the analyzer never running.
  If neither appears, add `Microsoft.CodeAnalysis.Analyzers` (`PrivateAssets="all"`) to the project and build again.
  Do **not** add `<AdditionalFiles>` items for the release files: the SDK registers them implicitly, and their absence from the project file is not a defect.
  This build is also where warnings introduced by 5c first appear, IDE0090 and IDE0303 among them: fix them now rather than waiting for Step 6.
  Then append the row `ID | Category | Severity | <short sentence describing the rule>` under `### New Rules`.
  When the pair is missing, create `AnalyzerReleases.Unshipped.md` from `examples/` and `AnalyzerReleases.Shipped.md` with **only its two comment lines** — copying the example's `## Release 1.0` section verbatim declares rules that do not exist and earns RS2002 (`references/analyzer-releases.md`).
  Skip this whole edit for suppressions, and skip the build when 5d reported `requiresVisualStudioRegeneration`.
- **5f.
  Documentation** (when requested): create the directory when it does not exist, then the page at the path used in 5c, following the newest existing page or `examples/rule-doc-template.md` when there is none; add the index row in sorted position, creating the index from `examples/rules-index-template.md` when the directory is new (`references/documentation.md`).
  For suppressions, create pages only when `docs.suppressionDocs` or a suppressions table already exists.
- **5g.
  ID sharing**: read `idSharing` on **each** code-fix project in `projects[]`, not the repository-wide value.
  A project whose value is anything but `none` already reaches the IDs, so skip that one; act on each project that says `none`, whatever the others say — a repository where one code fix is wired and another is not reports `mixed` at the top level, and the wired one says nothing about the other.
  For each of them, ask for `AnalyzerProject` (recommended) or `LinkedFile`, then add the `<ProjectReference>` or the linked `<Compile>` item to that project and set the IDs class visibility accordingly.
  One question covers them all when the answer is the same for each.
  When `idSharingReliable` is false, `none` only means the detection could not see: say so and ask before adding anything, since the reference may already be there in a project MSBuild failed to evaluate.
  When `diagnosticIdsProject` is null the IDs file belongs to no project, so add the linked `<Compile>` item to each side (`SharedFile`) instead of moving it.
- **5h.
  Config file**: create or update `.claude/roslyn-skills/add-diagnostic.md` (`examples/add-diagnostic.md`, creating the directory when missing) only when a decision was made that detection cannot reproduce next time: a non-GitHub URL template, a docs layout the scan misreads, a descriptor helper worth naming in the notes.
  A new prefix or a new band is **not** such a decision — 5a writes the `// <Category> (<PREFIX><n>xxx)` header, and detection reads both back from it, as Step 6 confirms.
  Creating the file for those leaves two sources of truth.

Match each file's existing indentation, line endings, and blank-line pattern.
Use `Edit` for insertions into existing files; use `Write` only for new files.

`Write` produces LF and no BOM, which is wrong for a repository whose `.csproj` files carry one, so read the two properties off a neighbouring file of the same kind before creating one beside it:

```bash
git -C "$R" ls-files --eol -- <neighbouring file>   # i/lf w/crlf attr/text: what git stores and checks out
head -c 3 "$R/<neighbouring file>" | od -An -tx1    # ef bb bf means the file starts with a BOM
```

A repository is routinely mixed — `.cs` as LF without a BOM, `.csproj` as CRLF with one — so ask the neighbour, not the repository.
Where `.gitattributes` sets `eol`, it wins over what the working copy happens to hold.

### Step 6: Verify

- Re-run `find-conventions --summary` and confirm the new ID appears under `diagnosticIds.ids` / `suppressionIds.ids`, and — on the resx route — that the resx report was `valid`.
- Confirm the RS2000 or RS2008 observation from 5e happened and that the row now silences it.
- Grep the target project for the new name: the IDs file, the descriptor, `SupportedDiagnostics` (or `SupportedSuppressions`), and — on the resx route — every culture file must contain it.
- Build the analyzer project (`dotnet build <csproj> -nodeReuse:false`) **only when** `requiresVisualStudioRegeneration` is false; when it is true the build is expected to fail with CS0117 until Visual Studio regenerates `Resources.Designer.cs`, so skip the build and say so.
  Fix every warning the new code introduced (compare against a build before the change when unsure); under `EnforceCodeStyleInBuild` with `AnalysisLevel` `latest-all`, typical ones are IDE0090 (use `new(...)` for the descriptor), IDE0303 (use a collection expression) and CS1574 (unresolvable `cref` in a project without Microsoft.CodeAnalysis).

### Step 7: Report

Summarize in a short list: the ID and name, every file changed or created, the `helpLinkUri` (or that it was omitted), and — when a new resx was created whose class is generated at build time — that the class lives in `obj/` and an unbuilt clone shows it as undefined until the first build, and the next steps the skill cannot do: regenerate `Resources.Designer.cs` in Visual Studio when flagged, implement the analysis / suppression logic, add tests, and translate the satellite resx files when the strings went to resx.

## Rules that always hold

- The IDs file contains only constants and band headers; descriptors live in the reporting class.
- The descriptor field, the ID constant, and (on the resx route) the resx stem share one name.
- Every culture file of the resource group gets the same entries; only `.resx` files are edited.
- The existing descriptors decide literals or resx; only a project with none asks, and creating or sharing a resx file is part of that answer, never assumed.
- Never change the accessibility, signature, or name of an existing member to make new code compile; a `private` helper means the entry point belongs next to it, in the same class.
- Back up to the scratchpad before any edit that may need undoing.
  Never recover with `git checkout --`, `git restore`, or `git stash`: the file may hold work from before this run.
- Check whether the diagnostic already exists before adding anything (Step 2.5).
- Suppressions: separate IDs file, independent sequence, `Justification` only, no AnalyzerReleases row.
- `helpLinkUri` points at a page that exists once 5f has run, or is omitted.
  Between 5c and 5f the URL deliberately points at a page not yet written.
- Documentation is always offered, never skipped silently; a missing documentation directory is created, not treated as a decision.
- Ask for severity, category, and message arguments unless the request already states them.
- Do not implement analysis logic, code fixes, or tests; offer them as follow-ups.

## Additional resources

### Reference files

- **`references/id-conventions.md`** — naming rules, prefix and band scheme, IDs file layout, suppression IDs, and how real projects (StyleCop, xunit, Roslynator, Meziantou) share IDs with code-fix projects.
- **`references/descriptors.md`** — DiagnosticDescriptor / SuppressionDescriptor patterns, text format rules (RS1031–RS1033), severity guidance, required `customTags`, source generator descriptors.
- **`references/resources.md`** — resx entry naming and ordering, `add-resx-entries` usage, Designer.cs regeneration matrix.
- **`references/analyzer-releases.md`** — AnalyzerReleases file format, Notes column convention, RS2000 family.
- **`references/documentation.md`** — documentation layout, page and index templates, URL resolution, and the `.claude/roslyn-skills/add-diagnostic.md` configuration schema.

### Examples

- **`examples/DiagnosticIds.cs`**, **`examples/SuppressionIds.cs`**, **`examples/DiagnosticCategories.cs`** — canonical IDs and categories files.
- **`examples/AnalyzerWithDescriptor.cs`**, **`examples/SuppressorWithDescriptor.cs`** — descriptor placement and `SupportedDiagnostics` / `SupportedSuppressions` wiring.
- **`examples/Resources.Roslyn.cs`** — hand-written partial of the resource class with a private helper and the nested `Localizable` property class.
- **`examples/AnalyzerReleases.Shipped.md`**, **`examples/AnalyzerReleases.Unshipped.md`** — release tracking files.
- **`examples/rule-doc-template.md`**, **`examples/rules-index-template.md`** — documentation templates.
- **`examples/add-diagnostic.md`** — configuration file with every supported key.
- **`examples/resx-entries.json`**, **`examples/resx-entries.ja.json`** — input format for `add-resx-entries`, one file per culture.

### Tool subcommands

`dotnet tool exec Aetos.RoslynSkills.Tools@0.1.5 -- add-diagnostic <subcommand>`, see "The helper tool" above.

- **`find-conventions`** — repository convention detection (JSON).
- **`next-id`** — next free ID in a category band or suppression sequence.
- **`add-resx-entries`** — ordered resx insertion with validation.
- **`doc-url`** — documentation URL from the git remote or a template.
