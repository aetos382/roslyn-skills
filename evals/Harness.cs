using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Aetos.RoslynSkills.Evals;

/// <summary>Building a fixture repository, running the tool and the compiler over it, and writing the prompt the
/// agent is handed.</summary>
internal static partial class Harness
{
    [GeneratedRegex(@"Aetos\.RoslynSkills\.Tools@(?<version>[0-9][^\s""]*)")]
    private static partial Regex ToolPinPattern { get; }

    // The whole line, because the whole line is what makes two warnings the same one. MSBuild repeats a warning
    // across the targets that report it, always with the same file, position and project, so matching the line
    // collapses those; matching only from "warning" onwards would also collapse two real warnings of the same
    // code at different places, which is exactly the case the build assertion exists to catch.
    // The code is not always letters-then-digits either: the SDK reports EnableGenerateDocumentationFile under
    // that name, and a warning the counter cannot see is a warning the build assertion would never notice.
    [GeneratedRegex(@"^.*?warning (?<code>[A-Za-z][A-Za-z0-9]*):.*$", RegexOptions.Multiline)]
    private static partial Regex WarningPattern { get; }

    /// <summary>The repository this file lives in, found from the compiler's own view of where it is.</summary>
    public static string RepoRoot { get; } = Directory.GetParent(ThisFile())!.Parent!.FullName;

    public static string SkillDirectory(string skill) =>
        Path.Combine(RepoRoot, "plugin", "skills", skill);

    /// <summary>
    /// Where a skill's prompts and fixtures live, one directory per skill. The skill names it, but does not name
    /// the folder: <see cref="SkillEvals.Directory"/> does, because a slash command and a folder of C# are named
    /// by different conventions.
    /// </summary>
    public static string EvalsDirectory(string skill) =>
        Path.Combine(RepoRoot, "evals", Skills.Get(skill).Directory);

    /// <summary>
    /// Runs live under the workspace's own <c>Temp/</c>, which git already ignores, so that what an eval produced
    /// stays next to the work rather than somewhere the machine may clear out. A fixture carries the MSBuild files
    /// that stop this repository's own from reaching it, so <c>--out</c> can point anywhere.
    /// </summary>
    public static string DefaultOutRoot { get; } = Path.Combine(RepoRoot, "Temp", "evals");

    /// <summary>
    /// The SDK pin this repository uses, for the fixtures to carry as their own <c>global.json</c>. Without one a
    /// fixture builds under whichever SDK happens to be newest on the machine, which decides what the analyzers
    /// say — and a baseline that shifts under the eval is a baseline that cannot separate the agent's warnings
    /// from the toolchain's.
    /// </summary>
    public static string FixtureGlobalJson { get; } = ReadSdkPin();

    /// <summary>
    /// The tool version that skill's SKILL.md tells the agent to run. Read from the skill rather than pinned here,
    /// so an eval never silently measures a different release than the one the skill ships with.
    /// </summary>
    public static string ToolPin(string skill)
    {
        if (ToolPins.TryGetValue(skill, out var cached))
        {
            return cached;
        }

        var text = File.ReadAllText(Path.Combine(SkillDirectory(skill), "SKILL.md"));
        var match = ToolPinPattern.Match(text);
        var version = match.Success
            ? match.Groups["version"].Value
            : throw new InvalidOperationException($"{skill}/SKILL.md does not name a pinned tool version.");

        ToolPins[skill] = version;
        return version;
    }

    private static readonly Dictionary<string, string> ToolPins = new(StringComparer.Ordinal);

    /// <summary>Every eval of every skill, in the order the skills are registered.</summary>
    public static IEnumerable<(string Skill, JsonObject Eval)> Evals()
    {
        foreach (var skill in Skills.All.Keys)
        {
            foreach (var eval in Evals(skill))
            {
                yield return (skill, eval);
            }
        }
    }

    public static IEnumerable<JsonObject> Evals(string skill)
    {
        var file = Path.Combine(EvalsDirectory(skill), "evals.json");
        var json = JsonNode.Parse(File.ReadAllText(file))!;
        return json["evals"]!.AsArray().Select(e => e!.AsObject());
    }

