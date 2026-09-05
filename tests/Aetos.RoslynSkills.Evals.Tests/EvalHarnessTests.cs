using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace Aetos.RoslynSkills.Evals.Tests;

/// <summary>
/// The parts of the eval harness that decide whether an assertion passed. A bug in any of them is worse than a
/// broken eval, because it does not announce itself: the run reports PASS and the skill looks fine.
/// </summary>
[TestClass]
public sealed class EvalHarnessTests
{
    private static readonly JsonObject Scan = new()
    {
        ["diagnosticIds"] = new JsonObject
        {
            ["ids"] = new JsonArray(
                new JsonObject { ["name"] = "TaskShouldBeAwaited", ["value"] = "ACM2001" },
                new JsonObject { ["name"] = "AsyncSuffixedMethodShouldReturnTask", ["value"] = "ACM3001" }),
        },
        ["suppressionIds"] = new JsonObject
        {
            ["ids"] = new JsonArray(
                new JsonObject { ["name"] = "TestClassesMayBePublic", ["value"] = "ACMS0001" }),
        },
    };

    private static GradingContext Context() => new("run", "repo", Scan, []);

    /// <summary>
    /// Guarantees a path ending in <c>[]</c> walks into every element of the array it names, since that is how
    /// every assertion about the set of IDs a scan reported is written.
    /// </summary>
    [TestMethod]
    public void ADottedPathExpandsTheArrayASegmentNames()
    {
        var values = Assertions.Select(Scan, "diagnosticIds.ids[].value").Select(n => n!.ToString()).ToList();

        Assert.AreSequenceEqual(["ACM2001", "ACM3001"], values);
    }

    /// <summary>
    /// Guarantees a path through something the scan did not report yields nothing rather than throwing, because
    /// a greenfield fixture legitimately has no diagnosticIds at all and a count of zero is the assertion.
    /// </summary>
    [TestMethod]
    public void APathThroughAMissingNodeYieldsNothing()
    {
        Assert.IsEmpty(Assertions.Select(Scan, "docs.ruleDocs[].path"));
        Assert.IsEmpty(Assertions.Select(new JsonObject(), "diagnosticIds.ids[]"));
    }

    /// <summary>
    /// Guarantees a name placeholder is replaced by the constant name the scan reported for that ID value, for
    /// diagnostics and suppressions alike: the name is the agent's to choose, so the ID value is the only part an
    /// assertion can name in advance.
    /// </summary>
    [TestMethod]
    public void ANamePlaceholderResolvesThroughTheIdValue()
    {
        var diagnostic = Assertions.Substitute("DiagnosticDescriptor {name:ACM3001}", Context(), out var unresolved);
        Assert.IsNull(unresolved);
        Assert.AreEqual("DiagnosticDescriptor AsyncSuffixedMethodShouldReturnTask", diagnostic);

        var suppression = Assertions.Substitute("{name:ACMS0001}", Context(), out unresolved);
        Assert.IsNull(unresolved);
        Assert.AreEqual("TestClassesMayBePublic", suppression);
    }

    /// <summary>
    /// Guarantees a placeholder for an ID that was never allocated reports itself as unresolved. Left to fall
    /// through, the pattern would search for the literal text and the assertion would fail with the wrong reason.
    /// </summary>
    [TestMethod]
    public void AnUnallocatedIdLeavesThePlaceholderUnresolved()
    {
        _ = Assertions.Substitute("{name:ACM9999}", Context(), out var unresolved);

        Assert.IsNotNull(unresolved);
        Assert.Contains("ACM9999", unresolved);
    }

    /// <summary>
    /// Guarantees <c>**/</c> also matches nothing at all, so <c>**/ACM3001.md</c> finds a page a skill put at the
    /// repository root as readily as one under docs/rules.
    /// </summary>
    [TestMethod]
    public void ADoubleStarMatchesAnyNumberOfDirectoriesIncludingNone()
    {
        var glob = Assertions.GlobToRegex("**/ACM3001.md");

        Assert.IsTrue(glob.IsMatch("ACM3001.md"));
        Assert.IsTrue(glob.IsMatch("docs/rules/ACM3001.md"));
        Assert.IsFalse(glob.IsMatch("docs/rules/ACM3002.md"));
    }

