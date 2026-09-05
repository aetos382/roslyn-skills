using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

/// <summary>Building a fixture repository, running the tool and the compiler over it, and writing the prompt the
/// agent is handed.</summary>
internal static partial class Harness
{
    [GeneratedRegex(@"Aetos\.RoslynSkills\.Tools@(?<version>[0-9][^\s""]*)")]
    private static partial Regex ToolPinPattern { get; }

    // MSBuild prints a warning once per project that reports it and repeats it across targets, so the raw count
    // means nothing; the whole text from "warning" onwards is what makes two lines the same warning or not.
    // The code is not always letters-then-digits either: the SDK reports EnableGenerateDocumentationFile under
    // that name, and a warning the counter cannot see is a warning the build assertion would never notice.
    [GeneratedRegex(@"warning (?<code>[A-Za-z][A-Za-z0-9]*):.*$", RegexOptions.Multiline)]
    private static partial Regex WarningPattern { get; }

    /// <summary>The repository this file lives in, found from the compiler's own view of where it is.</summary>
    public static string RepoRoot { get; } = Directory.GetParent(ThisFile())!.Parent!.FullName;

    public static string SkillDirectory { get; } =
        Path.Combine(RepoRoot, "plugin", "skills", "add-diagnostic");

    public static string DefaultOutRoot { get; } =
        Path.Combine(Path.GetTempPath(), "roslyn-skills-evals");

    /// <summary>
    /// The tool version SKILL.md tells the agent to run. Read from the skill rather than pinned here, so an eval
    /// never silently measures a different release than the one the skill ships with.
    /// </summary>
    public static string ToolPin { get; } = ReadToolPin();

    public static IEnumerable<JsonObject> Evals()
    {
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(RepoRoot, "evals", "evals.json")))!;
        return json["evals"]!.AsArray().Select(e => e!.AsObject());
    }

    public static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
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

    public static string FindConventions(string repo)
    {
        // Run it from outside the repository, the way the skill insists on: inside it, the fixture's own
        // NuGet.config and any global.json above it start deciding what the tool runs as.
        var scratch = Directory.CreateTempSubdirectory("roslyn-skills-eval-").FullName;
        try
        {
            var (exit, stdout, stderr) = Run(
                "dotnet",
                ["tool", "exec", $"Aetos.RoslynSkills.Tools@{ToolPin}", "--", "add-diagnostic", "find-conventions", "--path", repo, "--summary"],
                scratch);

            if (exit != 0 || stdout.Length == 0)
            {
                throw new InvalidOperationException($"find-conventions failed (exit {exit}):\n{stdout}\n{stderr}");
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
        var counts = WarningPattern.Matches(log)
            .Select(m => (Code: m.Groups["code"].Value, Text: m.Value))
            .Distinct()
            .GroupBy(w => w.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        return (exit == 0, counts, log);
    }

    /// <summary>
    /// The task the agent is handed. It has to say how to behave without a human in front of it, because the
    /// skill asks questions by design and a run that stops on one measures nothing.
    /// </summary>
    public static string Prompt(JsonObject eval, string run, string repo, bool withSkill)
    {
        var skill = withSkill
            ? $"Skill to follow: {Path.Combine(SkillDirectory, "SKILL.md").Replace('\\', '/')}\nRead it first, and follow it.\n"
            : "No skill applies here. Work it out yourself.\n";

        return $"""
            {skill}
            Target repository: {repo.Replace('\\', '/')}
            Scratch directory: {Path.Combine(run, "scratch").Replace('\\', '/')}

            Task, in the words of the repository's owner:

            > {eval["prompt"]!.ToString().Replace("\n", "\n> ")}

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

    private static string ReadToolPin()
    {
        var text = File.ReadAllText(Path.Combine(SkillDirectory, "SKILL.md"));
        var match = ToolPinPattern.Match(text);
        return match.Success
            ? match.Groups["version"].Value
            : throw new InvalidOperationException("SKILL.md does not name a pinned tool version.");
    }

    private static string ThisFile([CallerFilePath] string path = "") => path;
}
