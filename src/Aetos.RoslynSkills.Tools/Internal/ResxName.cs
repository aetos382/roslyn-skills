using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Aetos.RoslynSkills.Tools.Internal;

/// <summary>Splits a resx file name into its base name and its culture, as in Resources.ja.resx.</summary>
internal static partial class ResxName
{
    // Subtags are matched in the casing culture names are written with — region ES, UN M.49 419, script Hans —
    // so a base name whose last segment merely contains a hyphen is not read as one.
    [GeneratedRegex(@"^(?<base>.+)\.(?<culture>(?<language>[a-z]{2,3})(?<subtags>(?:-(?:[A-Z]{2}|[0-9]{3}|[A-Z][a-z]{3}))*))$")]
    private static partial Regex CultureSuffix { get; }

    /// <summary>
    /// The languages a bare suffix is accepted as. A list rather than a CultureInfo lookup because the shape
    /// alone cannot tell Resources.ja.resx from a base name that merely ends in a short lowercase segment, and
    /// because asking the BCL would make the answer depend on the host's globalization data. These are the
    /// languages .NET itself localizes into, which is what an analyzer repository translates its resources to;
    /// a repository using another one adds it here. A suffix carrying a region or script needs no list, since
    /// nothing but a culture is written that way.
    /// </summary>
    private static readonly HashSet<string> Languages = new(StringComparer.Ordinal)
    {
        "cs", "de", "en", "es", "fr", "it", "ja", "ko", "pl", "pt", "ru", "tr", "zh",
    };

    /// <summary>
    /// The base name and culture of a resx path. A file with no recognized culture suffix is the neutral one,
    /// reported with its whole stem as the base name and an empty culture.
    /// </summary>
    public static (string Base, string Culture) Split(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path);
        if (CultureSuffix.Match(stem) is not { Success: true } m)
        {
            return (stem, "");
        }

        if (m.Groups["subtags"].Length == 0 && !Languages.Contains(m.Groups["language"].Value))
        {
            return (stem, "");
        }

        return (m.Groups["base"].Value, m.Groups["culture"].Value);
    }
}
