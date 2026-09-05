using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Aetos.RoslynSkills.Evals;

/// <summary>
/// The checks evals.json is written in. Each one answers with a verdict and the evidence behind it, because a
/// bare "FAIL" on a run that took an agent several minutes is not enough to act on.
/// </summary>
internal static partial class Assertions
{
    // The descriptor field, the resource stem and the ID constant share a name the agent chooses, so no
    // assertion can spell it out. {name:ACM3001} stands for whatever name the IDs file gave that value.
    [GeneratedRegex(@"\{name:(?<value>[A-Za-z0-9]+)\}")]
    private static partial Regex NamePlaceholder { get; }

    [GeneratedRegex(@"error [A-Za-z][A-Za-z0-9]*:.*$", RegexOptions.Multiline)]
    private static partial Regex ErrorPattern { get; }

    /// <summary>The pattern with its name placeholders knocked out, for validating it without a run to resolve them against.</summary>
    public static string StripPlaceholders(string pattern) => NamePlaceholder.Replace(pattern, "X");

    /// <summary>
    /// Every kind <see cref="Evaluate"/> understands, kept beside the switch so the two are read together: a kind
    /// missing from here is one <c>check</c> rejects in an evals.json that would in fact have run.
    /// </summary>
    public static IReadOnlyList<string> Kinds { get; } =
    [
        "jsonContains", "jsonNotContains", "jsonCount", "jsonEquals",
        "fileExists", "fileMissing", "contains", "anyContains", "notContains",
        "resxEntryCount", "resxParity", "noLeftovers", "build",
    ];

    public static (bool Passed, string Evidence) Evaluate(JsonObject a, GradingContext c)
    {
        var kind = a["kind"]!.ToString();
        return kind switch
        {
            "jsonContains" => JsonContains(a, c, expected: true),
            "jsonNotContains" => JsonContains(a, c, expected: false),
            "jsonCount" => JsonCount(a, c),
            "jsonEquals" => JsonEquals(a, c),
            "fileExists" => FileExists(a, c, expected: true),
            "fileMissing" => FileExists(a, c, expected: false),
            "contains" => Contains(a, c, everyFile: true),
            "anyContains" => Contains(a, c, everyFile: false),
            "notContains" => NotContains(a, c),
            "resxEntryCount" => ResxEntryCount(a, c),
            "resxParity" => ResxParity(a, c),
            "noLeftovers" => NoLeftovers(c),
            "build" => Build(c),
            _ => (false, $"unknown assertion kind '{kind}'"),
        };
    }

    private static (bool, string) JsonContains(JsonObject a, GradingContext c, bool expected)
    {
        var path = a["path"]!.ToString();
        var value = a["value"]!.ToString();
        var found = Select(c.Scan, path).Select(n => n?.ToString()).ToList();
        var has = found.Contains(value, StringComparer.Ordinal);
        return (has == expected, $"{path} = [{string.Join(", ", found)}]");
    }

    private static (bool, string) JsonCount(JsonObject a, GradingContext c)
    {
        var path = a["path"]!.ToString();
        var expected = (int)a["count"]!;
        var found = Select(c.Scan, path).ToList();
        return (found.Count == expected, $"{path} holds {found.Count}, expected {expected}");
    }

    private static (bool, string) JsonEquals(JsonObject a, GradingContext c)
    {
        var path = a["path"]!.ToString();
        var expected = a["value"]!.ToString();
        var found = Select(c.Scan, path).Select(n => n?.ToString()).ToList();
        return (found.Count == 1 && found[0] == expected, $"{path} = [{string.Join(", ", found)}], expected {expected}");
    }

    private static (bool, string) FileExists(JsonObject a, GradingContext c, bool expected)
    {
        var glob = a["glob"]!.ToString();
        var files = Match(c, glob).ToList();
        return ((files.Count > 0) == expected, files.Count == 0 ? "nothing matched" : string.Join(", ", files.Select(f => Relative(c, f))));
    }

