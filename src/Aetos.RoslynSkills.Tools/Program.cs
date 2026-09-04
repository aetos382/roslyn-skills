using System.CommandLine;

using Aetos.RoslynSkills.Tools.AddDiagnostic;

namespace Aetos.RoslynSkills.Tools;

internal static class Program
{
    public static int Main(string[] args)
    {
        // No arguments at all is someone looking for the command list, not a mistake worth reporting.
        if (args.Length == 0) args = ["--help"];

        var parse = CreateRootCommand().Parse(args);
        if (parse.Errors.Count > 0)
            return Json.Fail(string.Join(" ", parse.Errors.Select(e => e.Message)),
                "Run the command with --help to see the options it accepts.");
        return parse.Invoke();
    }

    internal static RootCommand CreateRootCommand()
    {
        // One subcommand group per skill, so a new skill adds a group here instead of another package.
        var root = new RootCommand(
            "Helper commands for the roslyn-skills plugin's skills. Every command prints JSON on stdout, "
            + "including for expected failures ({\"error\": ..., \"hint\": ...} with exit code 1).");
        root.Subcommands.Add(AddDiagnosticCommand.Create());
        return root;
    }
}
