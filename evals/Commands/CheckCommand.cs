using System;
using System.CommandLine;

namespace Aetos.RoslynSkills.Evals.Commands;

/// <summary>Validates the evals.json files, so a typo in one is found before an agent run rather than after.</summary>
internal static class CheckCommand
{
    public static Command Create()
    {
        var command = new Command("check", "Checks every evals.json against what the harness can run.");
        command.SetAction(_ => Run());
        return command;
    }

    private static int Run()
    {
        var problems = EvalSet.Validate();
        foreach (var problem in problems)
        {
            Console.Error.WriteLine(problem);
        }

        Console.WriteLine(problems.Count == 0
            ? $"{EvalSet.Count()} evals across {Skills.All.Count} skill(s), all well formed"
            : $"{problems.Count} problem(s)");

        return problems.Count == 0 ? 0 : 1;
    }
}
