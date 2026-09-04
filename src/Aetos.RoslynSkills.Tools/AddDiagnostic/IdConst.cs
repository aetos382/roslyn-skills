using System.Text.RegularExpressions;

namespace Aetos.RoslynSkills.Tools.AddDiagnostic;

/// <summary>A `const string Name = "PFX1001";` declaration.</summary>
internal sealed record IdConst(string Name, string Value, int Line, string Letters, int Number, int Digits)
{
    // Letters = everything before the number. For a suppression ID this ends with the extra 'S'.
    private static readonly Regex IdValue = new(@"^(?<letters>[A-Z]{2,7}?)(?<num>\d{3,5})$", RegexOptions.Compiled);

    public static List<IdConst> Parse(string text)
    {
        var list = new List<IdConst>();
        foreach (var constant in CSharpSource.Parse(text).ConstStrings())
        {
            if (IdValue.Match(constant.Value) is not { Success: true } m) continue;
            var num = m.Groups["num"].Value;
            list.Add(new IdConst(
                constant.Name, constant.Value, constant.Line, m.Groups["letters"].Value, int.Parse(num), num.Length));
        }
        return list;
    }

    public bool IsDiagnosticOf(string prefix) => Letters == prefix;
    public bool IsSuppressionOf(string prefix) => Letters == prefix + "S";
}
