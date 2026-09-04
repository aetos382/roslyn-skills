using Microsoft.VisualStudio.TestTools.UnitTesting;

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
        var empty = Directory.CreateDirectory(Path.Combine(_repo.Root, "no-plugins-here"));
        Repo.FindVendoredPlugins(empty.FullName);
        _repo.Dispose();
    }

    /// <summary>
    /// Guarantees the repository being inspected is never excluded from itself: this plugin's own repository
    /// carries a plugin manifest and a SKILL.md, and excluding the scan root would report nothing at all.
    /// </summary>
    [TestMethod]
    public void TheScanRootIsNotExcludedFromItself()
    {
        _repo.Write(".claude-plugin/plugin.json", "{}");
        _repo.Write("skills/add-diagnostic/SKILL.md", "# skill");

        var found = Repo.FindVendoredPlugins(_repo.Root);

        CollectionAssert.DoesNotContain(found.ToArray(), _repo.Root);
        Assert.IsFalse(Repo.IsExcluded(Path.Combine(_repo.Root, "src", "Analyzers", "A.cs")));
    }

    /// <summary>
    /// Guarantees a plugin checked into the repository is reported once, by its outermost directory, so its
    /// sample DiagnosticIds.cs is not mistaken for the repository's own conventions.
    /// </summary>
    [TestMethod]
    public void AVendoredPluginIsReportedOnceByItsOutermostDirectory()
    {
        var plugin = Path.Combine(_repo.Root, "vendor", "roslyn-skills");
        _repo.Write("vendor/roslyn-skills/.claude-plugin/plugin.json", "{}");
        _repo.Write("vendor/roslyn-skills/skills/add-diagnostic/SKILL.md", "# skill");
        _repo.Write("vendor/roslyn-skills/skills/add-diagnostic/examples/DiagnosticIds.cs", "// sample");

        var found = Repo.FindVendoredPlugins(_repo.Root);

        CollectionAssert.AreEqual(new[] { plugin }, found.ToArray());
        Assert.IsTrue(Repo.IsExcluded(Path.Combine(plugin, "skills", "add-diagnostic", "examples", "DiagnosticIds.cs")));
    }

    /// <summary>
    /// Guarantees a bare SKILL.md with no plugin manifest still excludes its directory, since a skill folder
    /// dropped into the repository has the same sample files.
    /// </summary>
    [TestMethod]
    public void ASkillDirectoryWithoutAManifestIsStillExcluded()
    {
        var skill = Path.Combine(_repo.Root, "tools", "my-skill");
        _repo.Write("tools/my-skill/SKILL.md", "# skill");

        var found = Repo.FindVendoredPlugins(_repo.Root);

        CollectionAssert.AreEqual(new[] { skill }, found.ToArray());
    }

    /// <summary>Guarantees an installed-plugins directory is excluded, which is where Claude Code caches plugins.</summary>
    [TestMethod]
    public void AnInstalledPluginsDirectoryIsExcluded()
    {
        var plugins = Path.Combine(_repo.Root, ".claude", "plugins");
        _repo.Write(".claude/plugins/roslyn-skills/skills/add-diagnostic/examples/DiagnosticIds.cs", "// sample");

        var found = Repo.FindVendoredPlugins(_repo.Root);

        CollectionAssert.Contains(found.ToArray(), plugins);
    }

    /// <summary>Guarantees a repository with no plugin in it excludes nothing.</summary>
    [TestMethod]
    public void ARepositoryWithNoVendoredPluginExcludesNothing()
    {
        _repo.Write("src/Analyzers/A.cs", "// source");

        Assert.AreEqual(0, Repo.FindVendoredPlugins(_repo.Root).Count);
    }
}
