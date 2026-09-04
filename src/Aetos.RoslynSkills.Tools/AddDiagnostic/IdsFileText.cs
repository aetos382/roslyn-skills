using System.Text.RegularExpressions;

namespace Aetos.RoslynSkills.Tools.AddDiagnostic;

internal static class IdsFileText
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
