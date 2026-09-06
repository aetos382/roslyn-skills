---
name: add-diagnostic
description: This skill should be used when the user asks to "add a diagnostic", "add a new rule", "add an analyzer rule", "create a DiagnosticDescriptor", "add a diagnostic ID", "register a new diagnostic", "add a suppression", "add a DiagnosticSuppressor", "診断を追加", "警告を追加", "アナライザーにルールを追加", "サプレッサーを追加", or describes a new warning, error, or suppression a Roslyn analyzer, source generator, or suppressor should report. Adds the ID constant, descriptor, its strings as resx entries or literals, AnalyzerReleases.Unshipped.md row, and optional rule docs following repository conventions. Not for implementing analysis logic, code fixes, or tests.
argument-hint: <what the diagnostic should report, in plain language>
allowed-tools: Read, Write, Edit, Glob, Grep, Bash, AskUserQuestion, TodoWrite
---

# Add a Diagnostic

Add every artifact a new Roslyn diagnostic (or suppression) needs, in one pass, without implementing the analysis itself.
The deliverable is a consistent set of edits:

| Artifact | Diagnostic | Suppression |
|----------|------------|-------------|
| ID constant in the IDs file | `DiagnosticIds.cs` | `SuppressionIds.cs` |
| Descriptor in the reporting class | `DiagnosticDescriptor` + `SupportedDiagnostics` | `SuppressionDescriptor` + `SupportedSuppressions` |
| Strings, as literals or resx entries in every culture file (Step 4) | `{Name}Title`, `{Name}Message`, `{Name}Description` | `{Name}Justification` |
| `AnalyzerReleases.Unshipped.md` row | yes | no |
| Documentation page + index row | optional (drives `helpLinkUri`) | only if the repo already documents suppressions |

`$ARGUMENTS` is a plain-language description of what the diagnostic should report.
When it is empty, ask for it before anything else.

## The helper tool

The four helpers live under the `add-diagnostic` command group of the `Aetos.RoslynSkills.Tools` .NET tool, run straight from NuGet.org without installing anything.
It needs the .NET 10 SDK or later.
`find-conventions` runs `dotnet msbuild` once per project, in parallel, so project data comes from a real evaluation rather than from reading XML: custom `.props`/`.targets`, central package management and conditions are all resolved.
Expect a second or two on a small repository, plus a few seconds on the first run while the tool package downloads.
From Bash, where each call starts a fresh shell:

```bash
# Preamble — repeat it at the top of every Bash call that runs the tool or a build.
# The working directory survives between calls; variables and exports do not.
export DOTNET_CLI_UI_LANGUAGE=en VSLANG=1033        # English output from every dotnet command
S="/absolute/path/to/the/scratch/directory"         # created once: S="$(mktemp -d)"; echo "$S"
R="/absolute/path/to/the/repository"
T="dotnet tool exec Aetos.RoslynSkills.Tools@0.1.6 -- add-diagnostic"   # not dnx, keep the --
cd "$S"                                             # never run the tool from inside $R

$T find-conventions --path "$R" --summary > conventions.json    # then read that file
$T next-id --ids-file "$R/src/X/DiagnosticIds.cs" --category Usage
$T next-id --ids-file "$R/src/X/DiagnosticIds.cs" --prefix ABC --band 1     # fresh repository
$T next-id --ids-file "$R/src/X/SuppressionIds.cs" --suppression
$T add-resx-entries --ids-file "$R/src/X/DiagnosticIds.cs" --resx "$R/src/X/Resources.resx"    --entries entries.en.json
$T add-resx-entries --ids-file "$R/src/X/DiagnosticIds.cs" --resx "$R/src/X/Resources.ja.resx" --entries entries.ja.json
$T doc-url --doc docs/rules/ABC1001.md --path "$R"             # --doc stays repository-relative
```

Create the scratch directory once, at the start of the run, and keep its absolute path in the report of every step that writes there; `$S` has to be spelled out again in each later call, and an unset `$S` turns `cd "$S"` into a no-op that leaves the tool running inside the repository, which is exactly what the scratchpad exists to prevent.
The same holds for the two exports: a `dotnet build` in a later call without them answers in the machine's language, and these documents name failures (MSB1001, CS0117, IDE0090, the `NU####` codes) in English only.

