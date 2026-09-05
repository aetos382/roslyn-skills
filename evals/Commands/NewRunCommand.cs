using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aetos.RoslynSkills.Evals.Commands;

/// <summary>
/// Builds the repository one eval runs against, records what it looks like before the agent touches it, and
/// writes the prompt to hand an agent.
/// </summary>
internal static class NewRunCommand
{
    public static Command Create()
    {
        var eval = new Argument<string>("eval")
        {
            Description = "The eval to run, as its id or as <skill>:<id> when two skills use the same one.",
        };
        var outRoot = new Option<string?>("--out")
        {
            Description = "Where to create the run directory. Defaults to Temp/evals in this repository.",
        };
        var noBuild = new Option<bool>("--no-build")
        {
            Description = "Skip the baseline build. The run then has nothing to compare against, so the 'build' assertion fails.",
        };

        var command = new Command("new-run", "Creates a run directory for one eval and prints the prompt to hand an agent.");
        command.Arguments.Add(eval);
        command.Options.Add(outRoot);
        command.Options.Add(noBuild);
        command.SetAction(parse => Run(
            parse.GetValue(eval)!,
            parse.GetValue(outRoot) ?? Harness.DefaultOutRoot,
            build: !parse.GetValue(noBuild)));
        return command;
    }

    private static int Run(string reference, string outRoot, bool build)
    {
        var (skillName, eval) = Harness.Resolve(reference);
        var skill = Skills.Get(skillName);
        var id = eval["id"]!.ToString();

        var fixtureName = eval["fixture"]!.ToString();
        var fixture = skill.Fixtures.TryGetValue(fixtureName, out var make)
            ? make()
            : throw new KeyNotFoundException($"{skillName} has no fixture named '{fixtureName}'.");

        // Runs are grouped by skill so that one skill's noise never buries another's.
        var run = Path.Combine(outRoot, skillName, $"{id}-{DateTime.Now:yyyyMMdd-HHmmss}");
        var repo = Path.Combine(run, "fixture");
        Directory.CreateDirectory(Path.Combine(run, "outputs"));
        Directory.CreateDirectory(Path.Combine(run, "scratch"));
        Directory.CreateDirectory(Path.Combine(run, "baseline"));

        Harness.Materialize(fixture, repo);
        Harness.InitGit(repo, fixture.Remote);

        if (Harness.Scan(skillName, repo) is { } scan)
        {
            File.WriteAllText(Path.Combine(run, "baseline", "scan.json"), scan);
        }

        var warnings = new JsonObject();
        if (build)
        {
            var (ok, counts, log) = Harness.Build(repo, fixture.BuildProject);
            File.WriteAllText(Path.Combine(run, "baseline", "build.log"), log);
            if (!ok)
            {
                // Better to hear this now than after spending an agent run: a fixture that starts broken cannot
                // say anything about what the agent did to it.
                throw new InvalidOperationException(
                    $"the '{fixture.Name}' fixture does not build; see {Path.Combine(run, "baseline", "build.log")}.");
            }

            foreach (var (code, n) in counts)
            {
                warnings[code] = n;
            }
        }

        var meta = new JsonObject
        {
            ["skill"] = skillName,
            ["evalId"] = id,
            ["fixture"] = fixture.Name,
            ["buildProject"] = fixture.BuildProject,
            ["createdAt"] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
            ["baselineBuilt"] = build,
            ["baselineWarnings"] = warnings,
        };
        File.WriteAllText(
            Path.Combine(run, "run.json"),
            meta.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        File.WriteAllText(Path.Combine(run, "prompt.md"), Harness.Prompt(skillName, eval, run, repo, withSkill: true));
        File.WriteAllText(Path.Combine(run, "prompt-baseline.md"), Harness.Prompt(skillName, eval, run, repo, withSkill: false));

        Console.WriteLine($"skill     {skillName}");
        Console.WriteLine($"run       {run}");
        Console.WriteLine($"repo      {repo}");
        Console.WriteLine($"prompt    {Path.Combine(run, "prompt.md")}");
        Console.WriteLine($"baseline  {Path.Combine(run, "prompt-baseline.md")}");
        Console.WriteLine();
        Console.WriteLine($"When the agent is done:  dotnet run --project evals -- grade \"{run}\"");
        return 0;
    }
}
