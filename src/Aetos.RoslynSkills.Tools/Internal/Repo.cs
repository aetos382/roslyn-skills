using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Aetos.RoslynSkills.Tools.Internal;

internal static partial class Repo
{
    public static string GetRoot(string start)
    {
        var full = Path.GetFullPath(start);
        var top = Shell.Run("git", "rev-parse --show-toplevel", full);
        return string.IsNullOrEmpty(top) ? full : Path.GetFullPath(top);
    }

    public static string Rel(string root, string full)
    {
        return Path.GetRelativePath(root, full).Replace('\\', '/');
    }

    [GeneratedRegex(@"[\\/](bin|obj|\.git|\.vs|node_modules|artifacts|TestResults)([\\/]|$)")]
    private static partial Regex BuildOutput { get; }

    public static bool IsBuildOutput(string path)
    {
        return BuildOutput.IsMatch(path);
    }

    private static string[] _vendoredPlugins = [];

    /// <summary>
    /// Directory trees belonging to a Claude Code plugin checked into the repository. Their sample files
    /// look exactly like the real thing — this plugin's own examples/DiagnosticIds.cs declares a full set
    /// of IDs — so scanning them would report the plugin's samples as the repository's conventions.
    /// Detected by a plugin manifest, a SKILL.md, or a .claude/plugins directory.
    /// </summary>
    public static IReadOnlyList<string> FindVendoredPlugins(string root)
    {
        var found = new List<string>();
        void AddIfInside(string? dir)
        {
            if (dir is null)
            {
                return;
            }

            var full = Path.GetFullPath(dir);
            if (full.Length > root.Length && !found.Contains(full, StringComparer.OrdinalIgnoreCase))
            {
                found.Add(full);
            }
        }

        try
        {
            foreach (var f in Directory.EnumerateFiles(root, "plugin.json", SearchOption.AllDirectories))
            {
                var dir = Path.GetDirectoryName(f);
                if (dir is not null &&
                    Path.GetFileName(dir).Equals(".claude-plugin", StringComparison.OrdinalIgnoreCase))
                {
                    AddIfInside(Path.GetDirectoryName(dir));
                }
            }

            foreach (var f in Directory.EnumerateFiles(root, "SKILL.md", SearchOption.AllDirectories))
            {
                AddIfInside(Path.GetDirectoryName(f));
            }

            foreach (var d in Directory.EnumerateDirectories(root, "plugins", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(Path.GetDirectoryName(d) ?? "")
                    .Equals(".claude", StringComparison.OrdinalIgnoreCase))
                {
                    AddIfInside(d);
                }
            }
        }
        catch
        {
        }

        // Keep only the outermost tree of each nest, so the report names one directory per plugin.
        var outermost = found.Where(f => !found.Any(o => !ReferenceEquals(o, f) && f.Length > o.Length
            && f.StartsWith(o + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))).ToList();

        _vendoredPlugins = outermost.ToArray();
        return outermost;
    }

    public static bool IsExcluded(string path)
    {
        return IsBuildOutput(path) ||
               _vendoredPlugins.Any(p =>
                   path.StartsWith(p + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    public static IEnumerable<string> Files(string root, string pattern)
    {
        return Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories).Where(f => !IsExcluded(f));
    }
}