Type the commands as written: the pin, the `--`, `dotnet` rather than `dnx`, absolute paths for everything but `--doc`, and the scratchpad as the working directory.
Add `-nodeReuse:false` to every `dotnet build` and `dotnet msbuild` the workflow runs, so no MSBuild worker outlives it holding file locks.
Every subcommand prints JSON on stdout, failures included, so read what it printed rather than guessing what it would return.
Its paths are **repository-relative** — `diagnosticIds.path`, `resx[].files`, `docs.directory` and the rest — so prefix `$R/` before passing any of them back to a command or opening it, since every argument but `--doc` is absolute.

`references/tool.md` has the reasons behind each of those, and is the file to open when something goes wrong with the tool itself: a command line the tool rejects, exit code 2, an SDK too old to carry `dotnet tool exec`, or a package that will not resolve — the last of which has its own decision table, and neither of the last two is ever answered by lowering the pin.


## Workflow

Eight steps, and Step 6 alone makes eight edits, so track them as a todo list: an interrupted run leaves the repository half-edited, and the list is what says which half.

### Step 1: Detect conventions

Run `find-conventions` with `--path <repository>` and `--summary`, from the scratchpad, redirecting to a file there.
Read the fields listed below; the rest of the JSON describes projects and resource groups the workflow does not touch, so skip it.
Ask nothing in this step: collect what is undecided and put it in the single question round of Step 4.

- `diagnosticPrefix`, `diagnosticIds`, `suppressionIds` — prefix, IDs file paths, class visibility, digit count, existing IDs, and category bands (`// Design (ABC1xxx)` headers).
- `diagnosticCategories` — the class holding the `category` constants, null when the repository has none.
  Never search for it by hand.
- `projects[]` — `kind` (`analyzer`, `codefix`, `generator`, `test`, `roslyn-component`, `other`), the reporting classes with their files, `langVersion`, `targetFrameworks`, `neutralLanguage`, and `idSharing` on each code-fix project.
  A non-null `evaluationError` means MSBuild could not evaluate that project, so its package, reference and resource data is missing: say so rather than treating the project as empty.
- `resx[]` — one entry per resource group: every culture file, `generator`, `resourceClass`, `neutralLanguage`, `localizableStringHelper`, `localizableStringProperties`, `requiresVisualStudioRegeneration`.
  6c and 6d read them; `references/descriptors.md` and `references/resources.md` say what each one decides.
- `docs` — `directory` and `indexFile` when rule pages exist, `candidateDirectories` (documentation-ish folders, shallowest first, each with its `markdownFiles` and its `files` of any kind), `mentionFiles` (Markdown elsewhere naming existing IDs), `suggestedDirectory`.
  A directory with pages, one holding source, and one an interrupted run left empty are three different things, and only the counts tell them apart.
- `leftovers` — things that exist and hold nothing, each with `kind`, `path` and `detail`: `emptyDocumentationDirectory`, `categoriesClassWithoutConstants`, `analyzerReleasesWithoutRules`.
  Step 3 reports them; nothing here is a reason to skip a step.
- `analyzerReleases[]` — the Shipped/Unshipped pair per project, and `analyzersPackage` as a hint about the release-tracking analyzer that only 6e proves.
- `git.docUrlTemplate`, `idSharing` (the roll-up of the per-project values, `mixed` when they differ; `references/id-conventions.md`), `idSharingReliable` (false when a project MSBuild could not evaluate leaves `none` meaning "unknown"), `diagnosticIdsProject`, `config`.
- `excludedPluginDirectories` — Claude Code plugin trees inside the repository, skipped, this plugin included when it is installed there.
  Their sample files are documentation and must never be read as the repository's conventions; do not go looking in them by hand either.

