#!/usr/bin/env dotnet
#:include harness.cs
#:include assertions.cs
#:include fixtures.cs
#:include fixture-mature.cs
#:include fixture-greenfield.cs
#:include fixture-literal.cs

// The eval harness for the add-diagnostic skill.
//
//   dotnet run evals/eval.cs -- list
//   dotnet run evals/eval.cs -- check
//   dotnet run evals/eval.cs -- new-run <eval-id> [--out <dir>] [--no-build]
//   dotnet run evals/eval.cs -- grade <run-dir>
//   dotnet run evals/eval.cs -- report [--out <dir>]
//
// new-run builds a throwaway repository, records what it looks like before the agent touches it, and writes the
// prompt to hand an agent. grade re-runs find-conventions over what the agent left behind and checks the
// assertions in evals.json against it. See README.md for the loop the two halves belong to.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

var command = args.Length > 0 ? args[0] : "help";
var rest = args.Skip(1).ToArray();

// A CLI's entry point is exactly where every exception belongs: the caller is a shell, and a stack trace on
// stderr helps nobody running an eval.
#pragma warning disable CA1031
try
{
    return command switch
    {
        "list" => List(),
        "check" => Check(),
        "new-run" => NewRun(rest),
        "grade" => Grade(rest),
        "report" => Report(rest),
        _ => Help(),
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}
#pragma warning restore CA1031

static int Help()
{
    Console.WriteLine("""
        Usage:
          dotnet run evals/eval.cs -- list
          dotnet run evals/eval.cs -- check
          dotnet run evals/eval.cs -- new-run <eval-id> [--out <dir>] [--no-build]
          dotnet run evals/eval.cs -- grade <run-dir>
          dotnet run evals/eval.cs -- report [--out <dir>]
        """);
    return 1;
}

static int List()
{
    foreach (var e in Harness.Evals())
    {
        Console.WriteLine($"{e["id"],-26} {e["fixture"],-12} {e["prompt"]!.ToString().Split('\n')[0]}");
    }

    return 0;
}

// evals.json is only read when a run starts or is graded, so a typo in it otherwise surfaces after an agent run
// rather than before one. This is the cheap way to find out first.
static int Check()
{
    string[] kinds =
    [
        "jsonContains", "jsonNotContains", "jsonCount", "jsonEquals",
        "fileExists", "fileMissing", "contains", "anyContains", "notContains",
        "resxEntryCount", "resxParity", "noLeftovers", "build",
    ];

    var problems = new List<string>();
    var ids = new HashSet<string>(StringComparer.Ordinal);

    foreach (var eval in Harness.Evals())
    {
        var id = eval["id"]?.ToString();
        if (id is null || !ids.Add(id))
        {
            problems.Add($"an eval has a missing or duplicated id: {id ?? "(none)"}");
            continue;
        }

        if (eval["prompt"] is null || eval["expected_output"] is null)
        {
            problems.Add($"{id}: prompt and expected_output are both required");
        }

        var fixture = eval["fixture"]?.ToString();
        if (fixture is null || !Fixtures.Names.Contains(fixture))
        {
            problems.Add($"{id}: unknown fixture '{fixture}'");
        }

        foreach (var assertion in eval["assertions"]?.AsArray() ?? [])
        {
            var a = assertion!.AsObject();
            var kind = a["kind"]?.ToString();
            if (kind is null || !kinds.Contains(kind, StringComparer.Ordinal))
            {
                problems.Add($"{id}: unknown assertion kind '{kind}'");
            }

            if (a["text"] is null)
            {
                problems.Add($"{id}: an assertion has no text, so its result would be unreadable");
            }

            if (a["pattern"]?.ToString() is { } pattern)
            {
                try
                {
                    _ = new Regex(Assertions.StripPlaceholders(pattern));
                }
                catch (ArgumentException ex)
                {
                    problems.Add($"{id}: /{pattern}/ is not a valid regular expression: {ex.Message}");
                }
            }
        }
    }

    foreach (var problem in problems)
    {
        Console.Error.WriteLine(problem);
    }

    Console.WriteLine(problems.Count == 0
        ? $"{ids.Count} evals, all well formed"
        : $"{problems.Count} problem(s)");
    return problems.Count == 0 ? 0 : 1;
}

static int NewRun(string[] args)
{
    if (args.Length == 0)
    {
        throw new ArgumentException("new-run needs an eval id; run 'list' to see them.");
    }

    var id = args[0];
    var eval = Harness.Evals().FirstOrDefault(e => e["id"]!.ToString() == id)
        ?? throw new KeyNotFoundException($"unknown eval '{id}'; run 'list' to see them.");
    var outRoot = Harness.Option(args, "--out") ?? Harness.DefaultOutRoot;
    var build = !args.Contains("--no-build");

    var fixture = Fixtures.Get(eval["fixture"]!.ToString());
    var run = Path.Combine(outRoot, $"{id}-{DateTime.Now:yyyyMMdd-HHmmss}");
    var repo = Path.Combine(run, "fixture");
    Directory.CreateDirectory(Path.Combine(run, "outputs"));
    Directory.CreateDirectory(Path.Combine(run, "scratch"));
    Directory.CreateDirectory(Path.Combine(run, "baseline"));

    Harness.Materialize(fixture, repo);
    Harness.InitGit(repo, fixture.Remote);

    var conventions = Harness.FindConventions(repo);
    File.WriteAllText(Path.Combine(run, "baseline", "conventions.json"), conventions);

    var warnings = new JsonObject();
    if (build)
    {
        var (ok, counts, log) = Harness.Build(repo, fixture.BuildProject);
        File.WriteAllText(Path.Combine(run, "baseline", "build.log"), log);
        if (!ok)
        {
            throw new InvalidOperationException(
                $"the '{fixture.Name}' fixture does not build; see {Path.Combine(run, "baseline", "build.log")}. " +
                "A fixture that starts broken cannot tell you anything about the agent's edits.");
        }

        foreach (var (code, n) in counts)
        {
            warnings[code] = n;
        }
    }

    var meta = new JsonObject
    {
        ["evalId"] = id,
        ["fixture"] = fixture.Name,
        ["buildProject"] = fixture.BuildProject,
        ["createdAt"] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
        ["baselineBuilt"] = build,
        ["baselineWarnings"] = warnings,
    };
    File.WriteAllText(Path.Combine(run, "run.json"), meta.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

    File.WriteAllText(Path.Combine(run, "prompt.md"), Harness.Prompt(eval, run, repo, withSkill: true));
    File.WriteAllText(Path.Combine(run, "prompt-baseline.md"), Harness.Prompt(eval, run, repo, withSkill: false));

    Console.WriteLine($"run       {run}");
    Console.WriteLine($"repo      {repo}");
    Console.WriteLine($"prompt    {Path.Combine(run, "prompt.md")}");
    Console.WriteLine($"baseline  {Path.Combine(run, "prompt-baseline.md")}");
    Console.WriteLine();
    Console.WriteLine($"When the agent is done:  dotnet run evals/eval.cs -- grade \"{run}\"");
    return 0;
}

static int Grade(string[] args)
{
    if (args.Length == 0)
    {
        throw new ArgumentException("grade needs a run directory.");
    }

    var run = Path.GetFullPath(args[0]);
    var repo = Path.Combine(run, "fixture");
    var meta = JsonNode.Parse(File.ReadAllText(Path.Combine(run, "run.json")))!.AsObject();
    var id = meta["evalId"]!.ToString();
    var eval = Harness.Evals().First(e => e["id"]!.ToString() == id);

    var conventionsText = Harness.FindConventions(repo);
    File.WriteAllText(Path.Combine(run, "conventions.json"), conventionsText);
    var conventions = JsonNode.Parse(conventionsText)!.AsObject();

    var context = new GradingContext(run, repo, conventions, meta);
    var results = new JsonArray();
    var passed = 0;
    var total = 0;

    foreach (var assertion in eval["assertions"]!.AsArray())
    {
        var a = assertion!.AsObject();
        var text = a["text"]!.ToString();
        string evidence;
        bool ok;
        try
        {
            (ok, evidence) = Assertions.Evaluate(a, context);
        }
        // A check that throws is a defect in the check, and the run is worth more with the other checks graded
        // than abandoned on the first bad one.
#pragma warning disable CA1031
        catch (Exception ex)
        {
            (ok, evidence) = (false, $"the check itself failed: {ex.Message}");
        }
#pragma warning restore CA1031

        total++;
        if (ok)
        {
            passed++;
        }

        results.Add((JsonNode)new JsonObject { ["text"] = text, ["passed"] = ok, ["evidence"] = evidence });
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {text}");
        if (!ok)
        {
            Console.WriteLine($"      {evidence}");
        }
    }

    var grading = new JsonObject
    {
        ["eval_id"] = id,
        ["run"] = run,
        ["pass_rate"] = total == 0 ? 0 : Math.Round((double)passed / total, 3),
        ["passed"] = passed,
        ["total"] = total,
        ["expectations"] = results,
    };
    File.WriteAllText(Path.Combine(run, "grading.json"), grading.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

    Console.WriteLine();
    Console.WriteLine($"{passed}/{total} passed  ->  {Path.Combine(run, "grading.json")}");
    return passed == total ? 0 : 1;
}

static int Report(string[] args)
{
    var outRoot = Harness.Option(args, "--out") ?? Harness.DefaultOutRoot;
    if (!Directory.Exists(outRoot))
    {
        Console.WriteLine($"no runs under {outRoot}");
        return 0;
    }

    var rows = new List<(string Run, string Eval, int Passed, int Total)>();
    foreach (var dir in Directory.EnumerateDirectories(outRoot).OrderBy(d => d, StringComparer.Ordinal))
    {
        var file = Path.Combine(dir, "grading.json");
        if (!File.Exists(file))
        {
            continue;
        }

        var g = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        rows.Add((Path.GetFileName(dir), g["eval_id"]!.ToString(), (int)g["passed"]!, (int)g["total"]!));
    }

    if (rows.Count == 0)
    {
        Console.WriteLine($"no graded runs under {outRoot}");
        return 0;
    }

    Console.WriteLine("| Run | Eval | Passed | Total |");
    Console.WriteLine("|-----|------|--------|-------|");
    foreach (var (r, e, p, t) in rows)
    {
        Console.WriteLine($"| {r} | {e} | {p} | {t} |");
    }

    Console.WriteLine();
    Console.WriteLine($"total {rows.Sum(r => r.Passed)}/{rows.Sum(r => r.Total)}");
    return 0;
}

/// <summary>Everything one assertion needs to look at: the repository, the run directory around it, the
/// convention scan taken after the agent finished, and what the run looked like before it started.</summary>
internal sealed record GradingContext(string Run, string Repo, JsonObject Conventions, JsonObject Meta);
