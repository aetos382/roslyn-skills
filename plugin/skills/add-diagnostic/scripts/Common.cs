// Shared helpers for the add-diagnostic scripts. Included from each entry point with `#:include Common.cs`.
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

/// <summary>Minimal `--name value` / `--switch` argument parser.</summary>
sealed class CliArgs
{
    private readonly Dictionary<string, List<string>> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _switches = new(StringComparer.OrdinalIgnoreCase);

    public CliArgs(string[] args, params string[] switchNames)
    {
        var known = new HashSet<string>(switchNames, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (!a.StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Unexpected argument '{a}'.");
            var name = a[2..];
            var eq = name.IndexOf('=');
            if (eq >= 0)
            {
                Add(name[..eq], name[(eq + 1)..]);
                continue;
            }
            if (known.Contains(name))
            {
                _switches.Add(name);
                continue;
            }
            if (i + 1 >= args.Length) throw new ArgumentException($"Option '--{name}' needs a value.");
            Add(name, args[++i]);
        }
    }

    private void Add(string name, string value)
    {
        if (!_values.TryGetValue(name, out var list)) _values[name] = list = new List<string>();
        list.Add(value);
    }

    public string? Get(string name) => _values.TryGetValue(name, out var l) ? l[^1] : null;
    public string Require(string name) => Get(name) ?? throw new ArgumentException($"--{name} is required.");
    public IReadOnlyList<string> GetAll(string name) => _values.TryGetValue(name, out var l) ? l : Array.Empty<string>();
    public int? GetInt(string name) => Get(name) is { } s ? int.Parse(s) : null;
    public bool Has(string name) => _switches.Contains(name) || _values.ContainsKey(name);
}

static class Shell
{
    /// <summary>Runs a process and returns its exit code with both streams; exit code -1 means it never ran.</summary>
    public static (int ExitCode, string StdOut, string StdErr) Exec(string file, IEnumerable<string> args, string? workingDirectory = null, int timeoutMs = 120000)
    {
        try
        {
            var psi = new ProcessStartInfo(file)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return (-1, "", "process did not start");
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return (-1, "", $"timed out after {timeoutMs} ms"); }
            return (p.ExitCode, stdout.Result, stderr.Result);
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }

    /// <summary>Runs a process and returns stdout, or null when it fails or is missing.</summary>
    public static string? Run(string file, string args, string? workingDirectory = null, int timeoutMs = 15000)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return null; }
            return p.ExitCode == 0 ? stdout.Result.Trim() : null;
        }
        catch
        {
            return null;
        }
    }
}

static class Repo
{
    public static string GetRoot(string start)
    {
        var full = Path.GetFullPath(start);
        var top = Shell.Run("git", "rev-parse --show-toplevel", full);
        return string.IsNullOrEmpty(top) ? full : Path.GetFullPath(top);
    }

    public static string Rel(string root, string full) => Path.GetRelativePath(root, full).Replace('\\', '/');

    private static readonly Regex BuildOutput = new(@"[\\/](bin|obj|\.git|\.vs|node_modules|artifacts|TestResults)([\\/]|$)", RegexOptions.Compiled);
    public static bool IsBuildOutput(string path) => BuildOutput.IsMatch(path);

    private static string[] _vendoredPlugins = Array.Empty<string>();

    /// <summary>
    /// Directory trees belonging to a Claude Code plugin checked into the repository. Their sample files
    /// look exactly like the real thing — this plugin's own examples/DiagnosticIds.cs declares a full set
    /// of IDs — so scanning them would report the plugin's samples as the repository's conventions.
    /// Detected by a plugin manifest, a SKILL.md, or a .claude/plugins directory.
    /// </summary>
    public static IReadOnlyList<string> FindVendoredPlugins(string root)
    {
        var found = new List<string>();
        void AddIfInside(string? dir)
        {
            if (dir is null) return;
            var full = Path.GetFullPath(dir);
            if (full.Length > root.Length && !found.Contains(full, StringComparer.OrdinalIgnoreCase)) found.Add(full);
        }
        try
        {
            foreach (var f in Directory.EnumerateFiles(root, "plugin.json", SearchOption.AllDirectories))
            {
                var dir = Path.GetDirectoryName(f);
                if (dir is not null && Path.GetFileName(dir).Equals(".claude-plugin", StringComparison.OrdinalIgnoreCase))
                    AddIfInside(Path.GetDirectoryName(dir));
            }
            foreach (var f in Directory.EnumerateFiles(root, "SKILL.md", SearchOption.AllDirectories))
                AddIfInside(Path.GetDirectoryName(f));
            foreach (var d in Directory.EnumerateDirectories(root, "plugins", SearchOption.AllDirectories))
                if (Path.GetFileName(Path.GetDirectoryName(d) ?? "").Equals(".claude", StringComparison.OrdinalIgnoreCase))
                    AddIfInside(d);
        }
        catch { }
        // Keep only the outermost tree of each nest, so the report names one directory per plugin.
        var outermost = found.Where(f => !found.Any(o => !ReferenceEquals(o, f) && f.Length > o.Length
            && f.StartsWith(o + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))).ToList();
        _vendoredPlugins = outermost.ToArray();
        return outermost;
    }

