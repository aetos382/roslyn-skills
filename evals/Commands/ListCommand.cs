using System;
using System.CommandLine;

namespace Aetos.RoslynSkills.Evals.Commands;

/// <summary>Every eval of every skill, so that the ids the other commands take are discoverable.</summary>
internal static class ListCommand
{
    public static Command Create()
    {
        var command = new Command("list", "Lists every eval, with the fixture it runs against.");
        command.SetAction(_ => Run());
        return command;
    }

    private static int Run()
    {
        foreach (var (skill, eval) in Harness.Evals())
        {
            var firstLine = eval["prompt"]!.ToString().Split('\n')[0];
            Console.WriteLine($"{skill,-16} {eval["id"],-26} {eval["fixture"],-12} {firstLine}");
        }

        return 0;
    }
}