When something is missing, create only that piece: ask for the prefix only when `diagnosticPrefix` is null (no config, no existing IDs, no band header carrying one; an empty IDs file is not enough) **and the request does not state one**; ask for the analyzer project only when none is detected; create categories / releases files alongside the existing ones, and a resx only when Step 4 asked for one.
New IDs files follow the *structure* of `examples/DiagnosticIds.cs`, `examples/SuppressionIds.cs`, and `examples/DiagnosticCategories.cs` with the user's namespace and prefix, one band header for the new category, and none of the sample constants (defaults in `references/id-conventions.md`).

Read one existing descriptor in the target project (if any) before writing a new one; the new code must look like its neighbours (helper methods, argument style, resource class).
Read `config.notes` and follow any instructions found there.


### Step 2: Classify the request

Decide whether the description asks for a **diagnostic** (report something) or a **suppression** (stop another rule from reporting in a situation).
Words such as "suppress", "should not warn about", "CA1515 is wrong for test classes" mean suppression.
When unclear, ask.

### Step 3: Check whether it already exists

Never assume the request is new.
Repeated runs against the same repository, including this skill's own testing, routinely land on a diagnostic that is already there.

Compare the request against `diagnosticIds.ids` / `suppressionIds.ids`: look for the same name, an obvious synonym, or a constant whose descriptor reports the same pattern (grep the analyzer project for candidates when a name looks close).

- **Exact match** — do not add anything.
  Build the artifact checklist for that ID (constant, descriptor and `SupportedDiagnostics` entry, its strings — the three resx entries in every culture file, when that is the route the descriptor takes, the AnalyzerReleases row, the documentation page and index row, `helpLinkUri`), mark each present or missing, show the table, and run Steps 4 to 8 for the missing artifacts only.
  Say plainly that the diagnostic already existed.
- **Partial match** — a different name reporting the same pattern, or the same name with different wording.
  Ask whether to add a new diagnostic or amend the existing one, and stop until answered.
- **No match** — continue normally.

Also look for leftovers from an interrupted earlier run, which are not ID collisions and would otherwise go unnoticed.
Step 1's `leftovers` already lists the empty documentation directory, the categories class with no constants, and the `AnalyzerReleases.Unshipped.md` with no rows; add resx entries whose ID constant does not exist, which detection does not cover.
List whatever turns up in the report, so the user can decide whether it is stale.

### Step 4: Design the diagnostic

Draft before asking, so the user reviews concrete text rather than open questions:

1. **Name**: PascalCase normative statement (`TaskShouldBeAwaited`, `AbstractTypeShouldNotHavePublicConstructor`; `TestClassesMayBePublic` for suppressions).
   Rules and examples are in `references/id-conventions.md`.
2. **Category**: infer from the description when obvious (naming, usage, design, performance, ...) and match it to the repository's existing categories; leave it open when not obvious.
3. **Title / Message / Description** (or **Justification**): draft following the format rules in `references/descriptors.md` (title without trailing period, message with `'{0}'` placeholders and no trailing period, description as full sentences).
4. **Target class**: the analyzer, generator, or suppressor class that will report it; propose an existing one when the description matches, otherwise propose a new class name.
5. **Documentation**: propose *yes* with the target path, always.
   Use `docs.directory` when it exists, otherwise `docs.suggestedDirectory` (a missing folder is created, not a reason to skip).
   Check `docs.mentionFiles` first: when rules are documented in one shared page or a README table, propose extending that file instead of creating a new layout.
