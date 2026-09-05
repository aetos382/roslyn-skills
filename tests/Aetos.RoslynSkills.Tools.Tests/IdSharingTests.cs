using System.Collections.Generic;

using Aetos.RoslynSkills.Tools.AddDiagnostic;

namespace Aetos.RoslynSkills.Tools.Tests;

/// <summary>
/// How each code-fix project reaches the diagnostic IDs. The answer decides where a new ID constant is declared, and
/// a wrong one puts it somewhere the code fix cannot see, so every route the detection knows is checked here, along
/// with the repository-wide roll-up the skill reads before looking at the projects themselves.
/// </summary>
[TestClass]
public sealed class IdSharingTests
{
    private const string IdsFileName = "DiagnosticIds.cs";

    /// <summary>
    /// Guarantees a code fix that references the analyzer project owning the IDs file is reported as reaching the IDs
    /// through that project, which is the layout a new ID needs nothing extra for.
    /// </summary>
    [TestMethod]
    public void AProjectReferenceToTheAnalyzerIsTheAnalyzerProjectRoute()
    {
        var analyzer = Project("Analyzers", "analyzer");
        var codeFix = Project("CodeFixes", "codefix", projectReferences: [analyzer.Path]);

        Assert.AreEqual("AnalyzerProject", Route([analyzer, codeFix], analyzer, codeFix));
    }

    /// <summary>
    /// Guarantees a code fix that compiles the analyzer's IDs file as a linked file is told apart from one that
    /// references the project: the file is shared, the assembly is not, so the constants are declared once and
    /// compiled into both.
    /// </summary>
    [TestMethod]
    public void ALinkedCompileOfTheAnalyzersIdsFileIsTheLinkedFileRoute()
    {
        var analyzer = Project("Analyzers", "analyzer");
        var codeFix = Project("CodeFixes", "codefix", linkedCompileFiles: ["src/Analyzers/DiagnosticIds.cs"]);

        Assert.AreEqual("LinkedFile", Route([analyzer, codeFix], analyzer, codeFix));
    }

    /// <summary>
    /// Guarantees an IDs file living in a third project both sides reference is reported as a shared project rather
    /// than as the analyzer's own, since the new constant belongs in that project and not beside the analyzer.
    /// </summary>
    [TestMethod]
    public void AThirdProjectBothSidesReferenceIsTheSharedProjectRoute()
    {
        var common = Project("Common", "other");
        var analyzer = Project("Analyzers", "analyzer", projectReferences: [common.Path]);
        var codeFix = Project("CodeFixes", "codefix", projectReferences: [common.Path]);

        Assert.AreEqual("SharedProject", Route([common, analyzer, codeFix], common, codeFix));
    }

    /// <summary>
    /// Guarantees an IDs file that belongs to no project at all — compiled by each side through a linked Compile item,
    /// as a Visual Studio shared project does — is reported as a shared file, which no project owns the editing of.
    /// </summary>
    [TestMethod]
    public void AFileOwnedByNoProjectIsTheSharedFileRoute()
    {
        var analyzer = Project("Analyzers", "analyzer", linkedCompileFiles: ["shared/DiagnosticIds.cs"]);
        var codeFix = Project("CodeFixes", "codefix", linkedCompileFiles: ["shared/DiagnosticIds.cs"]);

        Assert.AreEqual("SharedFile", Route([analyzer, codeFix], null, codeFix));
    }

    /// <summary>
    /// Guarantees a repository with no route from a code fix to the IDs — and one with no code fix at all — is
    /// reported as having none instead of being given the closest-looking route: the skill asks in that case.
    /// </summary>
    [TestMethod]
    public void NoRouteFromTheCodeFixToTheIdsIsReportedAsNone()
    {
        var analyzer = Project("Analyzers", "analyzer");
        var codeFix = Project("CodeFixes", "codefix");

        Assert.AreEqual("none", Route([analyzer, codeFix], analyzer, codeFix));
        Assert.AreEqual("none", FindConventionsCommand.RollUpIdSharing(Sharing([analyzer], analyzer)),
            "a repository with no code-fix project shares nothing");
    }

    /// <summary>
    /// Guarantees a code fix that reaches the IDs project only through another project it references is reported as
    /// reaching them: project references are transitive for compilation, so the constants really are visible.
    /// </summary>
    [TestMethod]
    public void AnIndirectProjectReferenceReachesTheIds()
    {
        var common = Project("Common", "other");
        var analyzer = Project("Analyzers", "analyzer", projectReferences: [common.Path]);
        var codeFix = Project("CodeFixes", "codefix", projectReferences: [analyzer.Path]);

        Assert.AreEqual("SharedProject", Route([common, analyzer, codeFix], common, codeFix));
    }

    /// <summary>
    /// Guarantees each code-fix project is answered for on its own, and that a repository whose code fixes differ is
    /// reported as mixed rather than as the route the first one happens to take: a single value would hide the code
    /// fix that still cannot see the IDs, which is the one an edit has to reach.
    /// </summary>
    [TestMethod]
    public void CodeFixProjectsAreAnsweredForSeparately()
    {
        var analyzer = Project("Analyzers", "analyzer");
        var wired = Project("CodeFixes", "codefix", projectReferences: [analyzer.Path]);
        var unwired = Project("MoreCodeFixes", "codefix");

        var sharing = Sharing([analyzer, wired, unwired], analyzer);

        Assert.AreEqual("AnalyzerProject", sharing[wired.Path]);
        Assert.AreEqual("none", sharing[unwired.Path]);
        Assert.AreEqual("mixed", FindConventionsCommand.RollUpIdSharing(sharing));
    }

    private static Dictionary<string, string> Sharing(ProjectInfo[] projects, ProjectInfo? idsProject)
    {
        return FindConventionsCommand.DetectIdSharing(projects, idsProject, IdsFileName);
    }

    private static string Route(ProjectInfo[] projects, ProjectInfo? idsProject, ProjectInfo codeFix)
    {
        return Sharing(projects, idsProject)[codeFix.Path];
    }

    private static ProjectInfo Project(
        string name,
        string kind,
        string[]? projectReferences = null,
        string[]? linkedCompileFiles = null)
    {
        return new ProjectInfo
        {
            Name = name,
            Path = $"src/{name}/{name}.csproj",
            Directory = $"src/{name}",
            FullDirectory = $"/repo/src/{name}",
            Kind = kind,
            Roles = [],
            Classes = new(),
            PackageReferences = [],
            ProjectReferences = [.. projectReferences ?? []],
            LinkedCompileFiles = [.. linkedCompileFiles ?? []],
            ResxGenerators = new(),
            UsesResxSourceGenerator = false,
            NeutralLanguage = null,
            LangVersion = null,
            TargetFrameworks = null,
            EvaluationError = null,
        };
    }
}
