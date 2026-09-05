using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Aetos.RoslynSkills.Tools.Internal;

namespace Aetos.RoslynSkills.Tools.AddDiagnostic;

/// <summary>
/// Detects the conventions a Roslyn analyzer repository already uses and prints them as JSON.
///
/// Output covers: projects (analyzer / codefix / generator / test), diagnostic and suppression ID files,
/// resx groups (with generator detection), AnalyzerReleases files, rule documentation, the optional
/// .claude/roslyn-skills/add-diagnostic.md config, git remote information, and how IDs are shared with the
/// code-fix project.
/// </summary>
internal static partial class FindConventionsCommand
{
    [GeneratedRegex(@"Microsoft\.CodeAnalysis\.\w+\.Testing|^xunit|MSTest|NUnit|TUnit")]
    private static partial Regex TestingPackage { get; }

    [GeneratedRegex("Ids$|Identifiers$", RegexOptions.IgnoreCase)]
    private static partial Regex IdsClassName { get; }

    [GeneratedRegex(@"^(?<base>.+?)(?:Title|Message|Description|Justification)(?<suffix>\w*)$")]
    private static partial Regex LocalizableMemberName { get; }

    // Directories that a repository plausibly uses for documentation, whatever it calls them.
    [GeneratedRegex(@"^(docs?|documentation|wiki|rules|analyzers|diagnostics)$", RegexOptions.IgnoreCase)]
    private static partial Regex DocDirName { get; }

    // A rule row in an AnalyzerReleases file: the ID in the first column. Headings, the table rule, and the
    // template comments all fail to match, so a file carrying only those lists no rule.
    [GeneratedRegex(@"^\s*[A-Za-z]{2,7}\d{3,5}\s*\|", RegexOptions.Multiline)]
    private static partial Regex ReleaseRuleRow { get; }

    /// <summary>How many markdown files mentioning a known ID are reported before the scan gives up.</summary>
    private const int MentionFileLimit = 20;

    public static Command Create()
    {
        var path = new Option<string>("--path")
        {
            Description = "Absolute path to the repository, or any directory inside it. Defaults to the working directory.",
            DefaultValueFactory = _ => ".",
        };
        var summary = new Option<bool>("--summary")
        {
            Description = "Drop the per-project package/project/file lists, which no step reads once the project kinds and idSharing have been computed.",
        };

        var command = new Command("find-conventions", "Detects the diagnostic conventions a repository already uses.");
        command.Options.Add(path);
        command.Options.Add(summary);
        command.SetAction(parse => Run(parse.GetValue(path)!, parse.GetValue(summary)));
        return command;
    }

    private static int Run(string repoPath, bool summary)
    {
        var rootInfo = Repo.GetRoot(repoPath);
        var root = rootInfo.Path;

        // Exclude any Claude Code plugin checked into the repository before scanning: this plugin's own
        // examples/ would otherwise be reported as the repository's diagnostics.
        var scan = new RepoScan(root);

        // Files that could not be read. A scan that silently skips one reports an incomplete repository as a
        // complete one, which is indistinguishable from a repository that really has no diagnostics.
        var readErrors = new List<string>();
        string? ReadOrNull(string file)
        {
            try
            {
                return File.ReadAllText(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                readErrors.Add($"{scan.Rel(file)}: {ex.Message}");
                return null;
            }
        }

        var config = new Config(root);
        if (config.Error is { } configError)
        {
            return Json.Fail(configError, $"Fix the json block in {Config.RelativePath}, or delete the file to fall back to detection.");
        }

        // ---------------------------------------------------------------------------
        // Projects
        // ---------------------------------------------------------------------------
        // A class gets a role only when the role type appears in its base list (not merely somewhere in the file,
        // which would misclassify test drivers that mention IIncrementalGenerator).
        var roleBaseTypes = new Dictionary<string, string[]>
        {
            ["analyzer"] = ["DiagnosticAnalyzer"],
            ["generator"] = ["IIncrementalGenerator", "ISourceGenerator"],
            ["suppressor"] = ["DiagnosticSuppressor"],
            ["codefix"] = ["CodeFixProvider"],
            ["refactoring"] = ["CodeRefactoringProvider"],
        };

        // Project data comes from MSBuild itself rather than from reading the XML: only a real evaluation knows
        // what custom .props/.targets, imported .projitems, central package management and conditions produce.
        var csprojFiles = scan.Files(root, "*.csproj").OrderBy(p => p, StringComparer.Ordinal).ToList();
        var evaluations = MsBuild.EvaluateAll(csprojFiles);

        var projects = new List<ProjectInfo>();
        foreach (var csproj in csprojFiles)
        {
            var dir = Path.GetDirectoryName(csproj)!;
            var ev = evaluations[csproj];

            var packageRefs = new SortedSet<string>(StringComparer.Ordinal);
            var projectRefs = new SortedSet<string>(StringComparer.Ordinal);
            var linked = new SortedSet<string>(StringComparer.Ordinal);
            var resxGenerators = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var i in ev.Items("PackageReference"))
            {
                packageRefs.Add(i.Identity);
            }

            foreach (var i in ev.Items("ProjectReference"))
            {
                projectRefs.Add(i.FullPath is { } fp ? scan.Rel(fp) : i.Identity);
            }

            // A Compile item resolving outside the project directory is a linked file: the analyzer's IDs file
            // pulled into the code-fix project, or a shared file (directly, or through a .shproj's .projitems).
            foreach (var i in ev.Items("Compile"))
            {
                if (i.FullPath is { } fp && !fp.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    linked.Add(scan.Rel(fp));
                }
            }

            foreach (var i in ev.Items("EmbeddedResource"))
            {
                if (i.Metadata.TryGetValue("Generator", out var gen) && !string.IsNullOrWhiteSpace(gen))
                {
                    resxGenerators[Path.GetFileName((i.FullPath ?? i.Identity).Replace('\\', '/'))] = gen.Trim();
                }
            }

            // The neutral resx language, from the three places it can be declared: the NeutralLanguage property,
            // an AssemblyAttribute item carrying NeutralResourcesLanguageAttribute, and — since evaluation cannot
            // see into source — a hand-written [assembly: NeutralResourcesLanguage("...")], found by the .cs scan.
            //
            // All three collapse into obj/<project>.AssemblyInfo.cs, and then into the assembly, either of which
            // would answer this outright. Neither is read: both exist only after a build, and this runs on
            // repositories that have never been built. The generated file is also invisible to evaluation (the
            // target that writes it is the one that adds it to Compile), and locating the assembly means resolving
            // TargetPath, which is empty for the outer build of a multi-targeting project. See
            // references/resources.md, "Which files to edit".
            var neutralLanguage = ev.Property("NeutralLanguage");
            neutralLanguage ??= ev.Items("AssemblyAttribute")
                .Where(i => i.Identity.Contains("NeutralResourcesLanguage", StringComparison.OrdinalIgnoreCase))
                .Select(i => i.Metadata.TryGetValue("_Parameter1", out var p) ? p.Trim() : null)
                .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));