    public static bool IsExcluded(string path) =>
        IsBuildOutput(path) ||
        _vendoredPlugins.Any(p => path.StartsWith(p + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<string> Files(string root, string pattern) =>
        Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories).Where(f => !IsExcluded(f));
}

/// <summary>
/// Reads .claude/roslyn-skills/add-diagnostic.md: the first fenced `json` block holds the settings, with
/// `//` comments and trailing commas allowed, and every line outside that block is free-form notes for the
/// skill. A fenced block keeps the file a real Markdown document, so it renders and gets highlighted
/// wherever it is read; JSON smuggled into a `---` front matter block does neither.
/// </summary>
sealed class Config
{
    public const string RelativePath = ".claude/roslyn-skills/add-diagnostic.md";

    private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonDocumentOptions DocumentOptions =
        new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    // `jsonc` is accepted as well as `json`, since comments are allowed and some editors only colour that tag.
    private static readonly Regex FenceStart =
        new(@"^ {0,3}(?<fence>`{3,}|~{3,})[ \t]*jsonc?[ \t]*$", RegexOptions.IgnoreCase);

    public string Path { get; }
    public bool Exists { get; }

    /// <summary>
    /// Why the settings could not be read; null when they parsed, the file has no `json` block, or the file
    /// is absent. Callers report it instead of carrying on, because a key dropped by a lenient parser would
    /// have the scripts describe conventions the repository does not actually follow.
    /// </summary>
    public string? Error { get; }

    public JsonObject Values { get; } = new();
    public string Body { get; } = "";

    public Config(string root)
    {
        Path = System.IO.Path.Combine(root, RelativePath);
        Exists = File.Exists(Path);
        if (!Exists) return;
        var lines = File.ReadAllLines(Path);
        var (start, end) = FindBlock(lines);
        if (start < 0)
        {
            // No `json` block: the file is notes only, which is a legitimate way to use it.
            Body = string.Join('\n', lines).Trim();
            return;
        }
        Body = string.Join('\n', lines.Take(start).Concat(lines.Skip(end + 1))).Trim();
        var block = string.Join('\n', lines[(start + 1)..end]).Trim();
        if (block.Length == 0) return;
        try
        {
            Values = JsonNode.Parse(block, NodeOptions, DocumentOptions) as JsonObject
                ?? throw new JsonException("the block is not a JSON object");
        }
        catch (JsonException ex)
        {
            Error = $"The json block in '{RelativePath}' is not valid JSON: {ex.Message}";
        }
    }

    /// <summary>
    /// The first fenced `json` block, as the line numbers of its opening and closing fences. Start is -1 when
    /// the document has none; end is one past the last line when the fence is never closed, which CommonMark
    /// treats as running to the end of the document rather than as an error.
    /// </summary>
    private static (int Start, int End) FindBlock(string[] lines)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            if (FenceStart.Match(lines[i]) is not { Success: true } m) continue;
            var fence = m.Groups["fence"].Value;
            var close = new Regex($@"^ {{0,3}}{Regex.Escape(fence[0].ToString())}{{{fence.Length},}}[ \t]*$");
            for (var j = i + 1; j < lines.Length; j++)
                if (close.IsMatch(lines[j])) return (i, j);
            return (i, lines.Length);
        }
        return (-1, -1);
    }

    public string? Get(string key) => Scalar(Values[key]);

    /// <summary>One level down, for maps such as `categories`.</summary>
    public string? Get(string key, string nested) => Values[key] is JsonObject m ? Scalar(m[nested]) : null;

    public JsonObject ToJson() => (JsonObject)Values.DeepClone();

    /// <summary>Callers want strings; numbers and booleans keep their JSON spelling.</summary>
    private static string? Scalar(JsonNode? node) => node switch
    {
        null => null,
        JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
        _ => node.ToJsonString(),
    };
}

sealed record GitInfo(string? Remote, string? Host, string? Owner, string? RepoName, string? DefaultBranch)
{
    public static GitInfo Read(string root)
    {
        string? remote = Shell.Run("git", "remote get-url origin", root);
        string? host = null, owner = null, repo = null;
        if (remote is not null)
        {
            var m = Regex.Match(remote, @"^(?:https?://|git@|ssh://git@)(?<host>[^/:]+)[/:](?<owner>[^/]+)/(?<repo>[^/]+?)(?:\.git)?/?$");
            if (m.Success) { host = m.Groups["host"].Value; owner = m.Groups["owner"].Value; repo = m.Groups["repo"].Value; }
        }
        var branch = Shell.Run("git", "symbolic-ref --short refs/remotes/origin/HEAD", root);
        if (!string.IsNullOrEmpty(branch)) branch = Regex.Replace(branch, "^origin/", "");
        if (string.IsNullOrEmpty(branch)) branch = Shell.Run("gh", "repo view --json defaultBranchRef -q .defaultBranchRef.name", root);
        if (string.IsNullOrEmpty(branch)) branch = Shell.Run("git", "branch --show-current", root);
        return new GitInfo(remote, host, owner, repo, string.IsNullOrEmpty(branch) ? null : branch);
    }

