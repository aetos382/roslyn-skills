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

    public static IEnumerable<string> Files(string root, string pattern) =>
        Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories).Where(f => !IsBuildOutput(f));
}

/// <summary>Reads the YAML front matter of .claude/roslyn-skills.md (flat keys plus one level of nesting).</summary>
sealed class Config
{
    public const string RelativePath = ".claude/roslyn-skills.md";
    public string Path { get; }
    public bool Exists { get; }
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Dictionary<string, string>> Maps { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string Body { get; private set; } = "";

    public Config(string root)
    {
        Path = System.IO.Path.Combine(root, RelativePath);
        Exists = File.Exists(Path);
        if (!Exists) return;
        var lines = File.ReadAllLines(Path);
        if (lines.Length == 0 || lines[0].Trim() != "---") return;
        Dictionary<string, string>? current = null;
        var i = 1;
        for (; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Trim() == "---") { i++; break; }
            if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#')) continue;
            var nested = Regex.Match(line, @"^\s+(?<k>[^:#]+):\s*(?<v>.*)$");
            if (nested.Success && current is not null)
            {
                current[nested.Groups["k"].Value.Trim()] = Unquote(nested.Groups["v"].Value);
                continue;
            }
            var top = Regex.Match(line, @"^(?<k>[^:#\s][^:#]*):\s*(?<v>.*)$");
            if (!top.Success) continue;
            var key = top.Groups["k"].Value.Trim();
            var val = top.Groups["v"].Value.Trim();
            if (val.Length == 0) { current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); Maps[key] = current; }
            else { current = null; Values[key] = Unquote(val); }
        }
        Body = string.Join('\n', lines.Skip(i)).Trim();
    }

    private static string Unquote(string s)
    {
        s = s.Trim();
        var c = s.Split('#')[0].Trim(); // drop trailing comments
        return c.Trim('"').Trim('\'');
    }

    public string? Get(string key) => Values.TryGetValue(key, out var v) ? v : null;

    public JsonObject ToJson()
    {
        var o = new JsonObject();
        foreach (var (k, v) in Values) o[k] = v;
        foreach (var (k, m) in Maps)
        {
            var mo = new JsonObject();
            foreach (var (mk, mv) in m) mo[mk] = mv;
            o[k] = mo;
        }
        return o;
    }
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
