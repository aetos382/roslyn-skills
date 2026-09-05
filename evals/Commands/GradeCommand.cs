using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aetos.RoslynSkills.Evals.Commands;

/// <summary>Reads back what the agent left in a run directory and checks it against that eval's assertions.</summary>
internal static class GradeCommand
{
    public static Command Create()
    {
        var directory = new Argument<string>("run-directory")
        {
            Description = "A run directory created by new-run.",
        };

        var command = new Command("grade", "Grades one finished run and writes grading.json beside it.");
        command.Arguments.Add(directory);
        command.SetAction(parse => Run(parse.GetValue(directory)!));
        return command;
    }

    private static int Run(string runDirectory)
    {
        var run = Path.GetFullPath(runDirectory);
        var repo = Path.Combine(run, "fixture");
        var meta = JsonNode.Parse(File.ReadAllText(Path.Combine(run, "run.json")))!.AsObject();
        var skillName = meta["skill"]!.ToString();
        var id = meta["evalId"]!.ToString();
        var eval = Harness.Evals(skillName).First(e => e["id"]!.ToString() == id);

        var scan = new JsonObject();
        if (Harness.Scan(skillName, repo) is { } text)
        {
            File.WriteAllText(Path.Combine(run, "scan.json"), text);
            scan = JsonNode.Parse(text)!.AsObject();
        }

        var context = new GradingContext(run, repo, scan, meta);
        var results = new JsonArray();
        var passed = 0;
        var total = 0;

        foreach (var assertion in eval["assertions"]!.AsArray())
        {
            var a = assertion!.AsObject();
            var description = a["text"]!.ToString();
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

            results.Add((JsonNode)new JsonObject
            {
                ["text"] = description,
                ["passed"] = ok,
                ["evidence"] = evidence,
            });

            Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {description}");
            if (!ok)
            {
                Console.WriteLine($"      {evidence}");
            }
        }

        // The field names are the ones the skill-creator's eval viewer expects, so a run directory can be dropped
        // into its workspace layout when the side-by-side output review is wanted too.
        var grading = new JsonObject
        {
            ["skill"] = skillName,
            ["eval_id"] = id,
            ["run"] = run,
            ["pass_rate"] = total == 0 ? 0 : Math.Round((double)passed / total, 3),
            ["passed"] = passed,
            ["total"] = total,
            ["expectations"] = results,
        };
        File.WriteAllText(
            Path.Combine(run, "grading.json"),
            grading.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine();
        Console.WriteLine($"{passed}/{total} passed  ->  {Path.Combine(run, "grading.json")}");
        return passed == total ? 0 : 1;
    }
}