    /// <summary>
    /// Finds the eval a command line names, as a bare id or as <c>skill:id</c>. A bare id that two skills both use
    /// is refused rather than guessed at: picking one silently would run one skill's eval and grade it against the
    /// other's assertions.
    /// </summary>
    public static (string Skill, JsonObject Eval) Resolve(string reference)
    {
        var parts = reference.Split(':', 2);
        var matches = Evals()
            .Where(e => parts.Length == 2
                ? e.Skill == parts[0] && e.Eval["id"]!.ToString() == parts[1]
                : e.Eval["id"]!.ToString() == reference)
            .ToList();

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new KeyNotFoundException($"unknown eval '{reference}'; run 'list' to see them."),
            _ => throw new ArgumentException(
                $"'{reference}' names an eval in more than one skill ({string.Join(", ", matches.Select(m => m.Skill))}); "
                + "say which as <skill>:<eval-id>.",
                nameof(reference)),
        };
    }

    public static void Materialize(Fixture fixture, string directory)
    {
        foreach (var (relative, content) in fixture.Files)
        {
            var path = Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content.TrimEnd() + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    /// <summary>
    /// A real repository with a real remote: <c>doc-url</c> derives the documentation URL from it, and a fixture
    /// without one would silently exercise the "no remote" branch in every eval.
    /// </summary>
    public static void InitGit(string repo, string remote)
    {
        Git(repo, "init", "-b", "main");
        Git(repo, "config", "user.name", "eval");
        Git(repo, "config", "user.email", "eval@example.invalid");
        Git(repo, "config", "commit.gpgsign", "false");
        Git(repo, "add", "-A");
        Git(repo, "commit", "-m", "Initial state");
        Git(repo, "remote", "add", "origin", remote);
    }

    /// <summary>
    /// The structured reading of the fixture that a skill's <c>Scan</c> command produces, which is what the
    /// <c>json*</c> assertions are written against. Null for a skill that declares no such command.
    /// </summary>
    public static string? Scan(string skill, string repo)
    {
        if (Skills.Get(skill).Scan is not { } command)
        {
            return null;
        }

        // Run it from outside the repository, the way the skill insists on: inside it, the fixture's own
        // NuGet.config and its global.json start deciding what the tool runs as.
        var scratch = Directory.CreateTempSubdirectory("roslyn-skills-eval-").FullName;
        try
        {
            string[] arguments =
            [
                "tool", "exec", $"Aetos.RoslynSkills.Tools@{ToolPin(skill)}", "--",
                .. command.Select(a => a.Replace("{repo}", repo, StringComparison.Ordinal)),
            ];

            var (exit, stdout, stderr) = Run("dotnet", arguments, scratch);

            if (exit != 0 || stdout.Length == 0)
            {
                throw new InvalidOperationException(
                    $"the {skill} scan failed (exit {exit}):\n{stdout}\n{stderr}");
            }

            return stdout;
        }
        finally
        {
            try
            {
                Directory.Delete(scratch, recursive: true);
            }
            catch (IOException)
            {
                // A leftover scratch directory in the machine's temp is not worth failing a run over.
            }
        }
    }

    /// <summary>Builds the fixture's analyzer project and returns how many distinct warnings each code produced.</summary>
    public static (bool Ok, Dictionary<string, int> Warnings, string Log) Build(string repo, string project)
    {
        var (exit, stdout, stderr) = Run(
            "dotnet",
            ["build", project, "-nodeReuse:false", "--nologo", "-v:m"],
            repo,
            ("DOTNET_CLI_UI_LANGUAGE", "en"),
            ("VSLANG", "1033"));

        var log = stdout + stderr;
        return (exit == 0, CountWarnings(log), log);
    }

    /// <summary>
    /// How many distinct warnings each code produced in a build log, which is what the <c>build</c> assertion
    /// compares against the baseline.
    /// </summary>
    internal static Dictionary<string, int> CountWarnings(string log) =>
        WarningPattern.Matches(log)
            .Select(m => (Code: m.Groups["code"].Value, Line: m.Value.Trim()))
            .Distinct()
            .GroupBy(w => w.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

    /// <summary>
    /// The task the agent is handed. It has to say how to behave without a human in front of it, because the
    /// skill asks questions by design and a run that stops on one measures nothing.
    /// </summary>
    public static string Prompt(string skill, JsonObject eval, string run, string repo, bool withSkill)
    {
        var heading = withSkill
            ? $"Skill to follow: {Path.Combine(SkillDirectory(skill), "SKILL.md").Replace('\\', '/')}\nRead it first, and follow it.\n"
            : "No skill applies here. Work it out yourself.\n";

        return $"""
            {heading}
            Target repository: {repo.Replace('\\', '/')}
            Scratch directory: {Path.Combine(run, "scratch").Replace('\\', '/')}

            Task, in the words of the repository's owner:

            > {eval["prompt"]!.ToString().Replace("\n", "\n> ", StringComparison.Ordinal)}

            How this run works, since there is nobody to talk to:

            - The target repository is the user's repository. Every path you pass to a tool is absolute, and the
              scratch directory above is the one to work from — never run anything from inside the repository
              except the builds that have to happen there.
            - You will reach points that call for asking the user. Do not stop. Write the question and the options
              you would have offered to {Path.Combine(run, "outputs", "questions.md").Replace('\\', '/')}, pick the
              option that is recommended (or the most defensible one when nothing is recommended), record which one
              you picked and why, and carry on. Answering yourself is what keeps the run comparable; guessing
              silently is not, so the file has to show every question.
            - Do not commit anything. The repository is a git repository so that URL resolution works, not so that
              the run ends in a commit.
            - When you are finished, write the report you would have shown the user to
              {Path.Combine(run, "outputs", "report.md").Replace('\\', '/')}.
            """;
    }

    private static void Git(string repo, params string[] arguments)
    {
        var (exit, stdout, stderr) = Run("git", arguments, repo);
        if (exit != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed:\n{stdout}\n{stderr}");
        }
    }

    private static (int Exit, string Stdout, string Stderr) Run(
        string fileName, IEnumerable<string> arguments, string workingDirectory, params (string Key, string Value)[] environment)
    {
        var info = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var a in arguments)
        {
            info.ArgumentList.Add(a);
        }

        foreach (var (key, value) in environment)
        {
            info.Environment[key] = value;
        }

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException($"could not start {fileName}");

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        return (process.ExitCode, stdout.Result, stderr.Result);
    }

    private static string ReadSdkPin()
    {
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(RepoRoot, "global.json")))!;
        var sdk = json["sdk"]?.DeepClone()
            ?? throw new InvalidOperationException("global.json does not pin an SDK.");
        return new JsonObject { ["sdk"] = sdk }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string ThisFile([CallerFilePath] string path = "") => path;
}