            // Files under the project directory, plus the evaluated Compile items, which add the linked files an
            // AssemblyInfo.cs or a shared analyzer class may live in. Both halves are needed: a multi-targeting
            // project's outer build reports only explicitly written Compile items, not the SDK's default glob,
            // so evaluation alone would miss the project's own sources. Generated files under obj/ stay excluded.
            var sourceFiles = scan.Files(dir, "*.cs")
                .Concat(ev.Items("Compile").Select(i => i.FullPath).OfType<string>()
                    .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && !scan.IsExcluded(f) && File.Exists(f)))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            var roles = new List<string>();
            var classes = new Dictionary<string, List<(string Class, string File)>>();
            foreach (var cs in sourceFiles.OrderBy(f => f, StringComparer.Ordinal))
            {
                if (ReadOrNull(cs) is not { } csText)
                {
                    continue;
                }

                var source = CSharpSource.Parse(csText);
                neutralLanguage ??= source.AssemblyAttributeArgument("NeutralResourcesLanguage");
                foreach (var (className, bases) in source.ClassesWithBaseTypes())
                {
                    foreach (var (role, roleTypes) in roleBaseTypes)
                    {
                        if (!bases.Any(roleTypes.Contains))
                        {
                            continue;
                        }

                        if (!roles.Contains(role))
                        {
                            roles.Add(role);
                        }

                        if (!classes.TryGetValue(role, out var list))
                        {
                            classes[role] = list = [];
                        }

                        var entry = (className, scan.Rel(cs));
                        if (!list.Contains(entry))
                        {
                            list.Add(entry);
                        }
                    }
                }
            }

            // Test projects: the property a test SDK sets, the MSTest SDK marker, or a testing package.
            var isTest = ev.IsTrue("IsTestProject") || ev.IsTrue("UsingMSTestSdk") || packageRefs.Any(TestingPackage.IsMatch);

            var kind = isTest ? "test"
                : roles.Contains("codefix") || roles.Contains("refactoring") ? "codefix"
                : roles.Contains("analyzer") || roles.Contains("suppressor") ? "analyzer"
                : roles.Contains("generator") ? "generator"
                : packageRefs.Any(p => p.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)) ? "roslyn-component"
                : "other";

            projects.Add(new ProjectInfo
            {
                Name = Path.GetFileNameWithoutExtension(csproj),
                Path = scan.Rel(csproj),
                Directory = scan.Rel(dir),
                FullDirectory = dir,
                Kind = kind,
                Roles = roles,
                Classes = classes,
                PackageReferences = packageRefs.ToList(),
                ProjectReferences = projectRefs.ToList(),
                LinkedCompileFiles = linked.ToList(),
                ResxGenerators = resxGenerators,
                UsesResxSourceGenerator = packageRefs.Contains("Microsoft.CodeAnalysis.ResxSourceGenerator"),
                NeutralLanguage = neutralLanguage,
                LangVersion = ev.Property("LangVersion"),
                TargetFrameworks = ev.Property("TargetFrameworks") ?? ev.Property("TargetFramework"),
                EvaluationError = ev.Error,
            });
        }

        // ---------------------------------------------------------------------------
        // ID files
        // ---------------------------------------------------------------------------
        var idFiles = new Dictionary<string, (List<IdConst> Ids, string? ClassName, string Visibility, string Text)>(StringComparer.OrdinalIgnoreCase);
        var categoryFiles = new Dictionary<string, (string? ClassName, string Visibility, string Text)>(StringComparer.OrdinalIgnoreCase);
        foreach (var cs in scan.Files(root, "*.cs"))
        {
            if (ReadOrNull(cs) is not { } text)
            {
                continue;
            }

            var ids = IdConst.Parse(text);
            var (cls, vis) = IdsFileText.ReadClass(text);
            var looksLikeIdsFile = ids.Count > 0 || (cls is not null && IdsClassName.IsMatch(cls));
            if (looksLikeIdsFile)
            {
                idFiles[cs] = (ids, cls, vis, text);
            }

            // Categories class: DiagnosticCategories, Categories, RuleCategories, ...
            if (cls is not null && cls.Contains("Categor", StringComparison.OrdinalIgnoreCase))
            {
                categoryFiles[cs] = (cls, vis, text);
            }
        }

        // Diagnostic prefix: config wins; otherwise inferred from the IDs themselves; otherwise a prefix
        // written in a band header.
        var prefix = config.Get("diagnosticPrefix");
        prefix ??= IdConst.InferPrefix(idFiles.Values.SelectMany(v => v.Ids));
        prefix ??= idFiles.Values.Select(v => IdsFileText.ReadHeaderPrefix(v.Text)).FirstOrDefault(p => p is not null);

        JsonObject? DescribeIdsFile(bool suppression, string? configuredPath)
        {
            string? file = null;
            if (configuredPath is not null && File.Exists(Path.Combine(root, configuredPath)))
            {
                file = Path.GetFullPath(Path.Combine(root, configuredPath));
            }
            else
            {
                Func<IdConst, bool> matches = suppression
                    ? i => prefix is not null ? i.IsSuppressionOf(prefix) : i.Letters.EndsWith('S')
                    : i => prefix is null || i.IsDiagnosticOf(prefix);

                Func<string?, bool> classMatches = suppression
                    ? c => c is not null && c.Contains("Suppress", StringComparison.OrdinalIgnoreCase)
                    : c => c is not null && !c.Contains("Suppress", StringComparison.OrdinalIgnoreCase);

                file = idFiles
                    .Select(kv => (Path: kv.Key, Count: kv.Value.Ids.Count(matches), ClassOk: classMatches(kv.Value.ClassName)))
                    .Where(x => x.Count > 0 || x.ClassOk)
                    .OrderByDescending(x => x.Count).ThenByDescending(x => x.ClassOk).ThenBy(x => x.Path, StringComparer.Ordinal)
                    .Select(x => x.Path)
                    .FirstOrDefault();

                if (file is not null && idFiles[file].Ids.Count == 0 && !classMatches(idFiles[file].ClassName))
                {
                    file = null;
                }
            }
            if (file is null)
            {
                return null;
            }

            if (!idFiles.TryGetValue(file, out var info))
            {
                if (ReadOrNull(file) is not { } text)
                {
                    return null;
                }

                var (cls, vis) = IdsFileText.ReadClass(text);
                info = (IdConst.Parse(text), cls, vis, text);
            }
            var ids = info.Ids.Where(i => prefix is null || (suppression ? i.IsSuppressionOf(prefix) : i.IsDiagnosticOf(prefix))).OrderBy(i => i.Value, StringComparer.Ordinal).ToList();
            var digits = ids.Count > 0 ? ids.GroupBy(i => i.Digits).OrderByDescending(g => g.Count()).First().Key : (int.TryParse(config.Get("idDigits"), out var d) ? d : 4);
            var bands = IdsFileText.ReadBands(info.Text);
            return new JsonObject
            {
                ["path"] = scan.Rel(file),
                ["className"] = info.ClassName,
                ["visibility"] = info.Visibility,
                ["prefix"] = prefix,
                ["digits"] = digits,
                ["categoryBands"] = Json.Array(bands.Select(b => (JsonNode?)new JsonObject { ["category"] = b.Key, ["band"] = b.Value })),
                ["ids"] = Json.Array(ids.Select(i => (JsonNode?)new JsonObject { ["name"] = i.Name, ["value"] = i.Value, ["line"] = i.Line })),
            };
        }

        var diagIds = DescribeIdsFile(false, config.Get("diagnosticIdsFile"));
        var suppIds = DescribeIdsFile(true, config.Get("suppressionIdsFile"));
        if (diagIds is not null && suppIds is not null && diagIds["path"]!.ToString() == suppIds["path"]!.ToString())
        {
            suppIds = null; // same file cannot be both; suppressions are expected in their own file
        }

        // ---------------------------------------------------------------------------
        // Categories class (the constants passed as DiagnosticDescriptor.category)
        // ---------------------------------------------------------------------------
        JsonObject? categoriesInfo = null;
        {
            string? file = null;
            if (config.Get("categoriesFile") is { } cfgCat && File.Exists(Path.Combine(root, cfgCat)))
            {
                file = Path.GetFullPath(Path.Combine(root, cfgCat));
            }
            else if (categoryFiles.Count > 0)
            {
                // Prefer the categories class that sits next to the IDs file; otherwise the one with most constants.
                var idsDir = diagIds is not null ? Path.GetDirectoryName(Path.Combine(root, diagIds["path"]!.ToString())) : null;
                file = categoryFiles.Keys
                    .OrderByDescending(k => idsDir is not null && string.Equals(Path.GetDirectoryName(k), idsDir, StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(k => CSharpSource.Parse(categoryFiles[k].Text).ConstStrings().Count())
                    .ThenBy(k => k, StringComparer.Ordinal)
                    .First();
            }

            if (file is not null)
            {
                // Only a configured path reaches this without having been read already; when it cannot be read,
                // categoriesInfo stays null and the reason is in scanErrors.
                if (!categoryFiles.TryGetValue(file, out var info) && ReadOrNull(file) is { } text)
                {
                    var (cls, vis) = IdsFileText.ReadClass(text);
                    info = (cls, vis, text);
                }

                if (info.Text is not null)
                {
                    var values = new JsonObject();
                    foreach (var constant in CSharpSource.Parse(info.Text).ConstStrings())
                    {
                        values[constant.Name] = constant.Value;
                    }

                    categoriesInfo = new JsonObject
                    {
                        ["path"] = scan.Rel(file),
                        ["className"] = info.ClassName,
                        ["visibility"] = info.Visibility,
                        ["values"] = values,
                    };
                }
            }
        }

        // ---------------------------------------------------------------------------
        // resx groups
        // ---------------------------------------------------------------------------
        // Cached per project: several resx groups in one project would otherwise re-read and re-parse every .cs
        // file in it once per group. The values are cached rather than the JSON, because a JsonNode belongs to
        // one parent and two groups in the same project both need one.
        var localizableByProject = new Dictionary<string, LocalizableConventions>(StringComparer.OrdinalIgnoreCase);
        LocalizableConventions LocalizableOf(ProjectInfo owner)
        {
            if (localizableByProject.TryGetValue(owner.FullDirectory, out var cached))
            {
                return cached;
            }

            LocalizableStringMember? helper = null;
            List<LocalizableStringMember>? members = null;
            string? helperFile = null, membersFile = null;
            foreach (var cs in scan.Files(owner.FullDirectory, "*.cs"))
            {
                if (helper is not null && members is not null)
                {
                    break;
                }

                if (ReadOrNull(cs) is not { } text)
                {
                    continue;
                }

                var source = CSharpSource.Parse(text);
                if (helper is null && source.LocalizableStringHelper() is { } h)
                {
                    (helper, helperFile) = (h, scan.Rel(cs));
                }

                if (members is null && source.LocalizableStringMembers() is { Count: > 0 } m)
                {
                    (members, membersFile) = (m, scan.Rel(cs));
                }
            }

            var result = new LocalizableConventions(helperFile, helper, membersFile, members);
            localizableByProject[owner.FullDirectory] = result;
            return result;
        }

        var resxGroups = new JsonArray();
        foreach (var g in scan.Files(root, "*.resx")
                     .GroupBy(f => (Dir: Path.GetDirectoryName(f)!, Base: ResxName.Split(f).Base))
                     .OrderBy(g => g.Key.Dir + g.Key.Base, StringComparer.Ordinal))
        {
            var dir = g.Key.Dir;
            var baseFile = Path.Combine(dir, g.Key.Base + ".resx");
            var designer = Path.Combine(dir, g.Key.Base + ".Designer.cs");
            var owner = projects
                .Where(p => dir.Replace('\\', '/').StartsWith(p.FullDirectory.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => p.FullDirectory.Length)
                .FirstOrDefault();

            string? generator = null;
            if (owner is not null)
            {
                if (owner.UsesResxSourceGenerator)
                {
                    generator = "Microsoft.CodeAnalysis.ResxSourceGenerator";
                }
                else if (owner.ResxGenerators.TryGetValue(g.Key.Base + ".resx", out var gen))
                {
                    generator = gen;
                }
            }
            if (generator is null && File.Exists(designer))
            {
                generator = "ResXFileCodeGenerator (inferred from Designer.cs)";
            }

            var resourceClass = g.Key.Base;
            if (File.Exists(designer) && ReadOrNull(designer) is { } designerText)
            {
                resourceClass = CSharpSource.Parse(designerText).FirstClassName() ?? resourceClass;
            }

            // A hand-written helper such as `static LocalizableResourceString GetLocalizableResourceString(string name)`
            // (usually in a partial of the resource class). Its accessibility matters: a private helper means the
            // intended entry points are LocalizableResourceString properties inside the same class.
            JsonObject? helper = null;
            JsonObject? properties = null;
            if (owner is not null)
            {
                var localizable = LocalizableOf(owner);
                if (localizable is { Helper: { } h, HelperFile: { } hf })
                {
                    helper = new JsonObject
                    {
                        ["class"] = string.Join('.', h.ContainingClasses),
                        ["method"] = h.Name,
                        ["accessibility"] = h.Accessibility,
                        ["file"] = hf,
                    };
                }

                if (localizable is { Properties: { Count: > 0 } members, PropertiesFile: { } pf })
                {
                    var first = members[0];
                    var nested = first.ContainingClasses.Count >= 2 ? first.ContainingClasses[^1] : null;
                    var sm = LocalizableMemberName.Match(first.Name);
                    var suffix = sm.Success ? sm.Groups["suffix"].Value : "";
                    properties = new JsonObject
                    {
                        ["file"] = pf,
                        ["class"] = string.Join('.', first.ContainingClasses),
                        ["style"] = nested is not null ? "nested" : suffix.Length > 0 ? "suffix" : "unknown",
                        ["nestedClass"] = nested,
                        ["suffix"] = suffix,
                        ["accessibility"] = first.Accessibility,
                        ["names"] = Json.Array(members.Select(m => m.Name)),
                    };
                }
            }
            var files = g.OrderBy(f => f, StringComparer.Ordinal).ToList();
            resxGroups.Add(new JsonObject
            {
                ["baseName"] = g.Key.Base,
                ["directory"] = scan.Rel(dir),
                ["project"] = owner?.Name,
                ["files"] = Json.Array(files.Select(scan.Rel)),
                ["cultures"] = Json.Array(files.Select(f => ResxName.Split(f).Culture).OrderBy(c => c, StringComparer.Ordinal)),
                ["baseFileExists"] = File.Exists(baseFile),
                ["designerFile"] = File.Exists(designer) ? scan.Rel(designer) : null,
                ["generator"] = generator,
                ["resourceClass"] = resourceClass,
                // Language of the neutral file, from <NeutralLanguage> or NeutralResourcesLanguageAttribute.
                // Null means the project never declared one; the existing entries are then the only evidence.
                ["neutralLanguage"] = owner?.NeutralLanguage,
                ["localizableStringHelper"] = helper,
                ["localizableStringProperties"] = properties,
                ["requiresVisualStudioRegeneration"] = File.Exists(designer) && generator is not null && generator.Contains("ResXFileCodeGenerator"),
            });
        }

        // ---------------------------------------------------------------------------
        // AnalyzerReleases
        // ---------------------------------------------------------------------------
        var releases = new JsonArray();
        foreach (var p in projects)
        {
            var shipped = Path.Combine(p.FullDirectory, "AnalyzerReleases.Shipped.md");
            var unshipped = Path.Combine(p.FullDirectory, "AnalyzerReleases.Unshipped.md");
            if (File.Exists(shipped) || File.Exists(unshipped) || p.Kind is "analyzer" or "generator")
            {
                releases.Add(new JsonObject
                {
                    ["project"] = p.Name,
                    ["shipped"] = File.Exists(shipped) ? scan.Rel(shipped) : null,
                    ["unshipped"] = File.Exists(unshipped) ? scan.Rel(unshipped) : null,
                    ["expectedDirectory"] = p.Directory,
                    // Whether the release-tracking analyzer (RS2000-RS2008) is reachable at all. Both values are
                    // weak: the package flows transitively from Microsoft.CodeAnalysis.*, and the SDK registers the
                    // AnalyzerReleases files as AdditionalFiles implicitly, so neither the package list nor the
                    // project file proves anything. Only the RS2000 observation in SKILL.md 6e does.
                    ["analyzersPackage"] = p.PackageReferences.Contains("Microsoft.CodeAnalysis.Analyzers") ? "direct"
                        : p.PackageReferences.Any(r => r.StartsWith("Microsoft.CodeAnalysis.", StringComparison.Ordinal)) ? "viaCodeAnalysis"
                        : "none",
                });
            }
        }

        // ---------------------------------------------------------------------------
        // Documentation
        // ---------------------------------------------------------------------------
        // A rule page is named after the ID, optionally followed by a separator and a slug
        // (CTS1001.md, CTS1001-disposable-field.md).
        // With a known prefix, casing in file names does not matter. Without one, require uppercase letters so
        // that ordinary files (ISO8601-notes.md and the like) are not mistaken for rule pages.
        var letters = prefix is not null ? Regex.Escape(prefix) : "[A-Z]{2,7}";
        var docCase = prefix is not null ? RegexOptions.IgnoreCase : RegexOptions.None;
        var ruleDocRegex = new Regex($@"^{letters}\d{{3,5}}([-_. ].*)?\.md$", docCase);
        var suppDocRegex = new Regex($@"^{letters}S\d{{3,5}}([-_. ].*)?\.md$", docCase);

        var allMarkdown = scan.Files(root, "*.md").OrderBy(f => f, StringComparer.Ordinal).ToList();

        string? docsDir = null;
        if (config.Get("docsDir") is { } cfgDocs && Directory.Exists(Path.Combine(root, cfgDocs)))
        {
            docsDir = Path.GetFullPath(Path.Combine(root, cfgDocs));
        }
        else
        {
            docsDir = allMarkdown
                .Where(f => ruleDocRegex.IsMatch(Path.GetFileName(f)) || suppDocRegex.IsMatch(Path.GetFileName(f)))
                .GroupBy(f => Path.GetDirectoryName(f)!)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => g.Key).FirstOrDefault();
        }

        // Each candidate carries how many Markdown files it holds, counting subdirectories: a directory left
        // behind empty by an interrupted run is otherwise indistinguishable from one full of pages, and the
        // difference decides whether Step 3 has a leftover to report.
        var candidateDirs = scan.Directories(root)
            .Where(d => !d.Equals(root, StringComparison.OrdinalIgnoreCase) && DocDirName.IsMatch(Path.GetFileName(d)))
            .OrderBy(d => d.Count(c => c is '/' or '\\')).ThenBy(d => d, StringComparer.Ordinal)
            .Select(d => (
                Path: scan.Rel(d),
                // Release tracking is not documentation, and counting it would make the analyzer's own directory
                // look like a documented one.
                MarkdownFiles: scan.Files(d, "*.md")
                    .Count(f => !Path.GetFileName(f).StartsWith("AnalyzerReleases.", StringComparison.OrdinalIgnoreCase)),
                // Whether the directory holds any file at all, of any kind. A directory named Analyzers full of
                // .cs files is somebody's source, not a documentation directory an interrupted run left behind.
                Files: scan.Files(d, "*").Count()))
            .ToList();

        // Markdown that mentions an existing diagnostic ID (a single page listing every rule, a README table).
        var knownIds = new[] { diagIds, suppIds }
            .Where(o => o is not null)
            .SelectMany(o => o!["ids"]!.AsArray().Select(i => i!["value"]!.ToString()))
            .Distinct(StringComparer.Ordinal).ToList();
        var mentionFiles = new List<string>();
        var mentionFilesTruncated = false;
        if (knownIds.Count > 0)
        {
            var mentionRegex = new Regex(@"(^|[\s|(\[#`])(" + string.Join('|', knownIds.Select(Regex.Escape)) + @")([\s|)\].,:`]|$)", RegexOptions.Multiline);
            foreach (var f in allMarkdown)
            {
                if (docsDir is not null && Path.GetDirectoryName(f) == docsDir)
                {
                    continue;
                }

                // Release tracking lists every ID by design; it is not documentation.
                if (Path.GetFileName(f).StartsWith("AnalyzerReleases.", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (Length(f) > 512 * 1024)
                {
                    continue;
                }

                if (ReadOrNull(f) is { } md && mentionRegex.IsMatch(md))
                {
                    mentionFiles.Add(scan.Rel(f));
                }

                if (mentionFiles.Count >= MentionFileLimit)
                {
                    // Reported: the remaining files were never looked at, so the list is a sample, not the set.
                    mentionFilesTruncated = true;
                    break;
                }
            }
        }

        long Length(string file)
        {
            try
            {
                return new FileInfo(file).Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                readErrors.Add($"{scan.Rel(file)}: {ex.Message}");
                return long.MaxValue;
            }
        }

        // Where a new page should go when nothing exists yet: under the shallowest documentation-ish directory,
        // else the conventional docs/rules.
        var suggested = docsDir is not null
            ? scan.Rel(docsDir)
            : candidateDirs.Count > 0
                ? (Path.GetFileName(candidateDirs[0].Path).Equals("rules", StringComparison.OrdinalIgnoreCase)
                    ? candidateDirs[0].Path
                    : candidateDirs[0].Path + "/rules")
                : "docs/rules";

        var docs = new JsonObject
        {
            ["directory"] = null,
            ["indexFile"] = null,
            ["ruleDocs"] = new JsonArray(),
            ["suppressionDocs"] = new JsonArray(),
            ["candidateDirectories"] = Json.Array(candidateDirs.Select(d => (JsonNode?)new JsonObject
            {
                ["path"] = d.Path,
                ["markdownFiles"] = d.MarkdownFiles,
                ["files"] = d.Files,
            })),
            ["mentionFiles"] = Json.Array(mentionFiles),
            ["mentionFilesTruncated"] = mentionFilesTruncated,
            ["suggestedDirectory"] = suggested,
        };

        if (docsDir is not null)
        {
            docs["directory"] = scan.Rel(docsDir);
            foreach (var idx in new[] { config.Get("docsIndexFile"), "README.md", "index.md", "Index.md" })
            {
                if (idx is not null && File.Exists(Path.Combine(docsDir, idx)))
                {
                    docs["indexFile"] = scan.Rel(Path.Combine(docsDir, idx));
                    break;
                }
            }

            var mds = scan.FilesIn(docsDir, "*.md").OrderBy(f => f, StringComparer.Ordinal).ToList();
            docs["ruleDocs"] = Json.Array(mds.Where(f => ruleDocRegex.IsMatch(Path.GetFileName(f))).Select(scan.Rel));
            docs["suppressionDocs"] = Json.Array(mds.Where(f => suppDocRegex.IsMatch(Path.GetFileName(f))).Select(scan.Rel));
        }

        // ---------------------------------------------------------------------------
        // Leftovers
        // ---------------------------------------------------------------------------
        // Traces of an interrupted earlier run: something that exists but holds nothing. None of them collides
        // with an ID, so without this list Step 3 can only find them by listing directories by hand — and a
        // leftover read as a convention makes the repository look like it documents nothing on purpose.
        var leftovers = new JsonArray();
        void Leftover(string kind, string? path, string detail)
        {
            leftovers.Add(new JsonObject { ["kind"] = kind, ["path"] = path, ["detail"] = detail });
        }

        foreach (var d in candidateDirs.Where(d => d.Files == 0))
        {
            Leftover("emptyDocumentationDirectory", d.Path, "the directory exists and holds no file at all");
        }

        if (categoriesInfo is { } cats && cats["values"] is JsonObject { Count: 0 })
        {
            Leftover("categoriesClassWithoutConstants", cats["path"]!.ToString(), "the class exists and declares no category constant");
        }

        foreach (var release in releases.OfType<JsonObject>())
        {
            if (release["unshipped"]?.ToString() is { } unshippedPath &&
                ReadOrNull(Path.Combine(root, unshippedPath)) is { } unshippedText &&
                ListsNoRule(unshippedText))
            {
                Leftover("analyzerReleasesWithoutRules", unshippedPath, "the file exists and lists no rule under any heading");
            }
        }

        // ---------------------------------------------------------------------------
        // Git
        // ---------------------------------------------------------------------------
        var git = GitInfo.Read(root);
        var gitJson = new JsonObject
        {
            ["remote"] = git.Remote,
            ["host"] = git.Repository?.Host,
            ["owner"] = git.Repository?.Owner,
            ["repo"] = git.Repository?.Name,
            ["defaultBranch"] = git.DefaultBranch,
            ["docUrlTemplate"] = config.Get("docUrlTemplate") ?? git.DefaultTemplate,
        };

        // ---------------------------------------------------------------------------
        // ID sharing
        // ---------------------------------------------------------------------------
        ProjectInfo? idsProject = null;
        if (diagIds is not null)
        {
            var idsDir = Path.GetDirectoryName(Path.Combine(root, diagIds["path"]!.ToString()))!.Replace('\\', '/');
            idsProject = projects
                .Where(p => idsDir.StartsWith(p.FullDirectory.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => p.FullDirectory.Length).FirstOrDefault();
        }

        var configuredSharing = config.Get("idSharing");
        var sharingByProject = DetectIdSharing(
            projects, idsProject, diagIds is not null ? Path.GetFileName(diagIds["path"]!.ToString()) : null);
        var sharing = configuredSharing ?? RollUpIdSharing(sharingByProject);

        // ---------------------------------------------------------------------------
        // Output
        // ---------------------------------------------------------------------------
        // --summary keeps only what the workflow reads: the projects a diagnostic can be added to, without the
        // reference lists that only fed kind detection and idSharing (both already computed above). A project
        // MSBuild could not evaluate is kept whatever its kind, since "other" is then only a guess.
        var reported = summary
            ? projects.Where(p => p.Kind is "analyzer" or "generator" or "codefix" or "roslyn-component"
                || p.EvaluationError is not null).ToList()
            : projects;
        var reportedNames = reported.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (summary)
        {
            foreach (var g in resxGroups.OfType<JsonObject>().ToList())
            {
                if (g["project"] is null || !reportedNames.Contains(g["project"]!.ToString()))
                {
                    resxGroups.Remove(g);
                }
            }
        }
        var projectsJson = Json.Array(reported.Select(p => (JsonNode?)p.ToJson()));

        // Each code-fix project carries its own route to the IDs: the repository-wide value below cannot say
        // which of several code fixes is the one still missing a reference. A configured value overrides them
        // all, since it is the user stating the arrangement rather than the scan guessing at it.
        foreach (var (p, json) in reported.Zip(projectsJson.OfType<JsonObject>()))
        {
            if (sharingByProject.TryGetValue(p.Path, out var route))
            {
                json["idSharing"] = configuredSharing ?? route;
            }
        }

        if (summary)
        {
            foreach (var p in projectsJson.OfType<JsonObject>())
            {
                foreach (var key in new[] { "packageReferences", "projectReferences", "linkedCompileFiles", "resxGenerators" })
                {
                    p.Remove(key);
                }
            }
        }

        Json.Print(new JsonObject
        {
            ["root"] = root.Replace('\\', '/'),
            // False when nothing identified a repository and --path was used as the root: every path below is
            // relative to it, so a subdirectory standing in for the root looks like a nearly empty repository.
            ["rootDetected"] = rootInfo.Detected,
            ["rootError"] = rootInfo.Error,
            // Directories and files the scan could not read. Anything missing below may be missing for this reason.
            ["scanErrors"] = Json.Array(scan.Errors.Concat(readErrors)),
            // Plugin trees skipped during the scan; their sample files are documentation, not this repository's code.
            ["excludedPluginDirectories"] = Json.Array(scan.VendoredPlugins.Select(scan.Rel)),
            ["config"] = new JsonObject { ["path"] = Config.RelativePath, ["exists"] = config.Exists, ["values"] = config.ToJson(), ["notes"] = config.Body },
            ["diagnosticPrefix"] = prefix,
            ["projects"] = projectsJson,
            ["diagnosticIds"] = diagIds,
            ["suppressionIds"] = suppIds,
            ["diagnosticCategories"] = categoriesInfo,
            // The repository-wide roll-up: one route when every code-fix project takes the same one, "mixed" when
            // they differ. Per-project values are on the projects themselves, and are what an edit acts on.
            ["idSharing"] = sharing,
            // False when idSharing was detected from project data that MSBuild could not fully evaluate: the
            // references and linked files detection reads come from evaluation, so "none" may only mean "unknown".
            ["idSharingReliable"] = configuredSharing is not null || projects.All(p => p.EvaluationError is null),
            ["diagnosticIdsProject"] = idsProject?.Name,
            ["resx"] = resxGroups,
            ["analyzerReleases"] = releases,
            ["docs"] = docs,
            // Half-finished artifacts of an earlier run; stale or deliberate is the user's call, not the scan's.
            ["leftovers"] = leftovers,
            ["git"] = gitJson,
        });
        return 0;
    }

    /// <summary>
    /// Whether an AnalyzerReleases file declares no rule at all: what is left when a run that created the file
    /// stopped before adding its row. The template comments, the section headings, and the table header and rule
    /// are all present in such a file, so its length says nothing.
    /// </summary>
    internal static bool ListsNoRule(string releaseFileText)
    {
        return !ReleaseRuleRow.IsMatch(releaseFileText);
    }

    /// <summary>
    /// How each code-fix project reaches the diagnostic IDs, keyed by the project's repository-relative path.
    /// The answer is per project on purpose: a repository whose code fixes were added at different times often
    /// has one that reaches the IDs and one that does not, and a single repository-wide value would report the
    /// second as already arranged (see <see cref="RollUpIdSharing"/>).
    ///
    /// The values name *where the IDs live* and how a consumer reaches them, deliberately avoiding MSBuild item
    /// names since three of the four can be built out of &lt;ProjectReference&gt; items:
    ///
    /// <code>
    ///                      | IDs in the analyzer project | IDs outside it
    ///   -------------------+-----------------------------+---------------------------------
    ///   reached by a       | AnalyzerProject             | SharedProject (a third project
    ///   project reference  |                             | both sides reference)
    ///   reached by a       | LinkedFile                  | SharedFile (a file owned by no
    ///   linked &lt;Compile&gt;   |                             | project, compiled by each side;
    ///                      |                             | a VS .shproj lands here, since it
    ///                      |                             | produces no assembly of its own)
    /// </code>
    ///
    /// "none" means no route was found, which for a project that evaluated means there is none.
    /// </summary>
    internal static Dictionary<string, string> DetectIdSharing(
        IReadOnlyList<ProjectInfo> projects,
        ProjectInfo? idsProject,
        string? idsFileName)
    {
        var byPath = projects
            .GroupBy(p => p.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var producers = projects.Where(p => p.Kind is "analyzer" or "generator").ToList();

        // Project references are transitive for compilation, so a code fix that references the analyzer sees the
        // IDs project the analyzer references. Reachability, not the direct list, is what decides whether the
        // constants are visible.
        bool Reaches(ProjectInfo from, ProjectInfo target)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { from.Path };
            var queue = new Queue<ProjectInfo>([from]);
            while (queue.Count > 0)
            {
                foreach (var reference in queue.Dequeue().ProjectReferences)
                {
                    if (string.Equals(reference, target.Path, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if (seen.Add(reference) && byPath.TryGetValue(reference, out var next))
                    {
                        queue.Enqueue(next);
                    }
                }
            }

            return false;
        }

        bool LinksIdsFile(ProjectInfo p)
        {
            return idsFileName is not null &&
                   p.LinkedCompileFiles.Any(l => l.EndsWith(idsFileName, StringComparison.OrdinalIgnoreCase));
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cf in projects.Where(p => p.Kind == "codefix"))
        {
            result[cf.Path] =
                // A linked file is checked first: a code fix that compiles the IDs file itself reaches the
                // constants whether or not it also references the project they nominally belong to.
                LinksIdsFile(cf) ? (idsProject is null ? "SharedFile" : "LinkedFile")
                : idsProject is null ? "none"
                : !Reaches(cf, idsProject) ? "none"
                : idsProject.Kind is "analyzer" or "generator" ? "AnalyzerProject"
                // A third project is only "shared" when a producer reaches it too; otherwise the IDs sit
                // somewhere only the code fix can see, which is not an arrangement to copy.
                : producers.Any(p => Reaches(p, idsProject)) ? "SharedProject"
                : "none";
        }

        return result;
    }

    /// <summary>
    /// The repository-wide answer: the one route when every code-fix project takes the same one, "mixed" when
    /// they differ, and "none" when there is no code-fix project at all. "mixed" is reported rather than smoothed
    /// over because it is exactly the case a single value used to hide — the skill has to look at each project.
    /// </summary>
    internal static string RollUpIdSharing(IReadOnlyDictionary<string, string> byProject)
    {
        var values = byProject.Values.Distinct(StringComparer.Ordinal).ToList();
        return values.Count switch
        {
            0 => "none",
            1 => values[0],
            _ => "mixed",
        };
    }

    /// <summary>
    /// A project's localizable-resource conventions, as read from its source once. The file paths are
    /// repository-relative, and are non-null exactly when the value beside them is.
    /// </summary>
    private sealed record LocalizableConventions(
        string? HelperFile,
        LocalizableStringMember? Helper,
        string? PropertiesFile,
        List<LocalizableStringMember>? Properties);
}
