using System.Text.RegularExpressions;

namespace Aetos.RoslynSkills.Tools.AddDiagnostic;

/// <summary>A `const string Name = "PFX1001";` declaration.</summary>
internal sealed record IdConst(string Name, string Value, int Line, string Letters, int Number, int Digits)
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
