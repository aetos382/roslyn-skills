using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Aetos.RoslynSkills.Tools.Tests;

/// <summary>
/// The skill's own documents, checked against the files beside them. A reference the agent is told to read and
/// that is not there costs a step of the workflow, and nothing else in the repository would notice.
/// </summary>
[TestClass]
public sealed partial class SkillDocumentTests
{
    private static readonly string[] CompanionDirectories = ["references", "examples"];

    [GeneratedRegex(@"(?<dir>references|examples)/(?<file>[A-Za-z0-9][A-Za-z0-9._-]*\.[A-Za-z0-9]+)")]
    private static partial Regex CompanionFile { get; }

    /// <summary>
    /// Guarantees every references/ and examples/ file the documents name exists: those paths are instructions to
    /// read a file, and a renamed one turns into a step the agent silently skips.
    /// </summary>
    [TestMethod]
    public void EveryCompanionFileTheDocumentsNameExists()
    {
        var missing = new List<string>();
        foreach (var (document, mention) in Mentions())
        {
            if (!File.Exists(Path.Combine(PluginSkill.Directory, mention.Replace('/', Path.DirectorySeparatorChar))))
            {
                missing.Add($"{document} names {mention}");
            }
        }

        Assert.IsEmpty(missing, string.Join(Environment.NewLine, missing));
    }

    /// <summary>
    /// Guarantees no references/ or examples/ file is shipped without a document naming it: an unmentioned file is
    /// one the agent never opens, so it is either dead weight or a document that forgot to point at it.
    /// </summary>
    [TestMethod]
    public void EveryCompanionFileIsNamedByADocument()
    {
        var mentioned = Mentions().Select(m => m.Mention).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unmentioned = CompanionDirectories
            .Select(d => Path.Combine(PluginSkill.Directory, d))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d))
            .Select(f => $"{Path.GetFileName(Path.GetDirectoryName(f))}/{Path.GetFileName(f)}")
            .Where(f => !mentioned.Contains(f))
            .ToList();

        Assert.IsEmpty(unmentioned, string.Join(Environment.NewLine, unmentioned));
    }

    private static IEnumerable<(string Document, string Mention)> Mentions()
    {
        foreach (var file in Directory.EnumerateFiles(PluginSkill.Directory, "*.md", SearchOption.AllDirectories))
        {
            foreach (Match m in CompanionFile.Matches(File.ReadAllText(file)))
            {
                yield return (Path.GetFileName(file), $"{m.Groups["dir"].Value}/{m.Groups["file"].Value}");
            }
        }
    }
}