    private static (bool, string) Contains(JsonObject a, GradingContext c, bool everyFile)
    {
        var glob = a["glob"]!.ToString();
        var files = Match(c, glob).ToList();
        if (files.Count == 0)
        {
            return (false, $"no file matched {glob}");
        }

        var pattern = Substitute(a["pattern"]!.ToString(), c, out var unresolved);
        if (unresolved is not null)
        {
            return (false, unresolved);
        }

        var regex = new Regex(pattern, RegexOptions.Multiline);
        var hits = files.Where(f => regex.IsMatch(File.ReadAllText(f))).ToList();
        var ok = everyFile ? hits.Count == files.Count : hits.Count > 0;
        var missing = files.Except(hits).Select(f => Relative(c, f)).ToList();

        return (ok, ok
            ? $"matched in {string.Join(", ", hits.Select(f => Relative(c, f)))}"
            : $"/{pattern}/ not found in {string.Join(", ", missing)}");
    }

    private static (bool, string) NotContains(JsonObject a, GradingContext c)
    {
        var glob = a["glob"]!.ToString();
        var pattern = Substitute(a["pattern"]!.ToString(), c, out var unresolved);
        if (unresolved is not null)
        {
            return (false, unresolved);
        }

        var regex = new Regex(pattern, RegexOptions.Multiline);
        var hits = Match(c, glob).Where(f => regex.IsMatch(File.ReadAllText(f))).Select(f => Relative(c, f)).ToList();
        return (hits.Count == 0, hits.Count == 0 ? "not present" : $"/{pattern}/ found in {string.Join(", ", hits)}");
    }

    private static (bool, string) ResxEntryCount(JsonObject a, GradingContext c)
    {
        var expected = (int)a["count"]!;
        var files = Match(c, a["glob"]!.ToString()).ToList();
        if (files.Count == 0)
        {
            return (false, $"no file matched {a["glob"]}");
        }

        var counts = files.ToDictionary(f => Relative(c, f), f => DataNames(f).Count, StringComparer.Ordinal);
        var ok = counts.Values.All(n => n == expected);
        return (ok, string.Join(", ", counts.Select(kv => $"{kv.Key}: {kv.Value}")) + $" (expected {expected} each)");
    }

    private static (bool, string) ResxParity(JsonObject a, GradingContext c)
    {
        var files = Match(c, a["glob"]!.ToString()).ToList();
        if (files.Count < 2)
        {
            return (false, $"{files.Count} file(s) matched {a["glob"]}; parity needs at least two cultures");
        }

        var sets = files.ToDictionary(f => Relative(c, f), f => DataNames(f), StringComparer.Ordinal);
        var union = sets.Values.SelectMany(s => s).ToHashSet(StringComparer.Ordinal);
        var gaps = sets
            .Select(kv => (kv.Key, Missing: union.Except(kv.Value, StringComparer.Ordinal).ToList()))
            .Where(x => x.Missing.Count > 0)
            .ToList();

        return (gaps.Count == 0, gaps.Count == 0
            ? $"{union.Count} entries present in every culture"
            : string.Join("; ", gaps.Select(g => $"{g.Key} is missing {string.Join(", ", g.Missing)}")));
    }

    private static (bool, string) NoLeftovers(GradingContext c)
    {
        var leftovers = c.Scan["leftovers"]?.AsArray() ?? [];
        var described = leftovers.Select(l => $"{l!["kind"]} at {l["path"]}").ToList();
        return (described.Count == 0, described.Count == 0 ? "none" : string.Join("; ", described));
    }

    private static (bool, string) Build(GradingContext c)
    {
        if (c.Meta["baselineBuilt"]?.GetValue<bool>() != true)
        {
            return (false, "the run was created with --no-build, so there is no baseline to compare against");
        }

        var project = c.Meta["buildProject"]!.ToString();
        var (ok, counts, log) = Harness.Build(c.Repo, project);
        File.WriteAllText(Path.Combine(c.Run, "build.log"), log);

        if (!ok)
        {
            var errors = ErrorPattern.Matches(log)
                .Select(m => m.Value.Trim())
                .Distinct()
                .Take(5);
            return (false, "build failed: " + string.Join(" | ", errors));
        }

        var baseline = c.Meta["baselineWarnings"]!.AsObject()
            .ToDictionary(kv => kv.Key, kv => (int)kv.Value!, StringComparer.Ordinal);
        var regressions = counts
            .Where(kv => kv.Value > baseline.GetValueOrDefault(kv.Key))
            .Select(kv => $"{kv.Key}: {baseline.GetValueOrDefault(kv.Key)} -> {kv.Value}")
            .ToList();

        return (regressions.Count == 0, regressions.Count == 0
            ? "build succeeded with no new warnings"
            : "new warnings: " + string.Join(", ", regressions));
    }

