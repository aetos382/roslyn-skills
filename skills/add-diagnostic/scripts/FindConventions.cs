#!/usr/bin/env dotnet
#:property PublishAot=false
#:include Common.cs
// FindConventions.cs — detects the conventions a Roslyn analyzer repository already uses and prints them as JSON.
//
// Usage:  dotnet FindConventions.cs -- [--path <repo-or-subdir>] [--summary]
//
// --summary drops the per-project package/project/file lists, which no step reads once the project kinds
// and idSharing have been computed. Use it unless a raw reference list is needed.
//
// Output covers: projects (analyzer / codefix / generator / test), diagnostic and suppression ID files,
// resx groups (with generator detection), AnalyzerReleases files, rule documentation, the optional
// .claude/roslyn-skills.md config, git remote information, and how IDs are shared with the code-fix project.

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;

var cli = new CliArgs(args, "summary");
var root = Repo.GetRoot(cli.Get("path") ?? ".");
var config = new Config(root);

// ---------------------------------------------------------------------------
// Projects
// ---------------------------------------------------------------------------
// A class gets a role only when the role type appears in its base list (not merely somewhere in the file,
// which would misclassify test drivers that mention IIncrementalGenerator).
var roleBaseTypes = new Dictionary<string, Regex>
{
    ["analyzer"] = new(@"\bDiagnosticAnalyzer\b"),
    ["generator"] = new(@"\bI(?:IncrementalGenerator|SourceGenerator)\b"),
    ["suppressor"] = new(@"\bDiagnosticSuppressor\b"),
    ["codefix"] = new(@"\bCodeFixProvider\b"),
    ["refactoring"] = new(@"\bCodeRefactoringProvider\b"),
};
var classWithBases = new Regex(@"\bclass\s+(?<name>\w+)\s*(?:<[^>]*>)?\s*:\s*(?<bases>[^{;]*)\{", RegexOptions.Singleline);

