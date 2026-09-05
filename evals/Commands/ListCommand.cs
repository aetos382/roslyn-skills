using System.Collections.Generic;
using System.CommandLine;
using System.Linq;

using Spectre.Console;
using Spectre.Console.Rendering;

namespace Aetos.RoslynSkills.Evals.Commands;

/// <summary>Every eval of every skill, so that the arguments the other commands take are discoverable.</summary>
internal static class ListCommand
{
    private const int LabelWidth = 8;

    public static Command Create()
    {
        var skill = new Option<string?>("--skill")
        {
            Description = "Only this skill's evals. Every skill's when omitted.",
        };

        var command = new Command("list", "Lists the evals and what each one is meant to guarantee.");
        command.Options.Add(skill);
        command.SetAction(parse => Run(parse.GetValue(skill)));
        return command;
    }

    private static int Run(string? only)
    {
        // An unknown name is left to Skills.Get, which answers with the ones that do exist.
        var evals = only is null
            ? Harness.Evals()
            : Harness.Evals(only).Select(e => (Skill: only, Eval: e));

        var blocks = new List<IRenderable>();

        foreach (var (skill, eval) in evals)
        {
            // One labelled field per line rather than a table of columns: a summary is a sentence, and a sentence
            // in a column is either truncated or squeezed into a column's share of the width. As the second
            // column of a two-column grid it gets the rest of the terminal, and the lines it wraps onto line up
            // under where it started, the way a stack trace continues under the frame it belongs to.
            var record = new Grid();
            record.AddColumn(new GridColumn().NoWrap().PadRight(1));
            record.AddColumn();

            record.AddRow(Label("Skill"), Value(skill));
            record.AddRow(Label("Id"), Value(eval["id"]!.ToString()));

            // What the eval is for, and the only thing here that is not an argument to another command. Reading
            // this listing is choosing which eval to run, so the question it has to answer is what an eval
            // establishes -- not which repository it does it in, and not what will be typed at the agent, both of
            // which the run itself carries. The long form of this is the eval's expected_output, which is for
            // reading a finished run rather than for picking one.
            record.AddRow(Label("Summary"), Value(eval["summary"]!.ToString()));

            blocks.Add(record);
            blocks.Add(Text.Empty);
        }

        AnsiConsole.Write(new Rows(blocks));
        return 0;
    }

    // Padded to a common width so the colons line up, which the grid alone would not do: it aligns the values,
    // and "Skill:" against "Fixture:" would leave the colons ragged.
    private static Text Label(string label) => new(label.PadRight(LabelWidth) + ":");

    // Text rather than Markup: an eval's own words are content, and a '[' in them is a bracket rather than the
    // start of a style tag.
    private static Text Value(string value) => new(value);
}
