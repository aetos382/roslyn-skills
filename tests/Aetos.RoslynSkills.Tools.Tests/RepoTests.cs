using System;
using System.IO;

using Aetos.RoslynSkills.Tools.Internal;

namespace Aetos.RoslynSkills.Tools.Tests;

[TestClass]
public sealed class RepoTests(TestContext testContext) : IDisposable
{
    private readonly TempRepository _repo = new(testContext);

    public void Dispose()
    {
        this._repo.Dispose();
    }

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
    /// Guarantees a reported path is relative to the repository root, since every path in the JSON is read as
    /// repository-relative.
    /// </summary>
    [TestMethod]
    public void ReportedPathsAreRelativeToTheRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "repo");
        var file = Path.Combine(root, "src", "Analyzers", "A.cs");

        Assert.AreEqual("src/Analyzers/A.cs", Repo.Rel(root, file));
    }

    /// <summary>
    /// Guarantees the separator is converted to '/' on Windows, because the paths go into JSON the skill pastes
    /// into Markdown and URLs. Asserted on Windows only: everywhere else the separator already is '/', so the
    /// conversion is a no-op the test could not catch being dropped.
    /// </summary>
    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void ReportedPathsUseForwardSlashesOnWindows()
    {
        Assert.AreEqual("src/Analyzers/A.cs", Repo.Rel(@"C:\repo", @"C:\repo\src\Analyzers\A.cs"));
    }

    /// <summary>
    /// Guarantees the root is found by walking up to the .git directory when git itself cannot answer, and that
    /// it is reported as found: every path in a report is relative to the root, so a subdirectory standing in for
    /// it would look like a repository with almost nothing in it.
    /// </summary>
    [TestMethod]
    public void TheRootIsFoundByWalkingUpToTheGitDirectory()
    {
        Directory.CreateDirectory(Path.Combine(this._repo.Root, ".git"));
        var start = Directory.CreateDirectory(Path.Combine(this._repo.Root, "src", "Analyzers"));

        var root = Repo.GetRoot(start.FullName);

        Assert.AreEqual(this._repo.Root, root.Path);
        Assert.IsTrue(root.Detected);
        Assert.IsNull(root.Error);
    }

    /// <summary>
    /// Guarantees a worktree or submodule is recognised too: those carry a .git file rather than a directory,
    /// and requiring a directory would report their subdirectory as the root.
    /// </summary>
    [TestMethod]
    public void AWorktreeIsRecognisedByItsGitFile()
    {
        this._repo.Write(".git", "gitdir: /nonexistent/worktrees/wt");
        var start = Directory.CreateDirectory(Path.Combine(this._repo.Root, "src"));

        var root = Repo.GetRoot(start.FullName);

        Assert.AreEqual(this._repo.Root, root.Path);
        Assert.IsTrue(root.Detected);
    }
}