var projects = new List<ProjectInfo>();
foreach (var csproj in Repo.Files(root, "*.csproj").OrderBy(p => p, StringComparer.Ordinal))
{
    var dir = Path.GetDirectoryName(csproj)!;
    var buildFiles = new List<string> { csproj };
    for (var probe = dir; probe is not null && probe.Length >= root.Length; probe = Path.GetDirectoryName(probe))
    {
        foreach (var n in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" })
        {
            var f = Path.Combine(probe, n);
            if (File.Exists(f)) buildFiles.Add(f);
        }
        if (string.Equals(probe, root, StringComparison.OrdinalIgnoreCase)) break;
    }

    var packageRefs = new SortedSet<string>(StringComparer.Ordinal);
    var projectRefs = new SortedSet<string>(StringComparer.Ordinal);
    var linked = new SortedSet<string>(StringComparer.Ordinal);
    var resxGenerators = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var bf in buildFiles)
    {
        XmlDocument bx = new();
        try { bx.Load(bf); } catch { continue; }
        var bfDir = Path.GetDirectoryName(bf)!;
        string Expand(string v) => v.Replace("$(MSBuildThisFileDirectory)", bfDir + "/").Replace('\\', '/');
        // GlobalPackageReference (Central Package Management) applies to every project, so treat it like PackageReference.
        foreach (XmlAttribute a in bx.SelectNodes("//PackageReference/@Include | //GlobalPackageReference/@Include")!) packageRefs.Add(a.Value);
        foreach (XmlAttribute a in bx.SelectNodes("//ProjectReference/@Include")!)
        {
            var p = Expand(a.Value);
            var full = Path.IsPathRooted(p) ? p : Path.Combine(bfDir, p);
            projectRefs.Add(File.Exists(full) ? Repo.Rel(root, Path.GetFullPath(full)) : p);
        }
        foreach (XmlAttribute a in bx.SelectNodes("//Compile/@Include")!)
        {
            var p = Expand(a.Value);
            if (p.Contains("../") || p.Contains('/')) linked.Add(p);
        }
        foreach (XmlElement er in bx.SelectNodes("//EmbeddedResource")!)
        {
            var name = er.GetAttribute("Update");
            if (name.Length == 0) name = er.GetAttribute("Include");
            var gen = er.SelectSingleNode("Generator")?.InnerText ?? (er.HasAttribute("Generator") ? er.GetAttribute("Generator") : null);
            if (name.Length > 0 && !string.IsNullOrWhiteSpace(gen)) resxGenerators[Path.GetFileName(name)] = gen.Trim();
        }
    }

    var roles = new List<string>();
    var classes = new Dictionary<string, List<(string Class, string File)>>();
    foreach (var cs in Repo.Files(dir, "*.cs"))
    {
        var text = File.ReadAllText(cs);
        foreach (Match cm in classWithBases.Matches(text))
        {
            var bases = cm.Groups["bases"].Value;
            foreach (var (role, rx) in roleBaseTypes)
            {
                if (!rx.IsMatch(bases)) continue;
                if (!roles.Contains(role)) roles.Add(role);
                if (!classes.TryGetValue(role, out var list)) classes[role] = list = new();
                var entry = (cm.Groups["name"].Value, Repo.Rel(root, cs));
                if (!list.Contains(entry)) list.Add(entry);
            }
        }
    }

    // Test projects: testing packages, a test SDK (MSTest.Sdk), or an explicit IsTestProject property.
    var sdkAttr = "";
    var isTestProjectProp = false;
    try
    {
        var px = new XmlDocument();
        px.Load(csproj);
        sdkAttr = px.DocumentElement?.GetAttribute("Sdk") ?? "";
        isTestProjectProp = string.Equals(px.SelectSingleNode("//IsTestProject")?.InnerText.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }
    catch { }
    var isTest = isTestProjectProp
        || sdkAttr.Contains("MSTest.Sdk", StringComparison.OrdinalIgnoreCase)
        || packageRefs.Any(p => Regex.IsMatch(p, @"Microsoft\.CodeAnalysis\.\w+\.Testing|^xunit|MSTest|NUnit|TUnit"));
    var kind = isTest ? "test"
        : roles.Contains("codefix") || roles.Contains("refactoring") ? "codefix"
        : roles.Contains("analyzer") || roles.Contains("suppressor") ? "analyzer"
        : roles.Contains("generator") ? "generator"
        : packageRefs.Any(p => p.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)) ? "roslyn-component"
        : "other";

    projects.Add(new ProjectInfo(Path.GetFileNameWithoutExtension(csproj), Repo.Rel(root, csproj), Repo.Rel(root, dir), dir, kind, roles, classes,
        packageRefs.ToList(), projectRefs.ToList(), linked.ToList(), resxGenerators,
        packageRefs.Contains("Microsoft.CodeAnalysis.ResxSourceGenerator")));
}

// ---------------------------------------------------------------------------
// ID files
// ---------------------------------------------------------------------------
var idFiles = new Dictionary<string, (List<IdConst> Ids, string? ClassName, string Visibility, string Text)>(StringComparer.OrdinalIgnoreCase);
var categoryFiles = new Dictionary<string, (string ClassName, string Visibility, string Text)>(StringComparer.OrdinalIgnoreCase);
var stringConstRegex = new Regex(@"(?m)^\s*(?:public|internal|private)?\s*const\s+string\s+(?<name>\w+)\s*=\s*""(?<value>[^""]*)""\s*;");
foreach (var cs in Repo.Files(root, "*.cs"))
{
    var text = File.ReadAllText(cs);
    var ids = IdConst.Parse(text);
    var (cls, vis) = IdsFileText.ReadClass(text);
    var looksLikeIdsFile = ids.Count > 0 || (cls is not null && Regex.IsMatch(cls, "Ids$|Identifiers$", RegexOptions.IgnoreCase));
    if (looksLikeIdsFile) idFiles[cs] = (ids, cls, vis, text);
    // Categories class: DiagnosticCategories, Categories, RuleCategories, ...
    if (cls is not null && cls.Contains("Categor", StringComparison.OrdinalIgnoreCase)) categoryFiles[cs] = (cls, vis, text);
}

// Diagnostic prefix: config wins; otherwise the most common letter group, ignoring groups that are
// another group + 'S' (those are suppressions); otherwise a prefix written in a band header.
var prefix = config.Get("diagnosticPrefix");
if (prefix is null)
{
    var groups = idFiles.Values.SelectMany(v => v.Ids).GroupBy(i => i.Letters).ToDictionary(g => g.Key, g => g.Count());
    prefix = groups.Keys
        .Where(k => !(k.EndsWith('S') && groups.ContainsKey(k[..^1])))
        .OrderByDescending(k => groups[k]).ThenBy(k => k, StringComparer.Ordinal)
        .FirstOrDefault();
}
prefix ??= idFiles.Values.Select(v => IdsFileText.ReadHeaderPrefix(v.Text)).FirstOrDefault(p => p is not null);

