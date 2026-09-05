using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace Aetos.RoslynSkills.Tools.Tests;

/// <summary>
/// The JSON <c>find-conventions</c> hands to the skill, produced once from a repository built to exercise the
/// fields SKILL.md Step 1 tells the agent to read. Unlike the unit tests around it, this goes through MSBuild
/// evaluation the way a real run does, so a field that quietly changed name or shape fails here rather than in
/// somebody else's repository.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class FindConventionsOutputTests
{
    private const string Csproj = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>netstandard2.0</TargetFramework>
            <RootNamespace>Sample</RootNamespace>
          </PropertyGroup>
        </Project>
        """;

    private static TempRepository? _repository;
    private static JsonObject _conventions = null!;

    [ClassInitialize]
    public static void CreateRepository(TestContext context)
    {
        System.ArgumentNullException.ThrowIfNull(context);

        var repo = new TempRepository(context, nameof(FindConventionsOutputTests));
        _repository = repo;

        repo.Write("src/Analyzers/Analyzers.csproj", Csproj);
        repo.Write("src/Analyzers/SampleAnalyzer.cs", """
            using Microsoft.CodeAnalysis.Diagnostics;

            namespace Sample
            {
                public sealed class SampleAnalyzer : DiagnosticAnalyzer
                {
                }
            }
            """);
        repo.Write("src/Analyzers/DiagnosticIds.cs", """
            namespace Sample
            {
                public static class DiagnosticIds
                {
                    // Design (SMP1xxx)

                    public const string DisposableFieldShouldBeDisposed = "SMP1001";
                }
            }
            """);

        // Declared and never filled in, the way an interrupted run leaves them.
        repo.Write("src/Analyzers/DiagnosticCategories.cs", """
            namespace Sample
            {
                public static class DiagnosticCategories
                {
                }
            }
            """);
        repo.Write("src/Analyzers/AnalyzerReleases.Unshipped.md", """
            ; Unshipped analyzer release

            ### New Rules

            Rule ID | Category | Severity | Notes
            --------|----------|----------|-------
            """);
        Directory.CreateDirectory(Path.Combine(repo.Root, "docs", "rules"));

        // One code fix reaches the IDs through the analyzer project; the other reaches nothing.
        repo.Write("src/CodeFixes/CodeFixes.csproj", Csproj.Replace(
            "</PropertyGroup>",
            """
            </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\Analyzers\Analyzers.csproj" />
              </ItemGroup>
            """,
            System.StringComparison.Ordinal));
        repo.Write("src/CodeFixes/SampleCodeFix.cs", CodeFixSource("SampleCodeFix"));
        repo.Write("src/MoreCodeFixes/MoreCodeFixes.csproj", Csproj);
        repo.Write("src/MoreCodeFixes/OtherCodeFix.cs", CodeFixSource("OtherCodeFix"));

        _conventions = Tool.Json(0, "add-diagnostic", "find-conventions", "--path", repo.Root, "--summary");
    }

    [ClassCleanup]
    public static void DeleteRepository()
    {
        _repository?.Dispose();
        _repository = null;
    }

    /// <summary>
    /// Guarantees MSBuild evaluated every project, since every assertion below rests on it: an evaluation failure
    /// leaves the same JSON with the interesting fields empty, which reads as a repository that has nothing.
    /// </summary>
    [TestMethod]
    public void EveryProjectEvaluated()
    {
        foreach (var project in Projects())
        {
            Assert.IsNull(
                project!["evaluationError"]?.GetValue<string?>(),
                $"{project["name"]}: {project["evaluationError"]}");
        }
    }

    /// <summary>
    /// Guarantees the IDs, the prefix and the categories class are reported where Step 1 looks for them, since
    /// the steps that allocate an ID and add a constant take the files to edit from exactly these fields.
    /// </summary>
    [TestMethod]
    public void TheIdsPrefixAndCategoriesAreReported()
    {
        Assert.AreEqual("SMP", _conventions["diagnosticPrefix"]!.GetValue<string>());

        var ids = _conventions["diagnosticIds"]!.AsObject();
        Assert.Contains("DiagnosticIds.cs", ids["path"]!.GetValue<string>());
        Assert.Contains(
            "SMP1001",
            ids["ids"]!.AsArray().Select(i => i!["value"]!.GetValue<string>()).ToArray());

        Assert.Contains("DiagnosticCategories.cs", _conventions["diagnosticCategories"]!["path"]!.GetValue<string>());
    }

    /// <summary>
    /// Guarantees each code-fix project carries its own route to the IDs and that a repository whose code fixes
    /// differ rolls up to "mixed": 5g acts per project, and one repository-wide value would hide the code fix
    /// that still cannot see the IDs.
    /// </summary>
    [TestMethod]
    public void IdSharingIsReportedPerCodeFixProject()
    {
        Assert.AreEqual("AnalyzerProject", Project("CodeFixes")["idSharing"]!.GetValue<string>());
        Assert.AreEqual("none", Project("MoreCodeFixes")["idSharing"]!.GetValue<string>());
        Assert.AreEqual("mixed", _conventions["idSharing"]!.GetValue<string>());
        Assert.IsTrue(_conventions["idSharingReliable"]!.GetValue<bool>());
    }

    /// <summary>
    /// Guarantees what an interrupted run left behind is reported rather than read as convention: without this
    /// list Step 2.5 has to find an empty documentation directory, an empty categories class and a rowless
    /// release file by listing the repository by hand.
    /// </summary>
    [TestMethod]
    public void LeftoversAreReportedWithTheirKindAndPath()
    {
        // One entry per thing found, so the two empty documentation directories are two entries.
        var leftovers = _conventions["leftovers"]!.AsArray()
            .Select(l => $"{l!["kind"]!.GetValue<string>()} {l["path"]!.GetValue<string>()}")
            .ToArray();

        Assert.Contains("emptyDocumentationDirectory docs/rules", leftovers);
        Assert.Contains("categoriesClassWithoutConstants src/Analyzers/DiagnosticCategories.cs", leftovers);
        Assert.Contains("analyzerReleasesWithoutRules src/Analyzers/AnalyzerReleases.Unshipped.md", leftovers);
    }

    /// <summary>
    /// Guarantees a documentation candidate says how much it holds, so an empty directory left behind is told
    /// apart from one full of pages and from a source directory that merely has a documentation-ish name.
    /// </summary>
    [TestMethod]
    public void DocumentationCandidatesCarryTheirFileCounts()
    {
        var candidates = _conventions["docs"]!["candidateDirectories"]!.AsArray()
            .ToDictionary(d => d!["path"]!.GetValue<string>(), d => d!.AsObject());

        Assert.AreEqual(0, candidates["docs/rules"]["files"]!.GetValue<int>());
        Assert.AreEqual(0, candidates["docs/rules"]["markdownFiles"]!.GetValue<int>());

        // Named "Analyzers", so it is a candidate, but it holds source: not a leftover.
        Assert.IsGreaterThan(0, candidates["src/Analyzers"]["files"]!.GetValue<int>());
        Assert.AreEqual("docs/rules", _conventions["docs"]!["suggestedDirectory"]!.GetValue<string>());
    }

    private static string CodeFixSource(string className)
    {
        return $$"""
            using Microsoft.CodeAnalysis.CodeFixes;

            namespace Sample
            {
                public sealed class {{className}} : CodeFixProvider
                {
                }
            }
            """;
    }

    private static JsonArray Projects()
    {
        return _conventions["projects"]!.AsArray();
    }

    private static JsonObject Project(string name)
    {
        return Projects()
            .Select(p => p!.AsObject())
            .Single(p => p["name"]!.GetValue<string>() == name);
    }
}
