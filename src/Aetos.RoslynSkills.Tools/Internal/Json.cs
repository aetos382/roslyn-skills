using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aetos.RoslynSkills.Tools.Internal;

internal static class Json
{
    public static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    public static JsonArray Array(IEnumerable<string> items)
    {
        var a = new JsonArray();

        foreach (var i in items)
        {
            a.Add(i);
        }

        return a;
    }

    public static JsonArray Array(IEnumerable<JsonNode?> items)
    {
        var a = new JsonArray();
        foreach (var i in items)
        {
            a.Add(i);
        }

        return a;
    }

    public static void Print(JsonNode node)
    {
        Console.WriteLine(node.ToJsonString(Indented));
    }

    /// <summary>
    /// Reports an expected failure as JSON and yields exit code 1, so callers see the same shape they get on
    /// success. A bug in the tool goes through <see cref="Crash"/> instead and gets its own exit code.
    /// </summary>
    public static int Fail(string error, string? hint = null)
    {
        Print(new JsonObject { ["error"] = error, ["hint"] = hint });
        return 1;
    }

    /// <summary>
    /// Reports a bug in the tool as JSON on stdout with exit code 2. A caller parses stdout either way, and the
    /// exit code tells the two apart: 1 means the tool rejected the request and the hint says what to change,
    /// 2 means nothing about the request can be concluded and the trace should be reported.
    /// </summary>
    public static int Crash(Exception ex)
    {
        Print(new JsonObject
        {
            ["error"] = ex.Message,
            ["hint"] = "This is a bug in the tool rather than a problem with the arguments. Report it with the stack trace below; retrying the same command will not help.",
            ["unexpected"] = true,
            ["exception"] = ex.GetType().FullName,
            ["stackTrace"] = ex.ToString(),
        });
        return 2;
    }
}
