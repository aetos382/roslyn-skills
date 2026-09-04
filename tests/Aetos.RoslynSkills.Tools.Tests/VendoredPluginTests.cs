using System;
using System.IO;
using System.Linq;

using Aetos.RoslynSkills.Tools.Internal;

namespace Aetos.RoslynSkills.Tools.Tests;

/// <summary>
/// The find-conventions command calls FindVendoredPlugins once and the result is kept in static state that
/// IsExcluded then reads, so these tests run one at a time and reset it afterwards.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class VendoredPluginTests(TestContext testContext) : IDisposable
{
    private readonly TempRepository _repo = new(testContext);

    public void Dispose()
    {
        // Scanning a directory with no markers in it is the only way to clear the static exclusion list.
        var empty = Directory.CreateDirectory(Path.Combine(this._repo.Root, "no-plugins-here"));
        Repo.FindVendoredPlugins(empty.FullName);
        this._repo.Dispose();
    }

    /// <summary>
    /// Guarantees the repository being inspected is never excluded from itself: this plugin's own repository
    /// carries a plugin manifest and a SKILL.md, and excluding the scan root would report nothing at all.
    /// </summary>
    [TestMethod]
    public void TheScanRootIsNotExcludedFromItself()
    {
        this._repo.Write(".claude-plugin/plugin.json", "{}");
        this._repo.Write("skills/add-diagnostic/SKILL.md", "# skill");

        var found = Repo.FindVendoredPlugins(this._repo.Root);

        Assert.DoesNotContain(this._repo.Root, found.ToArray());
        Assert.IsFalse(Repo.IsExcluded(Path.Combine(this._repo.Root, "src", "Analyzers", "A.cs")));
    }

    /// <summary>
    /// Guarantees a plugin checked into the repository is reported once, by its outermost directory, so its
    /// sample DiagnosticIds.cs is not mistaken for the repository's own conventions.
    /// </summary>
    [TestMethod]
    public void AVendoredPluginIsReportedOnceByItsOutermostDirectory()
    {
        var plugin = Path.Combine(this._repo.Root, "vendor", "roslyn-skills");
        this._repo.Write("vendor/roslyn-skills/.claude-plugin/plugin.json", "{}");
        this._repo.Write("vendor/roslyn-skills/skills/add-diagnostic/SKILL.md", "# skill");
        this._repo.Write("vendor/roslyn-skills/skills/add-diagnostic/examples/DiagnosticIds.cs", "// sample");

        var found = Repo.FindVendoredPlugins(this._repo.Root);

        Assert.AreSequenceEqual([plugin], found.ToArray());
        Assert.IsTrue(Repo.IsExcluded(Path.Combine(plugin, "skills", "add-diagnostic", "examples", "DiagnosticIds.cs")));
    }

    /// <summary>
    /// Guarantees a bare SKILL.md with no plugin manifest still excludes its directory, since a skill folder
    /// dropped into the repository has the same sample files.
    /// </summary>
    [TestMethod]
    public void ASkillDirectoryWithoutAManifestIsStillExcluded()
    {
        var skill = Path.Combine(this._repo.Root, "tools", "my-skill");
        this._repo.Write("tools/my-skill/SKILL.md", "# skill");

        var found = Repo.FindVendoredPlugins(this._repo.Root);

        Assert.AreSequenceEqual([skill], found.ToArray());
    }

    /// <summary>Guarantees an installed-plugins directory is excluded, which is where Claude Code caches plugins.</summary>
    [TestMethod]
    public void AnInstalledPluginsDirectoryIsExcluded()
    {
        var plugins = Path.Combine(this._repo.Root, ".claude", "plugins");
        this._repo.Write(".claude/plugins/roslyn-skills/skills/add-diagnostic/examples/DiagnosticIds.cs", "// sample");

        var found = Repo.FindVendoredPlugins(this._repo.Root);

        Assert.Contains(plugins, found.ToArray());
    }

    /// <summary>Guarantees a repository with no plugin in it excludes nothing.</summary>
    [TestMethod]
    public void ARepositoryWithNoVendoredPluginExcludesNothing()
    {
        this._repo.Write("src/Analyzers/A.cs", "// source");

        Assert.IsEmpty(Repo.FindVendoredPlugins(this._repo.Root));
    }
}
