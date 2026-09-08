using System.Globalization;
using System.IO;
using System.Linq;
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
internal sealed partial class Config
{
    public const string RelativePath = ".claude/roslyn-skills/add-diagnostic.md";

    private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonDocumentOptions DocumentOptions =
        new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    // `jsonc` is accepted as well as `json`, since comments are allowed and some editors only colour that tag.
    [GeneratedRegex(@"^ {0,3}(?<fence>`{3,}|~{3,})[ \t]*jsonc?[ \t]*$", RegexOptions.IgnoreCase)]
    private static partial Regex FenceStart { get; }

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

    /// <summary>
    /// The digit count the settings pin, null when they leave it open. Every command that mints or reports an ID
    /// reads it from here, so the two cannot disagree about the same repository. Reading it never fails: a value
    /// that is not a digit count has already been reported through <see cref="Error"/>.
    /// </summary>
    public int? IdDigits => ParseDigits(this.Get("idDigits"));

    public Config(string root)
    {
        this.Path = System.IO.Path.Combine(root, RelativePath);
        this.Exists = File.Exists(this.Path);
        if (!this.Exists)
        {
            return;
        }

        var lines = File.ReadAllLines(this.Path);
        var (start, end) = FindBlock(lines);
        if (start < 0)
        {
            // No `json` block: the file is notes only, which is a legitimate way to use it.
            this.Body = string.Join('\n', lines).Trim();
            return;
        }

        this.Body = string.Join('\n', lines.Take(start).Concat(lines.Skip(end + 1))).Trim();
        var block = string.Join('\n', lines[(start + 1)..end]).Trim();
        if (block.Length == 0)
        {
            return;
        }

        try
        {
            this.Values = JsonNode.Parse(block, NodeOptions, DocumentOptions) as JsonObject
                          ?? throw new JsonException("the block is not a JSON object");
        }
        catch (JsonException ex)
        {
            this.Error = $"The json block in '{RelativePath}' is not valid JSON: {ex.Message}";
        }

        // A value that parsed as JSON but is not the kind of thing its key stands for is reported here for the
        // same reason a malformed block is: a key nobody can use would otherwise be dropped, the default taken,
        // and nothing in the output would say the settings had been ignored.
        this.Error ??= Validate(this.Get("idDigits"));
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
            if (FenceStart.Match(lines[i]) is not { Success: true } m)
            {
                continue;
            }

            var fence = m.Groups["fence"].Value;
            var close = new Regex($@"^ {{0,3}}{Regex.Escape(fence[0].ToString())}{{{fence.Length},}}[ \t]*$");
            for (var j = i + 1; j < lines.Length; j++)
            {
                if (close.IsMatch(lines[j]))
                {
                    return (i, j);
                }
            }

            return (i, lines.Length);
        }
        return (-1, -1);
    }

    public string? Get(string key)
    {
        return Scalar(this.Values[key]);
    }

    /// <summary>One level down, for maps such as `categories`.</summary>
    public string? Get(string key, string nested)
    {
        return this.Values[key] is JsonObject m ? Scalar(m[nested]) : null;
    }

    public JsonObject ToJson()
    {
        return (JsonObject)this.Values.DeepClone();
    }

    /// <summary>
    /// Why a value that parsed cannot be used, null when it can. Only `idDigits` has a shape to check so far:
    /// a diagnostic ID has one to nine digits, and anything else — a word, a fraction, a zero — would be
    /// silently replaced by the default and mint IDs the settings did not ask for.
    /// </summary>
    private static string? Validate(string? idDigits)
    {
        if (idDigits is not null && ParseDigits(idDigits) is null)
        {
            return $"idDigits in '{RelativePath}' is '{idDigits}', which is not a digit count between 1 and 9.";
        }

        return null;
    }

    private static int? ParseDigits(string? value)
    {
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var digits) && digits is >= 1 and <= 9
            ? digits
            : null;
    }

    /// <summary>Callers want strings; numbers and booleans keep their JSON spelling.</summary>
    private static string? Scalar(JsonNode? node)
    {
        return node switch
        {
            null => null,
            JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
            _ => node.ToJsonString(),
        };
    }
}
