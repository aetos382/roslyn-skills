using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Aetos.RoslynSkills.Tools.AddDiagnostic;

/// <summary>
/// Reads .claude/roslyn-skills/add-diagnostic.md: the first fenced `json` block holds the settings, with
/// `//` comments and trailing commas allowed, and every line outside that block is free-form notes for the
/// skill. A fenced block keeps the file a real Markdown document, so it renders and gets highlighted
/// wherever it is read; JSON smuggled into a `---` front matter block does neither.
/// </summary>
internal sealed class Config
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
    /// have the tool describe conventions the repository does not actually follow.
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
