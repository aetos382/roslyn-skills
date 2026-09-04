using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Aetos.RoslynSkills.Tools.Tests;

/// <summary>
/// The add-diagnostic skill's Markdown, read from the repository so a test can check the tool against what the
/// skill actually tells the agent to type.
/// </summary>
internal static partial class PluginSkill
{
    /// <summary>
    /// One command line, from the `$T &lt;subcommand&gt;` shorthand the documents define or from a spelled-out
    /// `dotnet tool exec ... -- add-diagnostic &lt;subcommand&gt;`.
    /// </summary>
    [GeneratedRegex(@"(?:\$T|-- add-diagnostic) +(?<command>[a-z][a-z-]*)(?<rest>[^\n]*)")]
    private static partial Regex Invocation { get; }

    [GeneratedRegex(@"--[a-z][a-z-]*")]
    private static partial Regex OptionName { get; }

    /// <summary>A line continuation is joined first, so an invocation split over two lines is read whole.</summary>
    [GeneratedRegex(@"\\\r?\n\s*")]
    private static partial Regex Continuation { get; }

    public static string Directory { get; } = FindDirectory();

    /// <summary>Every command line the skill's documents show, as a subcommand name and the options passed to it.</summary>
    public static IEnumerable<(string Command, IReadOnlyList<string> Options, string File)> Invocations()
    {
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.md", SearchOption.AllDirectories))
        {
            var text = Continuation.Replace(File.ReadAllText(file), " ");
            foreach (Match m in Invocation.Matches(text))
            {
                var options = new List<string>();
                foreach (Match o in OptionName.Matches(m.Groups["rest"].Value))
                {
                    options.Add(o.Value);
                }

                yield return (m.Groups["command"].Value, options, Path.GetFileName(file));
            }
        }
    }

    private static string FindDirectory()
    {
        for (var dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            var candidate = Path.Combine(dir, "plugin", "skills", "add-diagnostic");
            if (File.Exists(Path.Combine(candidate, "SKILL.md")))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"plugin/skills/add-diagnostic was not found above {AppContext.BaseDirectory}.");
    }
}
