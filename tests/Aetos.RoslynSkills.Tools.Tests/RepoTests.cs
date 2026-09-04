using System.IO;

using Aetos.RoslynSkills.Tools.Internal;

namespace Aetos.RoslynSkills.Tools.Tests;

[TestClass]
public sealed class RepoTests
{
    /// <summary>
    /// Guarantees generated and tooling directories are skipped, and that the match is on whole segments so
    /// a directory whose name merely begins with one of them keeps its sources in the scan.
    /// </summary>
    [TestMethod]
    public void GeneratedDirectoriesAreSkippedButSimilarNamesAreNot()
    {
        Assert.IsTrue(Repo.IsBuildOutput(@"C:\repo\src\Analyzers\bin\Debug\A.cs"));
        Assert.IsTrue(Repo.IsBuildOutput("/repo/src/obj/A.cs"));
        Assert.IsTrue(Repo.IsBuildOutput("/repo/.git/config"));
        Assert.IsFalse(Repo.IsBuildOutput("/repo/src/binaries/A.cs"));
        Assert.IsFalse(Repo.IsBuildOutput("/repo/src/Objects/A.cs"));
    }

    /// <summary>
    /// Guarantees the check needs a separator before the directory name, which is why it is only ever handed
    /// the absolute paths the file enumeration produces — a repository-relative path would slip through.
    /// </summary>
    [TestMethod]
    public void TheCheckOnlyRecognisesRootedPaths()
    {
        Assert.IsFalse(Repo.IsBuildOutput("bin/Debug/A.cs"));
        Assert.IsTrue(Repo.IsBuildOutput("/repo/bin/Debug/A.cs"));
    }

    /// <summary>
    /// Guarantees reported paths use forward slashes whatever the platform, because they go into JSON that
    /// the skill pastes into Markdown and URLs.
    /// </summary>
    [TestMethod]
    public void ReportedPathsUseForwardSlashes()
    {
        var root = Path.Combine(Path.GetTempPath(), "repo");
        var file = Path.Combine(root, "src", "Analyzers", "A.cs");

        Assert.AreEqual("src/Analyzers/A.cs", Repo.Rel(root, file));
    }
}
