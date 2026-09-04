using System;
using System.IO;
using System.Text.Json.Nodes;

namespace Aetos.RoslynSkills.Tools.Tests;

/// <summary>
/// Runs the tool the way the skill does — a command line in, JSON and an exit code out. Console.Out is redirected,
/// so a test class using this carries <c>[DoNotParallelize]</c>.
/// </summary>
internal static class Tool
{
    public static (int ExitCode, string Output) Run(params string[] args)
    {
        var original = Console.Out;
        using var buffer = new StringWriter();
        Console.SetOut(buffer);
        try
        {
            return (Program.Main(args), buffer.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    /// <summary>The command's output as the object the skill reads, asserting the exit code first.</summary>
    public static JsonObject Json(int expectedExitCode, params string[] args)
    {
        var (exitCode, output) = Run(args);

        Assert.AreEqual(expectedExitCode, exitCode, output);
        return JsonNode.Parse(output)!.AsObject();
    }

    /// <summary>The output of a command that reports one entry per file, asserting the exit code first.</summary>
    public static JsonArray Report(int expectedExitCode, params string[] args)
    {
        var (exitCode, output) = Run(args);

        Assert.AreEqual(expectedExitCode, exitCode, output);
        return JsonNode.Parse(output)!.AsArray();
    }
}
