#!/usr/bin/env dotnet
#:property PublishAot=false
#:include Common.cs
// NextId.cs — computes the next free diagnostic (or suppression) ID from an ID constants file.
//
// Usage:  dotnet NextId.cs -- --ids-file <DiagnosticIds.cs> [--prefix ABC] [--category Usage | --band 2]
//                             [--digits 4] [--suppression]
//
// Category bands are read from comment headers in the file such as `// Design (ABC1xxx)`. With no band,
// the next number after the highest existing one is used. Suppression IDs (prefix + 'S' + number) have no bands.

using System.Text.Json.Nodes;

var cli = new CliArgs(args, "suppression");
var idsFile = cli.Require("ids-file");
var suppression = cli.Has("suppression");
var text = File.Exists(idsFile) ? File.ReadAllText(idsFile) : "";
var all = IdConst.Parse(text);

// Prefix: explicit, else config, else inferred from existing IDs, else from band headers (`// Design (ABC1xxx)`).
var prefix = cli.Get("prefix");
if (prefix is null)
{
    var cfg = new Config(Repo.GetRoot(Path.GetDirectoryName(Path.GetFullPath(idsFile))!));
    prefix = cfg.Get("diagnosticPrefix");
}
if (prefix is null && all.Count > 0)
{
    var letters = all.GroupBy(i => i.Letters).OrderByDescending(g => g.Count()).First().Key;
    prefix = suppression && letters.EndsWith('S') ? letters[..^1] : letters;
}
prefix ??= IdsFileText.ReadHeaderPrefix(text);
if (prefix is null)
    return Json.Fail($"No existing IDs or band headers with a prefix in '{idsFile}'.",
        "Pass --prefix <PREFIX>, and --band <n> for a diagnostic. This is the normal case for a repository with no diagnostics yet.");

var mine = all.Where(i => suppression ? i.IsSuppressionOf(prefix) : i.IsDiagnosticOf(prefix)).ToList();
var digits = cli.GetInt("digits") ?? (mine.Count > 0 ? mine.GroupBy(i => i.Digits).OrderByDescending(g => g.Count()).First().Key : 4);

var bands = IdsFileText.ReadBands(text);
int? band = cli.GetInt("band");
var category = cli.Get("category");
if (band is null && category is not null && bands.TryGetValue(category, out var b)) band = b;
if (band is null && category is not null && !suppression)
{
    // Fall back to the config's categories map.
    var cfg = new Config(Repo.GetRoot(Path.GetDirectoryName(Path.GetFullPath(idsFile))!));
    if (cfg.Maps.TryGetValue("categories", out var map) && map.TryGetValue(category, out var s) && int.TryParse(s, out var cb)) band = cb;
}
if (suppression) band = null;

var bandSize = (int)Math.Pow(10, digits - 1);
int next;
List<IdConst> inBand = new();
if (band is int bi)
{
    var low = bi * bandSize;
    var high = low + bandSize - 1;
    inBand = mine.Where(i => i.Number >= low && i.Number <= high).OrderBy(i => i.Number).ToList();
    next = inBand.Count > 0 ? inBand[^1].Number + 1 : low + 1;
    if (next > high)
        return Json.Fail($"Band {bi} ({prefix}{(suppression ? "S" : "")}{low}-{high}) is full.", "Choose another band for this category.");
}
else
{
    next = mine.Count > 0 ? mine.Max(i => i.Number) + 1 : 1;
}

var infix = suppression ? "S" : "";
var value = $"{prefix}{infix}{next.ToString().PadLeft(digits, '0')}";
if (mine.Any(i => i.Number == next)) return Json.Fail($"Computed ID {value} already exists.", "Re-run after checking the IDs file.");

var knownBands = new JsonObject();
foreach (var (k, v) in bands) knownBands[k] = v;
Json.Print(new JsonObject
{
    ["id"] = value,
    ["prefix"] = prefix,
    ["number"] = next,
    ["digits"] = digits,
    ["band"] = band,
    ["category"] = category,
    ["knownBands"] = knownBands,
    ["existingInBand"] = Json.Array(inBand.Select(i => i.Value)),
    ["highestOverall"] = mine.Count > 0 ? mine.Max(i => i.Number) : 0,
    ["unresolvedCategory"] = category is not null && band is null && !suppression,
});
return 0;