    public string? DefaultTemplate => Host == "github.com" ? "https://github.com/{owner}/{repo}/blob/{branch}/{path}" : null;
}

/// <summary>A `const string Name = "PFX1001";` declaration.</summary>
sealed record IdConst(string Name, string Value, int Line, string Letters, int Number, int Digits)
{
    // Letters = everything before the number. For a suppression ID this ends with the extra 'S'.
    private static readonly Regex ConstRegex = new(
        @"(?m)^\s*(?:public|internal|private)?\s*const\s+string\s+(?<name>\w+)\s*=\s*""(?<letters>[A-Z]{2,7}?)(?<num>\d{3,5})""\s*;",
        RegexOptions.Compiled);

    public static List<IdConst> Parse(string text)
    {
        var list = new List<IdConst>();
        foreach (Match m in ConstRegex.Matches(text))
        {
            var line = text.AsSpan(0, m.Index).Count('\n') + 1;
            var num = m.Groups["num"].Value;
            list.Add(new IdConst(m.Groups["name"].Value, m.Groups["letters"].Value + num, line, m.Groups["letters"].Value, int.Parse(num), num.Length));
        }
        return list;
    }

    public bool IsDiagnosticOf(string prefix) => Letters == prefix;
    public bool IsSuppressionOf(string prefix) => Letters == prefix + "S";
}

static class IdsFileText
{
    private static readonly Regex BandHeader = new(
        @"(?m)^\s*//[\s-]*(?<name>[A-Za-z][\w ]*?)\s*[:(\-]+\s*(?<prefix>[A-Z]{2,7})?(?<band>\d)x{2,4}", RegexOptions.Compiled);

    /// <summary>Reads band headers such as `// Design (CTS1xxx)` or `// ---- Usage: CTS2xxx ----`.</summary>
    public static Dictionary<string, int> ReadBands(string text)
    {
        var bands = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in BandHeader.Matches(text))
            bands[m.Groups["name"].Value.Trim()] = int.Parse(m.Groups["band"].Value);
        return bands;
    }

    /// <summary>The prefix written in band headers (`CTS` in `// Design (CTS1xxx)`), when any header carries one.</summary>
    public static string? ReadHeaderPrefix(string text) =>
        BandHeader.Matches(text).Cast<Match>()
            .Where(m => m.Groups["prefix"].Success && m.Groups["prefix"].Value.Length > 0)
            .GroupBy(m => m.Groups["prefix"].Value).OrderByDescending(g => g.Count())
            .Select(g => g.Key).FirstOrDefault();

    public static (string? ClassName, string Visibility) ReadClass(string text)
    {
        var m = Regex.Match(text, @"(?<vis>public|internal)?\s*static\s+(?:partial\s+)?class\s+(?<name>\w+)");
        if (!m.Success) return (null, "internal");
        return (m.Groups["name"].Value, m.Groups["vis"].Success && m.Groups["vis"].Value.Length > 0 ? m.Groups["vis"].Value : "internal");
    }
}

static class SourceScan
{
    /// <summary>
    /// Names of the classes whose bodies contain <paramref name="index"/>, outermost first
    /// (e.g. ["Resources", "Localizable"]). Brace matching ignores strings and comments, which is
    /// good enough for resource partials and ID files.
    /// </summary>
    public static List<string> ContainingClasses(string text, int index)
    {
        var result = new List<(int Start, int End, string Name)>();
        foreach (Match m in Regex.Matches(text, @"\b(?:class|struct|record)\s+(?<name>\w+)"))
        {
            var open = text.IndexOf('{', m.Index);
            if (open < 0) continue;
            var depth = 0;
            var close = -1;
            for (var i = open; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}' && --depth == 0) { close = i; break; }
            }
            if (close < 0) continue;
            if (index > open && index < close) result.Add((open, close, m.Groups["name"].Value));
        }
        return result.OrderBy(r => r.Start).Select(r => r.Name).ToList();
    }
}

static class Json
{
    public static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    public static JsonArray Array(IEnumerable<string> items) { var a = new JsonArray(); foreach (var i in items) a.Add(i); return a; }
    public static JsonArray Array(IEnumerable<JsonNode?> items) { var a = new JsonArray(); foreach (var i in items) a.Add(i); return a; }
    public static void Print(JsonNode node) => Console.WriteLine(node.ToJsonString(Indented));

    /// <summary>
    /// Reports an expected failure as JSON and yields the exit code, so callers see the same shape they
    /// get on success. Unexpected exceptions are deliberately left to crash: those are bugs, not results.
    /// </summary>
    public static int Fail(string error, string? hint = null)
    {
        Print(new JsonObject { ["error"] = error, ["hint"] = hint });
        return 1;
    }
}

static class Text
{
    public static (string Content, bool HasBom, string NewLine) ReadPreserving(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var content = new UTF8Encoding(false).GetString(bom ? bytes.AsSpan(3) : bytes);
        return (content, bom, content.Contains("\r\n") ? "\r\n" : "\n");
    }
}
