using System.Text.RegularExpressions;

namespace Aetos.RoslynSkills.Tools.AddDiagnostic;

internal static class IdsFileText
{
    private static readonly Regex BandHeader = new(
        @"^\s*//[\s-]*(?<name>[A-Za-z][\w ]*?)\s*[:(\-]+\s*(?<prefix>[A-Z]{2,7})?(?<band>\d)x{2,4}", RegexOptions.Compiled);

    /// <summary>Reads band headers such as `// Design (CTS1xxx)` or `// ---- Usage: CTS2xxx ----`.</summary>
    public static Dictionary<string, int> ReadBands(string text)
    {
        var bands = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in Headers(text))
            bands[m.Groups["name"].Value.Trim()] = int.Parse(m.Groups["band"].Value);
        return bands;
    }

    /// <summary>The prefix written in band headers (`CTS` in `// Design (CTS1xxx)`), when any header carries one.</summary>
    public static string? ReadHeaderPrefix(string text) =>
        Headers(text)
            .Select(m => m.Groups["prefix"].Value)
            .Where(p => p.Length > 0)
            .GroupBy(p => p).OrderByDescending(g => g.Count())
            .Select(g => g.Key).FirstOrDefault();

    public static (string? ClassName, string Visibility) ReadClass(string text) => CSharpSource.Parse(text).StaticClass();

    // Only real comments are considered, so a header quoted inside a string literal is not one.
    private static IEnumerable<Match> Headers(string text) =>
        CSharpSource.Parse(text).SingleLineComments().Select(c => BandHeader.Match(c)).Where(m => m.Success);
}