6. **Where the strings live**: read the existing `DiagnosticDescriptor`s of the **target project** — that project alone, since another project's habit is not this one's — and cross what they do with what the request asked for.
   resx is the better form, so the two directions are not symmetric:

   | The project's descriptors | The request asked for | What to do |
   |---------------------------|-----------------------|------------|
   | none | nothing, or resx | resx. |
   | none | literals | Literals. |
   | literals | nothing | Literals. |
   | resx | nothing | resx. |
   | literals | resx | Ask: does this one descriptor go to resx? |
   | resx | literals | Ask, recommending resx. |
   | mixed | nothing, or resx | resx. |
   | mixed | literals | Ask, recommending resx; literals if the user still says literals. |

   Rows that settle by themselves still show their answer in the proposal table below; they only mean the question round spends no slot on the route.
   The consent this step used to collect is not about the route at all but about the file: taking the resx route can mean creating one and registering it in the csproj, and neither the repository nor the request has agreed to that. `references/resources.md`, "Creating a new resx file", decides which file and asks whenever creating or sharing one is not already what the request called for.
   Do not weigh RS1007 against the neighbours or check whether the project enables it.
   Consistency inside a project is worth more than either form on its own, so mixing is never the goal — but an explicit request outranks that, and answering *yes, this one* above leaves the project mixed on purpose. Say nothing about the existing descriptors when it does: converting them is a repository-wide change this skill does not make, and not something to raise here.
   However many rows could ask, here or in that other table, the strings get **one** question at most: its options name the resx file, so the route and the file are settled by the same answer.

Then print the proposal as a short table in the message (name, ID band/category, title, message, description, target class, where the strings go, documentation yes/no) and, directly after it, ask **one** `AskUserQuestion` round.
It is the only round the design goes through: everything Step 1 left open — the prefix when `diagnosticPrefix` is null, and the arrangement each code-fix project reporting `idSharing: none` needs — belongs in it alongside what this step leaves open, so the user answers once and Step 6 then runs without stopping.
Candidates, in priority order when more than four exist:

1. prefix (only when null and the request does not state one; it names every future ID)
2. where the strings live (only when 4.6 or the resx-file table asks; it is also the consent to create or share a resx file)
3. ID sharing (only when a code-fix project reports `none`; it is the consent to edit that project file, and `AnalyzerProject` is the recommendation)
4. severity
5. `suppressedDiagnosticId` (suppressions only)
6. message arguments
7. category
8. documentation

Four questions legitimately fall outside this round, because each depends on something that cannot be known here: an unclear diagnostic-or-suppression classification (Step 2), a partial match against an existing rule (Step 3), a package reference 6d turns out to need, and a URL `doc-url` reports it cannot build — a host it has no template for, or a `{branch}` it cannot resolve, which are different repairs (`references/documentation.md`, "When doc-url cannot build the URL").
Anything else that comes up mid-edit is a sign the draft skipped something rather than a new round to open.

The strings question ranks high because it is a consent, not a preference: without an answer there is no file to write the entries to.
ID sharing follows it for the same reason, and because it is the one candidate whose omission costs a round: pushed past the four-question cap, 6g has to ask it on its own.
Category and message arguments come first among the rest because a drafted guess for them is often wrong.
Documentation is last because the proposal is always *yes* with a concrete path: it counts as offered once it appears in the table, whether or not it also gets a question.
The same holds for anything else pushed out by the four-question cap, since the user can correct any row in an "Other" answer.
Keep question texts short; the table carries the context.
An option's description says what that option means and nothing else: the ID and its band belong to the table, and spelling them out under each option is worse than useless in a repository with no bands yet, where the next unused band is 1 whichever category wins and all four options end up claiming the same number.
Skip the round entirely when the request already settles everything.
Re-draft once from the answers; do not loop.

Apply `customTags` only when required (`CompilationEnd`, `Unnecessary`, `NotConfigurable`; see `references/descriptors.md`), otherwise omit the argument.

### Step 5: Allocate the ID

Run `next-id` with `--ids-file` and `--category <name>` (diagnostic) or `--suppression` (suppression).
This step decides a value and writes nothing; every file edit belongs to Step 6.

When Step 1 reported `diagnosticPrefix` as null, do not try `--category` at all: run `--prefix <PREFIX> --band <n>` straight away, using the prefix answered in Step 4.
The same `--prefix` allocates the first ID of an IDs file that does not exist yet, which 6a then creates.

Read the output before moving on: `references/id-conventions.md`, "What next-id answers with", says which results are normal outcomes rather than failures — a missing prefix, a file that is not there, a category with no band — and what each one leaves 6a to do.


### Step 6: Apply the edits

Perform the edits in this order (6a–6h) so later edits can rely on earlier ones:

- **6a.
  IDs file**: add the `// <Category> (<PREFIX><n>xxx)` band header when Step 5 found none, then insert `public const string {Name} = "{Value}";` inside that block, sorted by number (`references/id-conventions.md`, "Layout of the IDs file").
  No `#region`.
  Create `DiagnosticIds.cs` / `SuppressionIds.cs` when missing (see Step 1 for how to derive them from `examples/`).
- **6b.
  Categories class** (diagnostics only): add the constant when the category is new, to the class named by `diagnosticCategories.path`.
  When that is null, create `DiagnosticCategories.cs` next to the IDs file from `examples/DiagnosticCategories.cs`, matching the IDs class visibility.
- **6c.
  Descriptor**, in this order:
  1. `helpLinkUri`: when documentation was requested, run `doc-url --doc <intended page path>` now — it needs only the path, not the file — and pass the URL as `helpLinkUri`; omit the argument otherwise.
  2. Class: create it from `examples/AnalyzerWithDescriptor.cs` or `examples/SuppressorWithDescriptor.cs` when it does not exist, and leave `Initialize` / `ReportSuppressions` without analysis logic.
  3. Field: add the `private static readonly` field named `{Name}`, shaped like the neighbouring descriptor read in Step 4.
  4. Strings: Step 4 settled the route.
     Literals go straight into the constructor and 6d is skipped; the resx route reaches its entries through whichever form `localizableStringProperties` and `localizableStringHelper` imply, never by widening a `private` helper.
  5. Registration: list the field in `SupportedDiagnostics` / `SupportedSuppressions` — source generators have neither, so the field alone suffices.
  6. Comment: a message with two or more placeholders gets the argument-order comment above the descriptor, so the `Diagnostic.Create` call sites written later pass them in that order.

  `references/descriptors.md` decides all of it and is worth opening here rather than guessing: the neighbouring shape to mirror, that route as a table, the severity spelling this step and 6e must agree on, what an uninitialized `SupportedDiagnostics` gets against what an already-populated one gets, and the conservative forms to write and let 6e shorten.
- **6d.
  resx** (resx route only; the literal route has nothing to do here), in this order:
  1. Target: write to the resource group Step 4 settled on.
     Never write diagnostic strings into a resx that holds none, and never create a resx, on the strength of a reading of the repository alone: the file comes from `references/resources.md`, "Creating a new resx file", either as the option the user answered or as one of the rows that table settles without asking.
  2. New file, only when Step 4 settled on one: write it from the neutral file of an existing group and **register it in the csproj**, with the metadata the repository's own resx files imply (`references/resources.md`, "Creating a new resx file") — `<Generator>ResXFileCodeGenerator</Generator>` where the repository uses that generator, an empty `<Generator></Generator>` where it uses `Microsoft.CodeAnalysis.ResxSourceGenerator`, and the `StronglyTyped*` metadata when it uses neither.
     The first makes the class Visual Studio's to generate, so treat it as `requiresVisualStudioRegeneration` from here on; the second needs that project to already reference the package, which is a package reference to ask about rather than add.
  3. Back up, but only when the group already existed: **copy every culture file of that group to the scratchpad first**.
     They may already carry uncommitted work that git has no record of, so `git checkout --` would throw that away along with this run's write and is not a recovery path.
     A group 6d.2 has just created has nothing to preserve — the copy would be of what this run wrote seconds ago, and restoring it would leave an empty resx behind — so skip the copy there and let 6d.6 undo the creation instead.
  4. Write: run `add-resx-entries` **once per culture file**, each with its own entries JSON in the scratchpad and `--ids-file` pointing at the IDs file edited in 6a.
  5. Language: the neutral file follows `resx[].neutralLanguage` rather than an assumption of English, and source text is never copied into a satellite file, where it looks translated and never gets fixed.
     Which language each file is written in, and the placeholder comments the neutral file carries, are in `references/resources.md`.
  6. Check: read each report, and when `valid` is false stop, after undoing the write: restore the scratchpad copies for a group that already existed, and for one 6d.2 created delete that file and take its `EmbeddedResource` item back out of the csproj.

  Never edit `*.Designer.cs` (`references/resources.md`).
