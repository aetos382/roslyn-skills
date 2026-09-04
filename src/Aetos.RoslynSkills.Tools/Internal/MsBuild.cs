using System.Text.Json.Nodes;

namespace Aetos.RoslynSkills.Tools;

/// <summary>One evaluated MSBuild item: its identity, resolved path, and metadata.</summary>
internal sealed record MsBuildItem(string Identity, string? FullPath, Dictionary<string, string> Metadata);

/// <summary>The result of evaluating one project, or the reason evaluation failed.</summary>
internal sealed class Evaluation
{
    private readonly Dictionary<string, string> _properties = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<MsBuildItem>> _items = new(StringComparer.OrdinalIgnoreCase);

    public string? Error { get; private init; }

    public string? Property(string name) =>
        _properties.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    public bool IsTrue(string name) => string.Equals(Property(name), "true", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<MsBuildItem> Items(string name) =>
        _items.TryGetValue(name, out var list) ? list : Array.Empty<MsBuildItem>();

    public static Evaluation Failed(string error) => new() { Error = error };

    public static Evaluation Parse(string json)
    {
        var e = new Evaluation();
        if (JsonNode.Parse(json) is not JsonObject o) return Failed("MSBuild returned output that is not JSON.");
        if (o["Properties"] is JsonObject props)
            foreach (var (k, v) in props) e._properties[k] = v?.ToString() ?? "";
        if (o["Items"] is JsonObject items)
        {
            foreach (var (name, arr) in items)
            {
                var list = new List<MsBuildItem>();
                foreach (var n in arr as JsonArray ?? new JsonArray())
                {
                    if (n is not JsonObject io) continue;
                    var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (mk, mv) in io) meta[mk] = mv?.ToString() ?? "";
                    meta.TryGetValue("Identity", out var identity);
                    meta.TryGetValue("FullPath", out var fullPath);
                    list.Add(new MsBuildItem(identity ?? "", string.IsNullOrWhiteSpace(fullPath) ? null : fullPath, meta));
                }
                e._items[name] = list;
            }
        }
        return e;
    }
}

internal static class MsBuild
{
    // Properties and items the workflow reads. Requesting at least one item keeps the output JSON, which a
    // single -getProperty would not be.
    private static readonly string[] Properties =
        ["NeutralLanguage", "LangVersion", "TargetFramework", "TargetFrameworks", "IsTestProject", "UsingMSTestSdk"];
    private static readonly string[] Items =
        ["PackageReference", "ProjectReference", "Compile", "EmbeddedResource", "AssemblyAttribute"];

    /// <summary>
    /// Evaluates every project in parallel. The working directory is left alone: it is the caller's
    /// scratchpad, so the SDK comes from there rather than from a global.json inside the repository, which
    /// may pin a version that is not installed.
    /// </summary>
    public static Dictionary<string, Evaluation> EvaluateAll(IReadOnlyList<string> projects)
    {
        var results = new System.Collections.Concurrent.ConcurrentDictionary<string, Evaluation>(StringComparer.OrdinalIgnoreCase);
        Parallel.ForEach(projects, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2) },
            p => results[p] = Evaluate(p));
        return projects.ToDictionary(p => p, p => results[p], StringComparer.OrdinalIgnoreCase);
    }

    private static Evaluation Evaluate(string project)
    {
        // -nodeReuse:false so no MSBuild worker process outlives the scan holding file locks.
        var args = new List<string> { "msbuild", project, "-nologo", "-nodeReuse:false" };
        args.AddRange(Properties.Select(p => "-getProperty:" + p));
        args.AddRange(Items.Select(i => "-getItem:" + i));

        var (exit, stdout, stderr) = Shell.Exec("dotnet", args);
        if (exit != 0)
        {
            var message = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            return Evaluation.Failed(FirstLines(message, 3));
        }
        try { return Evaluation.Parse(stdout); }
        catch (Exception ex) { return Evaluation.Failed(ex.Message); }
    }

    private static string FirstLines(string text, int count) =>
        string.Join(" ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(count));
}
