using System;
using System.IO;
using System.Linq;

using Aetos.RoslynSkills.Tools.Internal;

namespace Aetos.RoslynSkills.Tools.Tests;

[TestClass]
public sealed class VendoredPluginTests(TestContext testContext) : IDisposable
{
    private readonly TempRepository _repo = new(testContext);

    public void Dispose()
    {
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

        var scan = new RepoScan(this._repo.Root);

        Assert.DoesNotContain(this._repo.Root, scan.VendoredPlugins.ToArray());
        Assert.IsFalse(scan.IsExcluded(Path.Combine(this._repo.Root, "src", "Analyzers", "A.cs")));
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

        var scan = new RepoScan(this._repo.Root);

        Assert.AreSequenceEqual([plugin], scan.VendoredPlugins.ToArray());
        Assert.IsTrue(scan.IsExcluded(Path.Combine(plugin, "skills", "add-diagnostic", "examples", "DiagnosticIds.cs")));
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

        Assert.AreSequenceEqual([skill], new RepoScan(this._repo.Root).VendoredPlugins.ToArray());
    }

    /// <summary>Guarantees an installed-plugins directory is excluded, which is where Claude Code caches plugins.</summary>
    [TestMethod]
    public void AnInstalledPluginsDirectoryIsExcluded()
    {
        var plugins = Path.Combine(this._repo.Root, ".claude", "plugins");
        this._repo.Write(".claude/plugins/roslyn-skills/skills/add-diagnostic/examples/DiagnosticIds.cs", "// sample");

        Assert.Contains(plugins, new RepoScan(this._repo.Root).VendoredPlugins.ToArray());
    }

    /// <summary>Guarantees a repository with no plugin in it excludes nothing.</summary>
    [TestMethod]
    public void ARepositoryWithNoVendoredPluginExcludesNothing()
    {
        this._repo.Write("src/Analyzers/A.cs", "// source");

        Assert.IsEmpty(new RepoScan(this._repo.Root).VendoredPlugins);
    }

    /// <summary>
    /// Guarantees one scan's exclusions cannot reach another: each scan carries its own, so a repository with a
    /// plugin in it does not leave a later scan of a plugin-free repository excluding the same paths.
    /// </summary>
    [TestMethod]
    public void OneScansExclusionsDoNotLeakIntoAnother()
    {
        this._repo.Write("vendor/plugin/SKILL.md", "# skill");
        var withPlugin = new RepoScan(this._repo.Root);
        Assert.IsNotEmpty(withPlugin.VendoredPlugins);

        var other = Directory.CreateDirectory(Path.Combine(this._repo.Root, "elsewhere"));
        var withoutPlugin = new RepoScan(other.FullName);

        Assert.IsEmpty(withoutPlugin.VendoredPlugins);
        Assert.IsFalse(withoutPlugin.IsExcluded(Path.Combine(this._repo.Root, "vendor", "plugin", "sample.cs")));
        Assert.IsTrue(withPlugin.IsExcluded(Path.Combine(this._repo.Root, "vendor", "plugin", "sample.cs")));
    }

    /// <summary>
    /// Guarantees the files a scan reports skip the vendored plugin and the build output, since a sample file
    /// under either is documentation rather than a source file of the repository.
    /// </summary>
    [TestMethod]
    public void TheFileListSkipsVendoredPluginsAndBuildOutput()
    {
        this._repo.Write("src/Analyzers/A.cs", "// source");
        this._repo.Write("src/Analyzers/obj/Generated.cs", "// generated");
        this._repo.Write("vendor/plugin/SKILL.md", "# skill");
        this._repo.Write("vendor/plugin/examples/DiagnosticIds.cs", "// sample");

        var scan = new RepoScan(this._repo.Root);
        var files = scan.Files(scan.Root, "*.cs").Select(scan.Rel).ToList();

        Assert.AreSequenceEqual(["src/Analyzers/A.cs"], files);
    }
}
