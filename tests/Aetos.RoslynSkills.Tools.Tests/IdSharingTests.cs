using Aetos.RoslynSkills.Tools.AddDiagnostic;

namespace Aetos.RoslynSkills.Tools.Tests;

/// <summary>
/// How the code-fix project reaches the diagnostic IDs. The answer decides where a new ID constant is declared, and
/// a wrong one puts it somewhere the code fix cannot see, so every route the detection knows is checked here.
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

        Assert.AreEqual("AnalyzerProject", FindConventionsCommand.DetectIdSharing([analyzer, codeFix], analyzer, IdsFileName));
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

        Assert.AreEqual("LinkedFile", FindConventionsCommand.DetectIdSharing([analyzer, codeFix], analyzer, IdsFileName));
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

        Assert.AreEqual("SharedProject", FindConventionsCommand.DetectIdSharing([common, analyzer, codeFix], common, IdsFileName));
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

        Assert.AreEqual("SharedFile", FindConventionsCommand.DetectIdSharing([analyzer, codeFix], null, IdsFileName));
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

        Assert.AreEqual("none", FindConventionsCommand.DetectIdSharing([analyzer, codeFix], analyzer, IdsFileName));
        Assert.AreEqual("none", FindConventionsCommand.DetectIdSharing([analyzer], analyzer, IdsFileName),
            "a repository with no code-fix project shares nothing");
    }

    /// <summary>
    /// Guarantees a third project only one of two code fixes references is not reported as shared: the other code fix
    /// would then be told the IDs are somewhere it cannot reach them.
    /// </summary>
    [TestMethod]
    public void AThirdProjectOnlyOneCodeFixReferencesIsNotShared()
    {
        var common = Project("Common", "other");
        var analyzer = Project("Analyzers", "analyzer", projectReferences: [common.Path]);
        var sharing = Project("CodeFixes", "codefix", projectReferences: [common.Path]);
        var notSharing = Project("MoreCodeFixes", "codefix", projectReferences: [analyzer.Path]);

        Assert.AreEqual(
            "AnalyzerProject",
            FindConventionsCommand.DetectIdSharing([common, analyzer, sharing, notSharing], common, IdsFileName));
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
