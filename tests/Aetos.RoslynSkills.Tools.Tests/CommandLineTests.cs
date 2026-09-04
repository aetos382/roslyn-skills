using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace Aetos.RoslynSkills.Tools.Tests;

/// <summary>
/// The command line the skill types. Every case goes through Program.Main with Console.Out redirected, so
/// these run one at a time.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CommandLineTests(TestContext testContext) : IDisposable
{
    private readonly TempRepository _repo = new(testContext);

    public void Dispose()
    {
        this._repo.Dispose();
    }

    private static (int ExitCode, string Output) Run(params string[] args)
    {
        var original = Console.Out;
        using var buffer = new StringWriter();
        Console.SetOut(buffer);
        try
        {
            return (Program.Main(args), buffer.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    /// <summary>
    /// Guarantees the command path SKILL.md invokes — the skill's own group plus its four subcommands — is the
    /// one the tool actually exposes: a rename on either side leaves the skill running a command that does not exist.
    /// </summary>
    [TestMethod]
    public void EverySubcommandTheSkillInvokesExists()
    {
        var groups = Program.CreateRootCommand().Subcommands;

        Assert.AreSequenceEqual(["add-diagnostic"], groups.Select(c => c.Name).ToArray(), SequenceOrder.InAnyOrder);
        Assert.AreSequenceEqual(
            ["find-conventions", "next-id", "add-resx-entries", "doc-url"],
            groups.Single().Subcommands.Select(c => c.Name).ToArray(),
            SequenceOrder.InAnyOrder);
    }

    /// <summary>
    /// Guarantees a missing required option is reported as JSON on stdout, not as System.CommandLine's own
    /// message on stderr: SKILL.md promises every failure is parseable, including a mistyped command line.
    /// </summary>
    [TestMethod]
    public void AMissingRequiredOptionIsReportedAsJson()
    {
        var (exitCode, output) = Run("add-diagnostic", "next-id");

        Assert.AreEqual(1, exitCode);
        var json = JsonNode.Parse(output)!.AsObject();
        Assert.Contains("--ids-file", json["error"]!.ToString());
        Assert.IsNotNull(json["hint"]);
    }

    /// <summary>Guarantees an option the tool does not have is reported the same way, rather than ignored.</summary>
    [TestMethod]
    public void AnUnknownOptionIsReportedAsJson()
    {
        var (exitCode, output) = Run("add-diagnostic", "doc-url", "--doc", "docs/rules/ABC1001.md", "--bogus");

        Assert.AreEqual(1, exitCode);
        Assert.Contains("--bogus", JsonNode.Parse(output)!["error"]!.ToString());
    }

    /// <summary>
    /// Guarantees a bare invocation lists the skill groups instead of failing: with no arguments there is no
    /// mistake to report, and the list is what the caller is after.
    /// </summary>
    [TestMethod]
    public void NoArgumentsPrintsTheCommandList()
    {
        var (exitCode, output) = Run();

        Assert.AreEqual(0, exitCode);
        Assert.Contains("add-diagnostic", output);
    }

    /// <summary>
    /// Guarantees a skill group named without a subcommand lists what it holds: the group is a grouping only,
    /// so stopping there is the same kind of lookup as a bare invocation, not an error.
    /// </summary>
    [TestMethod]
    public void ASkillGroupWithoutASubcommandPrintsItsSubcommands()
    {
        var (exitCode, output) = Run("add-diagnostic");

        Assert.AreEqual(0, exitCode);
        Assert.Contains("find-conventions", output);
    }

    /// <summary>
    /// Guarantees --resx takes several files both ways SKILL.md may write them — repeated options and a
    /// comma-separated list — since every culture file has to be updated in one run.
    /// </summary>
    [TestMethod]
    public void RepeatedAndCommaSeparatedResxOptionsAreBothAccepted()
    {
        const string Empty = """
            <?xml version="1.0" encoding="utf-8"?>
            <root>
              <resheader name="version">
                <value>2.0</value>
              </resheader>
            </root>
            """;
        var en = this._repo.Write("Resources.resx", Empty);
        var ja = this._repo.Write("Resources.ja.resx", Empty);
        var de = this._repo.Write("Resources.de.resx", Empty);

        var (exitCode, output) = Run(
            "add-diagnostic", "add-resx-entries", "--resx", $"{en},{ja}", "--resx", de,
            "--entries",
            /*lang=json,strict*/ """[{ "name": "ABC1001Title", "value": "Title" }]""");

        Assert.AreEqual(0, exitCode);
        var report = JsonNode.Parse(output)!.AsArray();
        Assert.AreSequenceEqual([en, ja, de], report.Select(r => r!["file"]!.ToString()).ToArray());
        foreach (var file in new[] { en, ja, de })
        {
            Assert.Contains("ABC1001Title", File.ReadAllText(file));
        }
    }

    /// <summary>
    /// Guarantees a failure raised inside a command is JSON too, and names the path it could not find: the
    /// skill passes absolute paths because its working directory is not the repository, so a wrong one is likely.
    /// </summary>
    [TestMethod]
    public void AFailureInsideACommandIsReportedAsJson()
    {
        var missing = Path.Combine(this._repo.Root, "Nowhere.resx");

        var (exitCode, output) = Run("add-diagnostic", "add-resx-entries", "--resx", missing, "--validate-only");

        Assert.AreEqual(1, exitCode);
        Assert.Contains(missing, JsonNode.Parse(output)!["error"]!.ToString());
    }
}
