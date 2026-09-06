using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Completions;
using System.Globalization;
using System.IO;
using System.Linq;
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
        var skill = new Option<string>("--skill")
        {
            Description = "The skill whose evals to run, named as its directory under plugin/skills.",
            Required = true,
        };

        skill.AcceptOnlyFromAmong([.. Skills.All.Keys]);

        var id = new Option<string>("--id")
        {
            Description = "The eval's id within that skill.",
            Required = true,
        };

        // Which ids exist is known only once the skill's evals.json has been read, so this cannot be the static
        // AcceptOnlyFromAmong that --skill gets. A completion source runs against the line as it has been typed
        // so far, which is enough: it answers from whatever --skill is already on it.
        id.CompletionSources.Add(context => EvalIds(context.ParseResult.GetValue(skill)));

        var outRoot = new Option<string?>("--out")
        {
            Description = "Where to create the run directory. Defaults to Temp/evals in this repository.",
        };

        var noBuild = new Option<bool>("--no-build")
        {
            Description = "Skip the baseline build. The run then has nothing to compare against, so the 'build' assertion fails.",
        };

        var command = new Command("new-run", "Creates a run directory for one eval and prints the prompt to hand an agent.");
        command.Options.Add(skill);
        command.Options.Add(id);
        command.Options.Add(outRoot);
        command.Options.Add(noBuild);
        command.SetAction(parse => Run(
            parse.GetValue(skill)!,
            parse.GetValue(id)!,
            // Made absolute here, because the prompt tells the agent every path in it is: a relative --out would
            // otherwise reach the agent as a path relative to a directory it is not working in.
            Path.GetFullPath(parse.GetValue(outRoot) ?? Harness.DefaultOutRoot),
            build: !parse.GetValue(noBuild)));

        return command;
    }

    // List rather than IEnumerable so the reads below happen inside the try: a deferred sequence would throw
    // at the caller instead.
    private static List<CompletionItem> EvalIds(string? skillName)
    {
        // Half-typed input is the normal case here: --skill may be absent, misspelled, or name a skill whose
        // evals.json is being edited right now. Completion has nowhere to report any of that, so it stays quiet.
        if (skillName is null || !Skills.All.ContainsKey(skillName))
        {
            return [];
        }

        try
        {
            return Harness.Evals(skillName).Select(e => new CompletionItem(e["id"]!.ToString())).ToList();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return [];
        }
    }

    private static int Run(string skillName, string id, string outRoot, bool build)
    {
        var skill = Skills.Get(skillName);
        var eval = Harness.Resolve(skillName, id);

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