    /// <summary>
    /// Guarantees a single star stops at a directory separator, so <c>fileMissing</c> on <c>*.resx</c> is a claim
    /// about the repository root rather than about the whole tree.
    /// </summary>
    [TestMethod]
    public void ASingleStarDoesNotCrossDirectories()
    {
        var glob = Assertions.GlobToRegex("src/Acme.Analyzers/*.resx");

        Assert.IsTrue(glob.IsMatch("src/Acme.Analyzers/Resources.ja.resx"));
        Assert.IsFalse(glob.IsMatch("src/Acme.Analyzers/nested/Resources.resx"));
        Assert.IsFalse(glob.IsMatch("src/Other/Resources.resx"));
    }

    /// <summary>
    /// Guarantees the same warning repeated across targets counts once while two different warnings sharing a
    /// code count twice, because the build assertion compares those counts against the baseline and a repeat
    /// would read as a warning the agent introduced.
    /// </summary>
    [TestMethod]
    public void RepeatedWarningsCountOnceAndDistinctOnesCountSeparately()
    {
        var log = string.Join(
            "\n",
            "Analyzer.cs(31,58): warning IDE0090: 'new' expression can be simplified [C:\\a\\A.csproj]",
            "Analyzer.cs(31,58): warning IDE0090: 'new' expression can be simplified [C:\\a\\A.csproj]",
            "Analyzer.cs(44,12): warning IDE0090: 'new' expression can be simplified [C:\\a\\A.csproj]",
            "CSC : warning EnableGenerateDocumentationFile: Set MSBuild property 'GenerateDocumentationFile'");

        var counts = Harness.CountWarnings(log);
        var found = string.Join(", ", counts.Select(kv => $"{kv.Key}={kv.Value}"));

        Assert.AreEqual(2, counts.GetValueOrDefault("IDE0090"), found);

        // A code with no digits in it is still a warning, and one the counter used to be unable to see at all.
        Assert.AreEqual(1, counts.GetValueOrDefault("EnableGenerateDocumentationFile"), found);
    }

    /// <summary>
    /// Guarantees the evals.json files this repository ships are ones the harness can actually run: known
    /// assertion kinds, fixtures that exist, patterns that compile.
    /// </summary>
    [TestMethod]
    public void EveryShippedEvalIsWellFormed()
    {
        var problems = EvalSet.Validate();

        Assert.IsEmpty(problems, string.Join(System.Environment.NewLine, problems));
    }

    /// <summary>
    /// Guarantees every fixture builds its file set and that the project the grader is told to compile is one of
    /// the files in it. A typo there only shows up after an agent run has already been spent.
    /// </summary>
    [TestMethod]
    public void EveryFixtureProducesTheProjectItsGraderBuilds()
    {
        foreach (var (skillName, skill) in Skills.All)
        {
            foreach (var (fixtureName, make) in skill.Fixtures)
            {
                var fixture = make();
                var where = $"{skillName}/{fixtureName}";

                Assert.IsNotEmpty(fixture.Files, $"{where} produced no files");
                Assert.Contains(
                    fixture.BuildProject,
                    fixture.Files.Keys,
                    $"{where} names {fixture.BuildProject} as its build project, and does not produce it");

                // Nothing may escape the run directory: the fixtures are written by path, and a "../" would put
                // the agent's edits somewhere the grader never looks.
                foreach (var path in fixture.Files.Keys)
                {
                    Assert.IsFalse(
                        path.Contains("..", System.StringComparison.Ordinal) || Path.IsPathRooted(path),
                        $"{where} writes to '{path}', which is not inside the fixture");
                }
            }
        }
    }
}