JsonObject? DescribeIdsFile(bool suppression, string? configuredPath)
{
    string? file = null;
    if (configuredPath is not null && File.Exists(Path.Combine(root, configuredPath)))
        file = Path.GetFullPath(Path.Combine(root, configuredPath));
    else
    {
        Func<IdConst, bool> matches = suppression
            ? i => prefix is not null ? i.IsSuppressionOf(prefix) : i.Letters.EndsWith('S')
            : i => prefix is not null ? i.IsDiagnosticOf(prefix) : true;
        Func<string?, bool> classMatches = suppression
            ? c => c is not null && c.Contains("Suppress", StringComparison.OrdinalIgnoreCase)
            : c => c is not null && !c.Contains("Suppress", StringComparison.OrdinalIgnoreCase);
        file = idFiles
            .Select(kv => (Path: kv.Key, Count: kv.Value.Ids.Count(matches), ClassOk: classMatches(kv.Value.ClassName)))
            .Where(x => x.Count > 0 || x.ClassOk)
            .OrderByDescending(x => x.Count).ThenByDescending(x => x.ClassOk).ThenBy(x => x.Path, StringComparer.Ordinal)
            .Select(x => x.Path)
            .FirstOrDefault();
        if (file is not null && idFiles[file].Ids.Count == 0 && !classMatches(idFiles[file].ClassName)) file = null;
    }
    if (file is null) return null;
    if (!idFiles.TryGetValue(file, out var info))
    {
        var text = File.ReadAllText(file);
        var (cls, vis) = IdsFileText.ReadClass(text);
        info = (IdConst.Parse(text), cls, vis, text);
    }
    var ids = info.Ids.Where(i => prefix is null || (suppression ? i.IsSuppressionOf(prefix) : i.IsDiagnosticOf(prefix))).OrderBy(i => i.Value, StringComparer.Ordinal).ToList();
    var digits = ids.Count > 0 ? ids.GroupBy(i => i.Digits).OrderByDescending(g => g.Count()).First().Key : (int.TryParse(config.Get("idDigits"), out var d) ? d : 4);
    var bands = IdsFileText.ReadBands(info.Text);
    return new JsonObject
    {
        ["path"] = Repo.Rel(root, file),
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
    suppIds = null; // same file cannot be both; suppressions are expected in their own file

// ---------------------------------------------------------------------------
// Categories class (the constants passed as DiagnosticDescriptor.category)
// ---------------------------------------------------------------------------
JsonObject? categoriesInfo = null;
{
    string? file = null;
    if (config.Get("categoriesFile") is { } cfgCat && File.Exists(Path.Combine(root, cfgCat)))
        file = Path.GetFullPath(Path.Combine(root, cfgCat));
    else if (categoryFiles.Count > 0)
    {
        // Prefer the categories class that sits next to the IDs file; otherwise the one with most constants.
        var idsDir = diagIds is not null ? Path.GetDirectoryName(Path.Combine(root, diagIds["path"]!.ToString())) : null;
        file = categoryFiles.Keys
            .OrderByDescending(k => idsDir is not null && string.Equals(Path.GetDirectoryName(k), idsDir, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(k => stringConstRegex.Matches(categoryFiles[k].Text).Count)
            .ThenBy(k => k, StringComparer.Ordinal)
            .First();
    }
    if (file is not null)
    {
        if (!categoryFiles.TryGetValue(file, out var info))
        {
            var text = File.ReadAllText(file);
            var (cls, vis) = IdsFileText.ReadClass(text);
            info = (cls ?? "", vis, text);
        }
        var values = new JsonObject();
        foreach (Match m in stringConstRegex.Matches(info.Text)) values[m.Groups["name"].Value] = m.Groups["value"].Value;
        categoriesInfo = new JsonObject
        {
            ["path"] = Repo.Rel(root, file),
            ["className"] = info.ClassName,
            ["visibility"] = info.Visibility,
            ["values"] = values,
        };
    }
}

// ---------------------------------------------------------------------------
// resx groups
// ---------------------------------------------------------------------------
static (string Base, string Culture) SplitCulture(string fileName)
{
    var stem = Path.GetFileNameWithoutExtension(fileName);
    var m = Regex.Match(stem, @"^(?<base>.+)\.(?<culture>[a-z]{2,3}(?:-[A-Za-z0-9]{2,8})*)$");
    if (m.Success)
    {
        try { _ = System.Globalization.CultureInfo.GetCultureInfo(m.Groups["culture"].Value); return (m.Groups["base"].Value, m.Groups["culture"].Value); }
        catch { }
    }
    return (stem, "");
}

var resxGroups = new JsonArray();
foreach (var g in Repo.Files(root, "*.resx").GroupBy(f => (Dir: Path.GetDirectoryName(f)!, Base: SplitCulture(f).Base)).OrderBy(g => g.Key.Dir + g.Key.Base, StringComparer.Ordinal))
{
    var dir = g.Key.Dir;
    var baseFile = Path.Combine(dir, g.Key.Base + ".resx");
    var designer = Path.Combine(dir, g.Key.Base + ".Designer.cs");
    var owner = projects
        .Where(p => dir.Replace('\\', '/').StartsWith(p.FullDirectory.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(p => p.FullDirectory.Length).FirstOrDefault();
    string? generator = null;
    if (owner is not null)
    {
        if (owner.UsesResxSourceGenerator) generator = "Microsoft.CodeAnalysis.ResxSourceGenerator";
        else if (owner.ResxGenerators.TryGetValue(g.Key.Base + ".resx", out var gen)) generator = gen;
    }
    if (generator is null && File.Exists(designer)) generator = "ResXFileCodeGenerator (inferred from Designer.cs)";
    var resourceClass = g.Key.Base;
    if (File.Exists(designer))
    {
        var dm = Regex.Match(File.ReadAllText(designer), @"class\s+(\w+)");
        if (dm.Success) resourceClass = dm.Groups[1].Value;
    }
    // A hand-written helper such as `static LocalizableResourceString GetLocalizableResourceString(string name)`
    // (usually in a partial of the resource class). Its accessibility matters: a private helper means the
    // intended entry points are LocalizableResourceString properties inside the same class.
    JsonObject? helper = null;
    JsonObject? properties = null;
    if (owner is not null)
    {
        var helperRegex = new Regex(@"(?<acc>public|internal|private|protected)?\s*static\s+(?:Microsoft\.CodeAnalysis\.)?Localizable(?:Resource)?String\s+(?<m>\w+)\s*\(\s*string\s+\w+\s*\)");
        var propRegex = new Regex(@"(?<acc>public|internal)\s+static\s+(?:readonly\s+)?(?:Microsoft\.CodeAnalysis\.)?Localizable(?:Resource)?String\s+(?<name>\w+)\s*(?:\{|=>)");
        var propNames = new List<string>();
        foreach (var cs in Repo.Files(owner.FullDirectory, "*.cs"))
        {
            var text = File.ReadAllText(cs);
            var hm = helperRegex.Match(text);
            if (hm.Success && helper is null)
            {
                helper = new JsonObject
                {
                    ["class"] = string.Join('.', SourceScan.ContainingClasses(text, hm.Index)),
                    ["method"] = hm.Groups["m"].Value,
                    ["accessibility"] = hm.Groups["acc"].Success && hm.Groups["acc"].Value.Length > 0 ? hm.Groups["acc"].Value : "private",
                    ["file"] = Repo.Rel(root, cs),
                };
            }
            var pms = propRegex.Matches(text);
            if (pms.Count > 0 && properties is null)
            {
                var first = pms[0];
                var path = SourceScan.ContainingClasses(text, first.Index);
                var nested = path.Count >= 2 ? path[^1] : null;
                var sm = Regex.Match(first.Groups["name"].Value, @"^(?<base>.+?)(?:Title|Message|Description|Justification)(?<suffix>\w*)$");
                var suffix = sm.Success ? sm.Groups["suffix"].Value : "";
                properties = new JsonObject
                {
                    ["file"] = Repo.Rel(root, cs),
                    ["class"] = string.Join('.', path),
                    ["style"] = nested is not null ? "nested" : suffix.Length > 0 ? "suffix" : "unknown",
                    ["nestedClass"] = nested,
                    ["suffix"] = suffix,
                    ["accessibility"] = first.Groups["acc"].Value,
                    ["names"] = Json.Array(pms.Cast<Match>().Select(m => m.Groups["name"].Value)),
                };
            }
        }
    }
    var files = g.OrderBy(f => f, StringComparer.Ordinal).ToList();
    resxGroups.Add(new JsonObject
    {
        ["baseName"] = g.Key.Base,
        ["directory"] = Repo.Rel(root, dir),
        ["project"] = owner?.Name,
        ["files"] = Json.Array(files.Select(f => Repo.Rel(root, f))),
        ["cultures"] = Json.Array(files.Select(f => SplitCulture(f).Culture).OrderBy(c => c, StringComparer.Ordinal)),
        ["baseFileExists"] = File.Exists(baseFile),
        ["designerFile"] = File.Exists(designer) ? Repo.Rel(root, designer) : null,
        ["generator"] = generator,
        ["resourceClass"] = resourceClass,
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
        releases.Add(new JsonObject
        {
            ["project"] = p.Name,
            ["shipped"] = File.Exists(shipped) ? Repo.Rel(root, shipped) : null,
            ["unshipped"] = File.Exists(unshipped) ? Repo.Rel(root, unshipped) : null,
            ["expectedDirectory"] = p.Directory,
            // Whether the release-tracking analyzer (RS2000-RS2008) is reachable at all. Both values are
            // weak: the package flows transitively from Microsoft.CodeAnalysis.*, and the SDK registers the
            // AnalyzerReleases files as AdditionalFiles implicitly, so neither the package list nor the
            // project file proves anything. Only the RS2000 observation in SKILL.md 5e does.
            ["analyzersPackage"] = p.PackageReferences.Contains("Microsoft.CodeAnalysis.Analyzers") ? "direct"
                : p.PackageReferences.Any(r => r.StartsWith("Microsoft.CodeAnalysis.", StringComparison.Ordinal)) ? "viaCodeAnalysis"
                : "none",
        });
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

var allMarkdown = Repo.Files(root, "*.md").OrderBy(f => f, StringComparer.Ordinal).ToList();

string? docsDir = null;
if (config.Get("docsDir") is { } cfgDocs && Directory.Exists(Path.Combine(root, cfgDocs))) docsDir = Path.GetFullPath(Path.Combine(root, cfgDocs));
else
{
    docsDir = allMarkdown
        .Where(f => ruleDocRegex.IsMatch(Path.GetFileName(f)) || suppDocRegex.IsMatch(Path.GetFileName(f)))
        .GroupBy(f => Path.GetDirectoryName(f)!)
        .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
        .Select(g => g.Key).FirstOrDefault();
}

// Directories that a repository plausibly uses for documentation, whatever it calls them.
var docDirNameRegex = new Regex(@"^(docs?|documentation|wiki|rules|analyzers|diagnostics)$", RegexOptions.IgnoreCase);
var candidateDirs = Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
    .Where(d => !Repo.IsBuildOutput(d) && docDirNameRegex.IsMatch(Path.GetFileName(d)))
    .OrderBy(d => d.Count(c => c is '/' or '\\')).ThenBy(d => d, StringComparer.Ordinal)
    .Select(d => Repo.Rel(root, d))
    .ToList();

// Markdown that mentions an existing diagnostic ID (a single page listing every rule, a README table).
var knownIds = new[] { diagIds, suppIds }
    .Where(o => o is not null)
    .SelectMany(o => o!["ids"]!.AsArray().Select(i => i!["value"]!.ToString()))
    .Distinct(StringComparer.Ordinal).ToList();
var mentionFiles = new List<string>();
if (knownIds.Count > 0)
{
    var mentionRegex = new Regex(@"(^|[\s|(\[#`])(" + string.Join('|', knownIds.Select(Regex.Escape)) + @")([\s|)\].,:`]|$)", RegexOptions.Multiline);
    foreach (var f in allMarkdown)
    {
        if (docsDir is not null && Path.GetDirectoryName(f) == docsDir) continue;
        // Release tracking lists every ID by design; it is not documentation.
        if (Path.GetFileName(f).StartsWith("AnalyzerReleases.", StringComparison.OrdinalIgnoreCase)) continue;
        try
        {
            if (new FileInfo(f).Length > 512 * 1024) continue;
            if (mentionRegex.IsMatch(File.ReadAllText(f))) mentionFiles.Add(Repo.Rel(root, f));
        }
        catch { }
        if (mentionFiles.Count >= 20) break;
    }
}

// Where a new page should go when nothing exists yet: under the shallowest documentation-ish directory,
// else the conventional docs/rules.
var suggested = docsDir is not null
    ? Repo.Rel(root, docsDir)
    : candidateDirs.Count > 0
        ? (docDirNameRegex.IsMatch(Path.GetFileName(candidateDirs[0])) && Path.GetFileName(candidateDirs[0]).Equals("rules", StringComparison.OrdinalIgnoreCase)
            ? candidateDirs[0]
            : candidateDirs[0] + "/rules")
        : "docs/rules";

var docs = new JsonObject
{
    ["directory"] = null,
    ["indexFile"] = null,
    ["ruleDocs"] = new JsonArray(),
    ["suppressionDocs"] = new JsonArray(),
    ["candidateDirectories"] = Json.Array(candidateDirs),
    ["mentionFiles"] = Json.Array(mentionFiles),
    ["suggestedDirectory"] = suggested,
};
if (docsDir is not null)
{
    docs["directory"] = Repo.Rel(root, docsDir);
    foreach (var idx in new[] { config.Get("docsIndexFile"), "README.md", "index.md", "Index.md" })
    {
        if (idx is not null && File.Exists(Path.Combine(docsDir, idx))) { docs["indexFile"] = Repo.Rel(root, Path.Combine(docsDir, idx)); break; }
    }
    var mds = Directory.EnumerateFiles(docsDir, "*.md").OrderBy(f => f, StringComparer.Ordinal).ToList();
    docs["ruleDocs"] = Json.Array(mds.Where(f => ruleDocRegex.IsMatch(Path.GetFileName(f))).Select(f => Repo.Rel(root, f)));
    docs["suppressionDocs"] = Json.Array(mds.Where(f => suppDocRegex.IsMatch(Path.GetFileName(f))).Select(f => Repo.Rel(root, f)));
}

// ---------------------------------------------------------------------------
// Git
// ---------------------------------------------------------------------------
var git = GitInfo.Read(root);
var gitJson = new JsonObject
{
    ["remote"] = git.Remote,
    ["host"] = git.Host,
    ["owner"] = git.Owner,
    ["repo"] = git.RepoName,
    ["defaultBranch"] = git.DefaultBranch,
    ["docUrlTemplate"] = config.Get("docUrlTemplate") ?? git.DefaultTemplate,
};

// ---------------------------------------------------------------------------
// ID sharing
// ---------------------------------------------------------------------------
var sharing = config.Get("idSharing") ?? "none";
ProjectInfo? idsProject = null;
if (diagIds is not null)
{
    var idsDir = Path.GetDirectoryName(Path.Combine(root, diagIds["path"]!.ToString()))!.Replace('\\', '/');
    idsProject = projects
        .Where(p => idsDir.StartsWith(p.FullDirectory.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(p => p.FullDirectory.Length).FirstOrDefault();
}
// The values name *where the IDs live* and how a consumer reaches them, deliberately avoiding MSBuild
// item names since three of the four can be built out of <ProjectReference> items:
//
//                      | IDs in the analyzer project | IDs outside it
//   -------------------+-----------------------------+---------------------------------
//   reached by a       | AnalyzerProject             | SharedProject (a third project
//   project reference  |                             | both sides reference)
//   reached by a       | LinkedFile                  | SharedFile (a file owned by no
//   linked <Compile>   |                             | project, compiled by each side)
if (sharing == "none")
{
    var codeFixes = projects.Where(p => p.Kind == "codefix").ToList();
    var producers = projects.Where(p => p.Kind is "analyzer" or "generator").ToList();
    var analyzerPaths = producers.Select(p => p.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var idsFileName = diagIds is not null ? Path.GetFileName(diagIds["path"]!.ToString()) : null;
    bool LinksIdsFile(ProjectInfo p) =>
        idsFileName is not null && p.LinkedCompileFiles.Any(l => l.EndsWith(idsFileName, StringComparison.OrdinalIgnoreCase));

    // The IDs file lives in a separate project that analyzers and code fixes both reference.
    if (idsProject is not null && idsProject.Kind is not ("analyzer" or "generator") && codeFixes.Count > 0
        && codeFixes.All(cf => cf.ProjectReferences.Contains(idsProject.Path, StringComparer.OrdinalIgnoreCase))
        && producers.Any(p => p.ProjectReferences.Contains(idsProject.Path, StringComparer.OrdinalIgnoreCase)))
        sharing = "SharedProject";

    // The IDs file sits under no project at all and each side links it.
    if (sharing == "none" && idsProject is null && projects.Any(LinksIdsFile))
        sharing = "SharedFile";

    foreach (var cf in codeFixes)
    {
        if (sharing != "none") break;
        // The code fix references the analyzer project that owns the IDs file.
        if (cf.ProjectReferences.Any(analyzerPaths.Contains)) { sharing = "AnalyzerProject"; break; }
        // The code fix compiles the analyzer project's IDs file through a linked Compile item.
        if (LinksIdsFile(cf)) { sharing = "LinkedFile"; break; }
    }
}

// ---------------------------------------------------------------------------
// Output
// ---------------------------------------------------------------------------
// --summary keeps only what the workflow reads: the projects a diagnostic can be added to, without the
// reference lists that only fed kind detection and idSharing (both already computed above).
var reported = cli.Has("summary")
    ? projects.Where(p => p.Kind is "analyzer" or "generator" or "codefix" or "roslyn-component").ToList()
    : projects;
var reportedNames = reported.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
if (cli.Has("summary"))
{
    foreach (var g in resxGroups.OfType<JsonObject>().ToList())
        if (g["project"] is null || !reportedNames.Contains(g["project"]!.ToString())) resxGroups.Remove(g);
}
var projectsJson = Json.Array(reported.Select(p => (JsonNode?)p.ToJson()));
if (cli.Has("summary"))
{
    foreach (var p in projectsJson.OfType<JsonObject>())
        foreach (var key in new[] { "packageReferences", "projectReferences", "linkedCompileFiles", "resxGenerators" })
            p.Remove(key);
}

Json.Print(new JsonObject
{
    ["root"] = root.Replace('\\', '/'),
    ["config"] = new JsonObject { ["path"] = Config.RelativePath, ["exists"] = config.Exists, ["values"] = config.ToJson(), ["notes"] = config.Body },
    ["diagnosticPrefix"] = prefix,
    ["projects"] = projectsJson,
    ["diagnosticIds"] = diagIds,
    ["suppressionIds"] = suppIds,
    ["diagnosticCategories"] = categoriesInfo,
    ["idSharing"] = sharing,
    ["diagnosticIdsProject"] = idsProject?.Name,
    ["resx"] = resxGroups,
    ["analyzerReleases"] = releases,
    ["docs"] = docs,
    ["git"] = gitJson,
});

sealed record ProjectInfo(
    string Name, string Path, string Directory, string FullDirectory, string Kind, List<string> Roles,
    Dictionary<string, List<(string Class, string File)>> Classes, List<string> PackageReferences, List<string> ProjectReferences,
    List<string> LinkedCompileFiles, Dictionary<string, string> ResxGenerators, bool UsesResxSourceGenerator)
{
    public JsonObject ToJson()
    {
        var classes = new JsonObject();
        foreach (var (role, list) in Classes)
            classes[role] = Json.Array(list.Select(c => (JsonNode?)new JsonObject { ["class"] = c.Class, ["file"] = c.File }));
        var gens = new JsonObject();
        foreach (var (k, v) in ResxGenerators) gens[k] = v;
        return new JsonObject
        {
            ["name"] = Name,
            ["path"] = Path,
            ["directory"] = Directory,
            ["kind"] = Kind,
            ["roles"] = Json.Array(Roles),
            ["classes"] = classes,
            ["packageReferences"] = Json.Array(PackageReferences),
            ["projectReferences"] = Json.Array(ProjectReferences),
            ["linkedCompileFiles"] = Json.Array(LinkedCompileFiles),
            ["resxGenerators"] = gens,
            ["usesResxSourceGenerator"] = UsesResxSourceGenerator,
        };
    }
}
