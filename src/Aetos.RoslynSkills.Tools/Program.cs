using System;
using System.CommandLine;
using System.Linq;

using Aetos.RoslynSkills.Tools.AddDiagnostic;
using Aetos.RoslynSkills.Tools.Internal;

namespace Aetos.RoslynSkills.Tools;

internal static class Program
{
    public static int Main(string[] args)
    {
        // No arguments at all is someone looking for the command list, not a mistake worth reporting.
        if (args.Length == 0)
        {
            args = ["--help"];
        }

        var parse = CreateRootCommand().Parse(args);
        if (parse.Errors.Count > 0)
        {
            return Json.Fail(string.Join(" ", parse.Errors.Select(e => e.Message)),
                "Run the command with --help to see the options it accepts.");
        }

        try
        {
            // The default exception handler is switched off so that the handler below is the one that runs: left on,
            // System.CommandLine catches everything itself, prints "Unhandled exception:" and the trace to stderr,
            // and returns 1 — indistinguishable from an expected failure, with nothing on stdout to parse.
            return parse.Invoke(new InvocationConfiguration { EnableDefaultExceptionHandler = false });
        }
#pragma warning disable CA1031 // A top-level handler is the one place every exception type has to be caught.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // Every caller is a skill that parses stdout, so a bug has to arrive as JSON too: a .NET stack trace
            // on stderr with an empty stdout is indistinguishable from a command that printed nothing.
            return Json.Crash(ex);
        }
    }

    internal static RootCommand CreateRootCommand()
    {
        // One subcommand group per skill, so a new skill adds a group here instead of another package.
        var root = new RootCommand(
            "Helper commands for the roslyn-skills plugin's skills. Every command prints JSON on stdout, "
            + "including for expected failures ({\"error\": ..., \"hint\": ...} with exit code 1) and for bugs in "
            + "the tool itself (the same shape plus \"unexpected\": true, with exit code 2).");
        root.Subcommands.Add(AddDiagnosticCommand.Create());
        return root;
    }
}
