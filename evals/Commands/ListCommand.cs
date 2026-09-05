using System.Collections.Generic;
using System.CommandLine;

using Spectre.Console;
using Spectre.Console.Rendering;

namespace Aetos.RoslynSkills.Evals.Commands;

/// <summary>Every eval of every skill, so that the arguments the other commands take are discoverable.</summary>
internal static class ListCommand
{
    public static Command Create()
    {
        var command = new Command("list", "Lists every eval, with the fixture it runs against and what it asks for.");
        command.SetAction(_ => Run());
        return command;
    }

    private static int Run()
    {
        var blocks = new List<IRenderable>();

        foreach (var (skill, eval) in Harness.Evals())
        {
            // One labelled field per line rather than a table of columns: a prompt is a sentence, and a sentence
            // in a column is either truncated or squeezed into a column's share of the width. As the second
            // column of a two-column grid it gets the rest of the terminal, and the lines it wraps onto line up
            // under where it started, the way a stack trace continues under the frame it belongs to.
            var record = new Grid();
            record.AddColumn(new GridColumn().NoWrap().PadRight(1));
            record.AddColumn();

            record.AddRow(Label("Skill"), Value(skill));
            record.AddRow(Label("Id"), Value(eval["id"]!.ToString()));
            record.AddRow(Label("Fixture"), Value(eval["fixture"]!.ToString()));
            record.AddRow(Label("Prompt"), Value(eval["prompt"]!.ToString()));

            blocks.Add(record);
            blocks.Add(Text.Empty);
        }

        AnsiConsole.Write(new Rows(blocks));
        return 0;
    }

    // Padded to a common width so the colons line up, which the grid alone would not do: it aligns the values,
    // and "Skill:" against "Fixture:" would leave the colons ragged.
    private static Text Label(string label) => new($"{label,-8}:");

    // Text rather than Markup: an eval's own words are content, and a '[' in them is a bracket rather than the
    // start of a style tag.
    private static Text Value(string value) => new(value);
}
