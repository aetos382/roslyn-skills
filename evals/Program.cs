using System;
using System.CommandLine;
using System.Linq;

using Aetos.RoslynSkills.Evals.Commands;

namespace Aetos.RoslynSkills.Evals;

/// <summary>
/// The eval harness for this repository's skills: it builds a throwaway repository, hands an agent a task in it,
/// and grades what the agent left behind.
///
/// Everything here is skill-agnostic. A skill's prompts, its assertions and the repositories they run against
/// live under <c>evals/&lt;skill&gt;/</c>, and a skill joins in by adding such a directory plus a row in
/// <see cref="Skills"/>. See README.md for the loop these commands are two halves of.
/// </summary>
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
            Console.Error.WriteLine(string.Join(" ", parse.Errors.Select(e => e.Message)));
            Console.Error.WriteLine("Run the command with --help to see what it accepts.");
            return 1;
        }

        try
        {
            // The default exception handler is switched off so the handler below is the one that runs: left on,
            // System.CommandLine prints "Unhandled exception:" and the trace, which buries the one line that says
            // what went wrong.
            return parse.Invoke(new InvocationConfiguration { EnableDefaultExceptionHandler = false });
        }
#pragma warning disable CA1031 // A CLI's entry point is the one place every exception type has to be caught.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    internal static RootCommand CreateRootCommand()
    {
        var root = new RootCommand(
            "Runs and grades the end-to-end evals for this repository's skills. new-run builds a throwaway "
            + "repository and writes the prompt to hand an agent; grade reads back what the agent left behind.");
        root.Subcommands.Add(ListCommand.Create());
        root.Subcommands.Add(CheckCommand.Create());
        root.Subcommands.Add(NewRunCommand.Create());
        root.Subcommands.Add(GradeCommand.Create());
        root.Subcommands.Add(ReportCommand.Create());
        return root;
    }
}