    /// <summary>The <c>name</c> attribute of every <c>data</c> element in a resx.</summary>
    private static HashSet<string> DataNames(string file) =>
        XDocument.Load(file).Root!
            .Elements("data")
            .Select(d => d.Attribute("name")!.Value)
            .ToHashSet(StringComparer.Ordinal);

    internal static string Substitute(string pattern, GradingContext c, out string? unresolved)
    {
        string? failure = null;
        var result = NamePlaceholder.Replace(pattern, m =>
        {
            var value = m.Groups["value"].Value;
            var name = Select(c.Scan, "diagnosticIds.ids[]")
                .Concat(Select(c.Scan, "suppressionIds.ids[]"))
                .FirstOrDefault(n => n?["value"]?.ToString() == value)
                ?["name"]?.ToString();

            if (name is null)
            {
                failure ??= $"no constant with the value {value} exists, so the check has no name to look for";
                return value;
            }

            return Regex.Escape(name);
        });

        unresolved = failure;
        return result;
    }

    /// <summary>
    /// A dotted path into the scan's JSON, where a segment ending in <c>[]</c> expands the array it names:
    /// <c>diagnosticIds.ids[].value</c> is every ID value the scan found.
    /// </summary>
    internal static IEnumerable<JsonNode?> Select(JsonNode? root, string path)
    {
        IEnumerable<JsonNode?> current = [root];
        foreach (var raw in path.Split('.'))
        {
            var expand = raw.EndsWith("[]", StringComparison.Ordinal);
            var name = expand ? raw[..^2] : raw;

            current = current
                .Where(n => n is JsonObject)
                .Select(n => n![name])
                .Where(n => n is not null)
                .ToList();

            if (expand)
            {
                current = current.SelectMany(n => n is JsonArray array ? array.AsEnumerable() : []).ToList();
            }
        }

        return current;
    }

    private static IEnumerable<string> Match(GradingContext c, string glob)
    {
        // A glob starting with @ is about the run directory rather than the repository: the agent's own report
        // and the questions it answered for itself live beside the fixture, not inside it.
        var runRelative = glob.StartsWith('@');
        var root = runRelative ? c.Run : c.Repo;
        var pattern = runRelative ? glob[1..] : glob;
        var regex = GlobToRegex(pattern);

        return Enumerate(root)
            .Where(f => regex.IsMatch(Path.GetRelativePath(root, f).Replace('\\', '/')))
            .OrderBy(f => f, StringComparer.Ordinal);
    }

    private static IEnumerable<string> Enumerate(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.StartsWith(".git/", StringComparison.Ordinal) ||
                relative.Contains("/bin/", StringComparison.Ordinal) ||
                relative.Contains("/obj/", StringComparison.Ordinal) ||
                relative.StartsWith("bin/", StringComparison.Ordinal) ||
                relative.StartsWith("obj/", StringComparison.Ordinal))
            {
                continue;
            }

            yield return file;
        }
    }

    internal static Regex GlobToRegex(string glob)
    {
        var builder = new System.Text.StringBuilder("^");
        for (var i = 0; i < glob.Length; i++)
        {
            if (glob[i] == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
            {
                // "**/" also has to match nothing at all, so that **/X.md finds an X.md at the root.
                if (i + 2 < glob.Length && glob[i + 2] == '/')
                {
                    builder.Append("(?:.*/)?");
                    i += 2;
                }
                else
                {
                    builder.Append(".*");
                    i++;
                }
            }
            else if (glob[i] == '*')
            {
                builder.Append("[^/]*");
            }
            else if (glob[i] == '?')
            {
                builder.Append("[^/]");
            }
            else
            {
                builder.Append(Regex.Escape(glob[i].ToString()));
            }
        }

        return new Regex(builder.Append('$').ToString(), RegexOptions.IgnoreCase);
    }

    private static string Relative(GradingContext c, string file) =>
        Path.GetRelativePath(file.StartsWith(c.Repo, StringComparison.OrdinalIgnoreCase) ? c.Repo : c.Run, file)
            .Replace('\\', '/');
}
