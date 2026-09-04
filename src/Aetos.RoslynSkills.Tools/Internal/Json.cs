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
    /// Reports an expected failure as JSON and yields the exit code, so callers see the same shape they
    /// get on success. Unexpected exceptions are deliberately left to crash: those are bugs, not results.
    /// </summary>
    public static int Fail(string error, string? hint = null)
    {
        Print(new JsonObject { ["error"] = error, ["hint"] = hint });
        return 1;
    }
}
