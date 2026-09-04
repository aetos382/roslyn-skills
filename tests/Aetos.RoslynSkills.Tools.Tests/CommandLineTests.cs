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
        return Tool.Run(args);
    }

    /// <summary>
    /// Guarantees every command line the skill's documents show can actually be typed: the subcommand exists and
    /// so does each option passed to it. Read out of the Markdown rather than restated here, because a rename in
    /// the tool otherwise leaves the documents telling the agent to run a command that no longer exists.
    /// </summary>
    [TestMethod]
    public void EveryCommandLineTheDocumentsShowIsOneTheToolAccepts()
    {
        var group = Program.CreateRootCommand().Subcommands.Single(c => c.Name == "add-diagnostic");
        var invocations = PluginSkill.Invocations().ToList();

        Assert.IsNotEmpty(invocations, "no command lines were found in the skill's documents");
        foreach (var (name, options, file) in invocations)
        {
            var command = group.Subcommands.SingleOrDefault(c => c.Name == name);
            Assert.IsNotNull(command, $"{file} runs 'add-diagnostic {name}', which the tool does not have");

            var known = command.Options.SelectMany(o => o.Aliases.Append(o.Name)).ToHashSet(StringComparer.Ordinal);
            foreach (var option in options)
            {
                Assert.Contains(option, known, $"{file} passes {option} to '{name}', which does not take it");
            }
        }
    }

    /// <summary>
    /// Guarantees the other direction too: a subcommand no document shows is one the skill cannot reach, so it is
    /// either undocumented or dead.
    /// </summary>
    [TestMethod]
    public void EverySubcommandTheToolExposesIsDocumented()
    {
        var group = Program.CreateRootCommand().Subcommands.Single(c => c.Name == "add-diagnostic");
        var documented = PluginSkill.Invocations().Select(i => i.Command).ToHashSet(StringComparer.Ordinal);

        Assert.AreSequenceEqual(
            group.Subcommands.Select(c => c.Name).ToArray(),
            documented.ToArray(),
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
    /// Guarantees a bug in the tool arrives as JSON on stdout with exit code 2, told apart from a rejected request by
    /// that code and by "unexpected": System.CommandLine would otherwise catch it first, print the trace to stderr and
    /// return 1, leaving the skill with nothing to parse and no way to know the request was never answered.
    /// </summary>
    [TestMethod]
    public void ABugInTheToolIsReportedAsJsonWithItsOwnExitCode()
    {
        var file = this._repo.Write("DiagnosticIds.cs",
            """
            internal static class DiagnosticIds
            {
                public const string DisposableField = "ABC1001";
            }
            """);

        // A negative digit count is not validated anywhere, so it reaches the formatting and throws.
        var (exitCode, output) = Run("add-diagnostic", "next-id", "--ids-file", file, "--digits", "-1");

        Assert.AreEqual(2, exitCode, output);
        var json = JsonNode.Parse(output)!.AsObject();
        Assert.IsTrue(json["unexpected"]!.GetValue<bool>());
        Assert.IsNotNull(json["error"]);
        Assert.IsNotNull(json["stackTrace"]);
    }

    /// <summary>
    /// Guarantees entries JSON the caller got wrong is reported as a rejected request rather than as a bug in the
    /// tool: the JSON is the agent's own, so re-writing it is exactly what fixes the run.
    /// </summary>
    [TestMethod]
    public void MalformedEntriesJsonIsARejectedRequestAndNotABug()
    {
        var resx = this._repo.Write("Resources.resx",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <root />
            """);

        var (exitCode, output) = Run("add-diagnostic", "add-resx-entries", "--resx", resx, "--entries", "[{ name: ");

        Assert.AreEqual(1, exitCode, output);
        var json = JsonNode.Parse(output)!.AsObject();
        Assert.Contains("--entries", json["error"]!.ToString());
        Assert.IsNull(json["unexpected"]);
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
