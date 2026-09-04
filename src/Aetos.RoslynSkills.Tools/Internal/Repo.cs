using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Aetos.RoslynSkills.Tools.Internal;

/// <summary>
/// Where the repository root was taken to be. <see cref="Detected"/> is false when nothing identified a
/// repository and the path passed in was used instead: every path in a report is relative to the root, so a
/// subdirectory standing in for it produces a report that looks like a repository with almost nothing in it.
/// </summary>
internal sealed record RepoRoot(string Path, bool Detected, string? Error);

internal static partial class Repo
{
    /// <summary>
    /// The repository root: git's answer, else the nearest ancestor holding a .git entry, else the path
    /// passed in — reported as undetected, with git's reason for not answering.
    /// </summary>
    public static RepoRoot GetRoot(string start)
    {
        var full = Path.GetFullPath(start);
        var git = Shell.Exec("git", ["rev-parse", "--show-toplevel"], full, 15000);
        if (git.Output is { Length: > 0 } top)
        {
            return new RepoRoot(Path.GetFullPath(top), true, null);
        }

        // A worktree and a submodule carry a .git file rather than a directory.
        for (var dir = full; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            var dotGit = Path.Combine(dir, ".git");
            if (Directory.Exists(dotGit) || File.Exists(dotGit))
            {
                return new RepoRoot(dir, true, null);
            }
        }

        return new RepoRoot(full, false, git.Error);
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
}

/// <summary>
/// One walk of one repository: the root, the plugin trees left out of it, and the directories that could not
/// be read. State lives on the instance rather than in statics so a scan cannot leak its exclusions into the
/// next one, and so a test never has to reset the production copy.
/// </summary>
internal sealed class RepoScan
{
    private readonly List<string> _vendoredPlugins = [];
    private readonly List<string> _errors = [];

    public RepoScan(string root)
    {
        this.Root = Path.GetFullPath(root);

        // Empty until the search below finishes, which is what makes the search see the whole tree.
        this._vendoredPlugins.AddRange(this.FindVendoredPlugins());
    }

    public string Root { get; }

    /// <summary>
    /// Directory trees belonging to a Claude Code plugin checked into the repository. Their sample files look
    /// exactly like the real thing — this plugin's own examples/DiagnosticIds.cs declares a full set of IDs —
    /// so scanning them would report the plugin's samples as the repository's conventions. Detected by a
    /// plugin manifest, a SKILL.md, or a .claude/plugins directory.
    /// </summary>
    public IReadOnlyList<string> VendoredPlugins => this._vendoredPlugins;

    /// <summary>
    /// Directories that could not be read, with the reason. A caller has to report these: a scan that silently
    /// skips a tree it could not enter reports a partial repository as if it were the whole one.
    /// </summary>
    public IReadOnlyList<string> Errors => this._errors;

    public bool IsExcluded(string path)
    {
        return Repo.IsBuildOutput(path) || this.IsInVendoredPlugin(path);
    }

    public IEnumerable<string> Files(string directory, string pattern)
    {
        foreach (var dir in this.Directories(directory))
        {
            foreach (var file in this.FilesIn(dir, pattern))
            {
                yield return file;
            }
        }
    }

    public string Rel(string full)
    {
        return Repo.Rel(this.Root, full);
    }

    private bool IsInVendoredPlugin(string path)
    {
        return this._vendoredPlugins.Any(p =>
            path.Equals(p, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(p + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Walks the tree a directory at a time. EnumerateFiles with AllDirectories abandons the whole enumeration
    /// at the first directory it cannot read, which is why the walk is written out: one unreadable directory
    /// costs that directory and is recorded, not the rest of the repository.
    /// </summary>
    public IEnumerable<string> Directories(string start)
    {
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(start));

        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            yield return dir;

            string[] children;
            try
            {
                children = Directory.GetDirectories(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                this._errors.Add($"{this.Rel(dir)}: {ex.Message}");
                continue;
            }

            foreach (var child in children)
            {
                if (!this.IsExcluded(child))
                {
                    pending.Push(child);
                }
            }
        }
    }

    /// <summary>The matching files directly in one directory, with an unreadable directory recorded, not thrown.</summary>
    public IEnumerable<string> FilesIn(string dir, string pattern)
    {
        try
        {
            return Directory.GetFiles(dir, pattern);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this._errors.Add($"{this.Rel(dir)}: {ex.Message}");
            return [];
        }
    }

    private List<string> FindVendoredPlugins()
    {
        var found = new List<string>();
        void AddIfInside(string? dir)
        {
            if (dir is null)
            {
                return;
            }

            var full = Path.GetFullPath(dir);
            if (full.Length > this.Root.Length && !found.Contains(full, StringComparer.OrdinalIgnoreCase))
            {
                found.Add(full);
            }
        }

        foreach (var dir in this.Directories(this.Root))
        {
            if (File.Exists(Path.Combine(dir, ".claude-plugin", "plugin.json")) ||
                File.Exists(Path.Combine(dir, "SKILL.md")))
            {
                AddIfInside(dir);
            }

            if (Path.GetFileName(dir).Equals("plugins", StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(Path.GetDirectoryName(dir) ?? "").Equals(".claude", StringComparison.OrdinalIgnoreCase))
            {
                AddIfInside(dir);
            }
        }

        // Keep only the outermost tree of each nest, so the report names one directory per plugin.
        return found.Where(f => !found.Any(o => !ReferenceEquals(o, f) && f.Length > o.Length
            && f.StartsWith(o + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))).ToList();
    }
}
