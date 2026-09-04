using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

using Aetos.RoslynSkills.Tools.Internal;

namespace Aetos.RoslynSkills.Tools.AddDiagnostic;

/// <summary>A `const string Name = "PFX1001";` declaration.</summary>
internal sealed partial record IdConst(string Name, int Line, string Letters, int Number, int Digits)
{
    // Letters = everything before the number. For a suppression ID this ends with the extra 'S'.
    [GeneratedRegex(@"^(?<letters>[A-Z]{2,7}?)(?<num>\d{3,5})$")]
    private static partial Regex IdValue { get; }

    /// <summary>
    /// The ID as it is written in source. Computed rather than stored: the pattern above is anchored and
    /// <see cref="Digits"/> is the matched digit count, so the parts always reproduce the original exactly,
    /// and one formatter serves both parsing and allocating a new ID.
    /// </summary>
    public string Value => Format(this.Letters, this.Number, this.Digits);

    public static string Format(string letters, int number, int digits)
    {
        return letters + number.ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
    }

    public static List<IdConst> Parse([StringSyntax("c#")] string text)
    {
        var list = new List<IdConst>();
        foreach (var constant in CSharpSource.Parse(text).ConstStrings())
        {
            if (IdValue.Match(constant.Value) is not { Success: true } m)
            {
                continue;
            }

            var num = m.Groups["num"].Value;
            list.Add(new IdConst(
                constant.Name, constant.Line, m.Groups["letters"].Value,
                int.Parse(num, CultureInfo.InvariantCulture), num.Length));
        }
        return list;
    }

    /// <summary>
    /// The prefix a set of IDs uses: the most common letter group, ignoring a group that is another group plus
    /// 'S' (those are suppressions of it, not a prefix of their own). Ties break on the alphabet so the answer
    /// does not depend on the order files were scanned in. Null when there is nothing to infer from.
    /// </summary>
    public static string? InferPrefix(IEnumerable<IdConst> ids)
    {
        var groups = ids.GroupBy(i => i.Letters).ToDictionary(g => g.Key, g => g.Count());
        return groups.Keys
            .Where(k => !(k.EndsWith('S') && groups.ContainsKey(k[..^1])))
            .OrderByDescending(k => groups[k]).ThenBy(k => k, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public bool IsDiagnosticOf(string prefix)
    {
        return this.Letters == prefix;
    }

    public bool IsSuppressionOf(string prefix)
    {
        return this.Letters == prefix + "S";
    }
}
