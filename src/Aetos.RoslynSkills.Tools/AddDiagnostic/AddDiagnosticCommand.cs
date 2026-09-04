using System.CommandLine;
using System.CommandLine.Help;

namespace Aetos.RoslynSkills.Tools.AddDiagnostic;

internal static class AddDiagnosticCommand
{
    public static Command Create()
    {
        var command = new Command(
            "add-diagnostic",
            "Helpers for the add-diagnostic skill: repository convention detection, ID allocation, "
            + "ordered resx insertion, and documentation URL resolution.");
        command.Subcommands.Add(FindConventionsCommand.Create());
        command.Subcommands.Add(NextIdCommand.Create());
        command.Subcommands.Add(AddResxEntriesCommand.Create());
        command.Subcommands.Add(DocUrlCommand.Create());
        // Naming the group alone is a lookup, not a mistake, so list the subcommands as the root does.
        command.SetAction(new HelpAction().Invoke);
        return command;
    }
}
