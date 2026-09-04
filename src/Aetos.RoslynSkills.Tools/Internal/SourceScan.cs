using System.Text.RegularExpressions;

namespace Aetos.RoslynSkills.Tools;

internal static class SourceScan
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