- **6e.
  AnalyzerReleases.Unshipped.md** (diagnostics only): build the analyzer project **before** adding the row, with `dotnet build <csproj> -nodeReuse:false` from inside the repository — re-exporting `DOTNET_CLI_UI_LANGUAGE` and `VSLANG` in that same call, since the preamble of "The helper tool" does not survive from an earlier one — and confirm it reports **RS2000** for the new ID or **RS2008** — the proof that release tracking runs at all, and the reason the build comes first (`references/analyzer-releases.md`, "Proving that tracking runs", also covers what to do when neither appears).
  This build is also where warnings introduced by 6c first appear, IDE0090 and IDE0303 among them: fix them now rather than waiting for Step 7.
  Then append the row `ID | Category | Severity | <short sentence describing the rule>` under `### New Rules`.
  When the pair is missing, create `AnalyzerReleases.Unshipped.md` from `examples/` and `AnalyzerReleases.Shipped.md` with **only its two comment lines**; copying the sample release section verbatim declares rules that do not exist and earns RS2002.
  Neither file needs an `<AdditionalFiles>` item, and creating one is a mistake rather than a precaution: `Microsoft.CodeAnalysis.Analyzers` adds both from its own targets as soon as they exist in the project directory, so a hand-written item duplicates what the package already contributes, and the project file staying untouched is the expected outcome rather than a step that was forgotten.
  Skip this whole edit for suppressions, and skip the build when 6d reported `requiresVisualStudioRegeneration`.
- **6f.
  Documentation** (when requested): create the directory when it does not exist, then the page at the path used in 6c, following the newest existing page or `examples/rule-doc-template.md` when there is none; add the index row in sorted position, creating the index from `examples/rules-index-template.md` when the directory is new (`references/documentation.md`).
  For suppressions, create pages only when `docs.suppressionDocs` or a suppressions table already exists.
- **6g.
  ID sharing**: read `idSharing` on **each** code-fix project in `projects[]`, not the repository-wide value.
  A project whose value is anything but `none` already reaches the IDs, so skip that one; act on each project that says `none`, whatever the others say — a repository where one code fix is wired and another is not reports `mixed` at the top level, and the wired one says nothing about the other.
  For each of them, apply the arrangement Step 4 asked for — `AnalyzerProject` (recommended) or `LinkedFile` — by adding the `<ProjectReference>` or the linked `<Compile>` item to that project and setting the IDs class visibility accordingly.
  One answer covers them all when it is the same for each; ask here only when the four-question cap pushed the question out of that round.
  When `idSharingReliable` is false, `none` only means the detection could not see: say so in the Step 4 question and add nothing without an answer, since the reference may already be there in a project MSBuild failed to evaluate.
  When `diagnosticIdsProject` is null the IDs file belongs to no project, so add the linked `<Compile>` item to each side (`SharedFile`) instead of moving it.
- **6h.
  Config file**: create or update `.claude/roslyn-skills/add-diagnostic.md` (`examples/add-diagnostic.md`, creating the directory when missing) only when a decision was made that detection cannot reproduce next time: a non-GitHub URL template, a docs layout the scan misreads, a descriptor helper worth naming in the notes.
  A new prefix or a new band is **not** such a decision — 6a writes the `// <Category> (<PREFIX><n>xxx)` header, and detection reads both back from it, as Step 7 confirms.
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

### Step 7: Verify

- Re-run `find-conventions --summary` and confirm the new ID appears under `diagnosticIds.ids` / `suppressionIds.ids`, and — on the resx route — that the resx report was `valid`.
- Confirm the RS2000 or RS2008 observation from 6e happened and that the row now silences it.
- Grep the target project for the new name: the IDs file, the descriptor, `SupportedDiagnostics` (or `SupportedSuppressions`), and — on the resx route — every culture file must contain it.
- Build the analyzer project (`dotnet build <csproj> -nodeReuse:false`, with the same two exports) **only when** `requiresVisualStudioRegeneration` is false; when it is true the build is expected to fail with CS0117 until Visual Studio regenerates `Resources.Designer.cs`, so skip the build and say so.
  Fix every warning the new code introduced (compare against a build before the change when unsure); under `EnforceCodeStyleInBuild` with `AnalysisLevel` `latest-all`, typical ones are IDE0090 (use `new(...)` for the descriptor), IDE0303 (use a collection expression) and CS1574 (unresolvable `cref` in a project without Microsoft.CodeAnalysis).

