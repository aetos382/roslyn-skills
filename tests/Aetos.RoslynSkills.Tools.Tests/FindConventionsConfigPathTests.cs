using System.Text.Json.Nodes;

namespace Aetos.RoslynSkills.Tools.Tests;

/// <summary>
/// How <c>find-conventions</c> treats a path named in <c>.claude/roslyn-skills/add-diagnostic.md</c> that does not
/// resolve. The two cases look identical in the settings file and are told apart by what the scan found, so each
/// test builds its own repository: the answer depends on the whole tree, not on one file.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class FindConventionsConfigPathTests
{
    private const string IdsSource = """
        namespace Sample
        {
            public static class DiagnosticIds
            {
                public const string First = "SMP1001";
            }
        }
        """;

    public FindConventionsConfigPathTests(TestContext context)
    {
        this.TestContext = context;
    }

    public TestContext TestContext { get; }

    /// <summary>
    /// Guarantees a configured path the scan can contradict fails the run instead of being replaced by the file
    /// detection found. Substituting it would report conventions the repository does not follow, and the report
    /// would look exactly like one from a repository whose settings are right.
    /// </summary>
    [TestMethod]
    public void AConfiguredPathTheScanContradictsIsAnError()
    {
        using var repo = new TempRepository(this.TestContext);
        repo.Write("src/Analyzers/DiagnosticIds.cs", IdsSource);
        repo.WriteConfig("""
            ```json
            { "diagnosticIdsFile": "src/Core/DiagnosticIds.cs" }
            ```
            """);

        var failure = Tool.Json(1, "add-diagnostic", "find-conventions", "--path", repo.Root, "--summary");

        var error = failure["error"]!.GetValue<string>();
        Assert.Contains("diagnosticIdsFile", error);
        Assert.Contains("src/Core/DiagnosticIds.cs", error);

        // Both paths, because the repair is a choice between them and only the user knows which is right.
        Assert.Contains("src/Analyzers/DiagnosticIds.cs", error);
        Assert.Contains("find-conventions again", failure["hint"]!.GetValue<string>());
    }

    /// <summary>
    /// Guarantees a configured documentation directory that is not there yet is taken as the destination when the
    /// scan found no pages anywhere. Git cannot carry an empty directory, so a repository that committed its
    /// settings before its first rule page is normally in this state, and failing here would leave the skill with
    /// no way forward.
    /// </summary>
    [TestMethod]
    public void AConfiguredDirectoryTheScanCannotContradictIsTheDestination()
    {
        using var repo = new TempRepository(this.TestContext);
        repo.Write("src/Analyzers/DiagnosticIds.cs", IdsSource);
        repo.WriteConfig("""
            ```json
            { "docsDir": "Documentation/rules" }
            ```
            """);

        var conventions = Tool.Json(0, "add-diagnostic", "find-conventions", "--path", repo.Root, "--summary");

        var docs = conventions["docs"]!.AsObject();

        // Nothing exists yet, so there is no directory to report...
        Assert.IsNull(docs["directory"]);

        // ...but the proposal is the configured path, not the conventional docs/rules: proposing a different one
        // would start a second layout in a repository that already said where the pages go.
        Assert.AreEqual("Documentation/rules", docs["suggestedDirectory"]!.GetValue<string>());
    }

    /// <summary>
    /// Guarantees a configured path that resolves still overrides detection, which is the whole point of the key:
    /// here the scan would pick the file with more IDs, and the settings pick the other one.
    /// </summary>
    [TestMethod]
    public void AConfiguredPathThatResolvesOverridesDetection()
    {
        using var repo = new TempRepository(this.TestContext);
        repo.Write("src/Legacy/DiagnosticIds.cs", """
            namespace Sample.Legacy
            {
                public static class DiagnosticIds
                {
                    public const string First = "SMP1001";
                    public const string Second = "SMP1002";
                }
            }
            """);
        repo.Write("src/Analyzers/DiagnosticIds.cs", IdsSource);
        repo.WriteConfig("""
            ```json
            { "diagnosticIdsFile": "src/Analyzers/DiagnosticIds.cs" }
            ```
            """);

        var conventions = Tool.Json(0, "add-diagnostic", "find-conventions", "--path", repo.Root, "--summary");

        Assert.AreEqual(
            "src/Analyzers/DiagnosticIds.cs",
            conventions["diagnosticIds"]!["path"]!.GetValue<string>());
    }
}
