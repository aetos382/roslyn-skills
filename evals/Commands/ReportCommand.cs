using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace Aetos.RoslynSkills.Evals.Commands;

/// <summary>Every graded run under one output root, as one table.</summary>
internal static class ReportCommand
{
    public static Command Create()
    {
        var outRoot = new Option<string?>("--out")
        {
            Description = "Where the runs are. Defaults to Temp/evals in this repository.",
        };

        var command = new Command("report", "Summarizes every graded run.");
        command.Options.Add(outRoot);
        command.SetAction(parse => Run(parse.GetValue(outRoot) ?? Harness.DefaultOutRoot));
        return command;
    }

    private static int Run(string outRoot)
    {
        var rows = new List<(string Skill, string Eval, string Run, int Passed, int Total)>();

        if (Directory.Exists(outRoot))
        {
            foreach (var file in Directory.EnumerateFiles(outRoot, "grading.json", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.Ordinal))
            {
                var g = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
                rows.Add((
                    g["skill"]?.ToString() ?? "(unknown)",
                    g["eval_id"]!.ToString(),
                    Path.GetFileName(Path.GetDirectoryName(file))!,
                    (int)g["passed"]!,
                    (int)g["total"]!));
            }
        }

        if (rows.Count == 0)
        {
            Console.WriteLine($"no graded runs under {outRoot}");
            return 0;
        }

        Console.WriteLine("| Skill | Eval | Run | Passed | Total |");
        Console.WriteLine("|-------|------|-----|--------|-------|");
        foreach (var (skill, eval, run, passed, total) in rows)
        {
            Console.WriteLine($"| {skill} | {eval} | {run} | {passed} | {total} |");
        }

        Console.WriteLine();
        Console.WriteLine($"total {rows.Sum(r => r.Passed)}/{rows.Sum(r => r.Total)}");
        return 0;
    }
}