### Step 8: Report

Summarize in a short list: the ID and name, every file changed or created, the `helpLinkUri` (or that it was omitted), and — when a new resx was created whose class is generated at build time — that the class lives in `obj/` and an unbuilt clone shows it as undefined until the first build, and the next steps the skill cannot do: regenerate `Resources.Designer.cs` in Visual Studio when flagged, implement the analysis / suppression logic, add tests, and translate the satellite resx files when the strings went to resx.
When that regeneration is one of them, say that the build 6e and Step 7 both skipped still has to happen afterwards, and that it is what proves the release-tracking row: RS2000 and RS2001 are the two the run never got to observe, so a category or severity that disagrees with the descriptor is still unreported at this point.

## Rules that always hold

- The IDs file contains only constants and band headers; descriptors live in the reporting class.
- The descriptor field, the ID constant, and (on the resx route) the resx stem share one name.
- Every culture file of the resource group gets the same entries; only `.resx` files are edited.
- Where the strings live is decided by the table in Step 4.6, not by the neighbours alone; creating or sharing a resx file is the user's answer, never a reading of the repository.
- Never change the accessibility, signature, or name of an existing member to make new code compile; a `private` helper means the entry point belongs next to it, in the same class.
- Back up to the scratchpad before any edit that may need undoing, and never recover with `git checkout --`, `git restore` or `git stash`: the file may hold work from before this run.
- Check whether the diagnostic already exists before adding anything (Step 3).
- Suppressions: separate IDs file, independent sequence, `Justification` only, no AnalyzerReleases row.
- `helpLinkUri` points at a page that exists once 6f has run, or is omitted; between 6c and 6f it deliberately points at a page not yet written.
- Documentation is always offered, never skipped silently; a missing documentation directory is created, not treated as a decision.
- Ask only for what neither the repository nor the request already settles. Take each item — prefix, category, severity, message arguments, ID sharing — and cross "the repository decides it" with "the request states it": neither, ask; the request only, obey it without spending a question slot; the repository only, follow the repository; both and they agree, say nothing; both and they disagree, ask. Where the strings live follows Step 4.6 instead, which is deliberately asymmetric.
- Do not implement analysis logic, code fixes, or tests; offer them as follow-ups.

## Additional resources

### Reference files

- **`references/tool.md`** — running the helper tool: how the command line has to be spelled, where it must run from, what its JSON promises, an SDK too old for `dotnet tool exec`, and the decision table for a package that will not resolve.
- **`references/id-conventions.md`** — naming rules, prefix and band scheme, IDs file layout, suppression IDs, and how real projects (StyleCop, xunit, Roslynator, Meziantou) share IDs with code-fix projects.
  It also carries what `next-id` answers with.
- **`references/descriptors.md`** — DiagnosticDescriptor / SuppressionDescriptor patterns, literal and resx strings, text format rules (RS1031–RS1033), the severity spellings, `SupportedDiagnostics`, required `customTags`, source generator descriptors.
- **`references/resources.md`** — resx entry naming and ordering, `add-resx-entries` usage, creating and registering a new resx file, Designer.cs regeneration matrix.
- **`references/analyzer-releases.md`** — AnalyzerReleases file format, Notes column convention, RS2000 family, and why 6e builds before adding the row.
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

`dotnet tool exec Aetos.RoslynSkills.Tools@0.1.6 -- add-diagnostic <subcommand>`, see "The helper tool" above.

- **`find-conventions`** — repository convention detection (JSON).
- **`next-id`** — next free ID in a category band or suppression sequence.
- **`add-resx-entries`** — ordered resx insertion with validation.
- **`doc-url`** — documentation URL from the git remote or a template.
