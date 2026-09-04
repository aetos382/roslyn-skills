using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

using Aetos.RoslynSkills.Tools.Internal;

namespace Aetos.RoslynSkills.Tools.AddDiagnostic;

/// <summary>
/// One project as the scan sees it. The properties are set by name rather than by position: there are too many
/// of them, and several are same-typed neighbours (Path / Directory / FullDirectory, LangVersion /
/// TargetFrameworks) that a positional call could swap without the compiler noticing.
/// </summary>
internal sealed record ProjectInfo
{
    public required string Name { get; init; }

    /// <summary>Repository-relative path of the .csproj, with forward slashes.</summary>
    public required string Path { get; init; }

    /// <summary>Repository-relative directory of the project, with forward slashes.</summary>
    public required string Directory { get; init; }

    /// <summary>Absolute directory of the project, in the platform's own form. Never reported as-is.</summary>
    public required string FullDirectory { get; init; }

    public required string Kind { get; init; }

    public required List<string> Roles { get; init; }

    public required Dictionary<string, List<(string Class, string File)>> Classes { get; init; }

    public required List<string> PackageReferences { get; init; }

    public required List<string> ProjectReferences { get; init; }

    public required List<string> LinkedCompileFiles { get; init; }

    public required Dictionary<string, string> ResxGenerators { get; init; }

    public required bool UsesResxSourceGenerator { get; init; }

    public required string? NeutralLanguage { get; init; }

    public required string? LangVersion { get; init; }

    public required string? TargetFrameworks { get; init; }

    /// <summary>
    /// Non-null when MSBuild could not evaluate the project: every property above is then a guess based on
    /// source files alone.
    /// </summary>
    public required string? EvaluationError { get; init; }

    public JsonObject ToJson()
    {
        var classes = new JsonObject();
        foreach (var (role, list) in this.Classes)
        {
            classes[role] = Json.Array(list.Select(c => (JsonNode?)new JsonObject { ["class"] = c.Class, ["file"] = c.File }));
        }

        var gens = new JsonObject();
        foreach (var (k, v) in this.ResxGenerators)
        {
            gens[k] = v;
        }

        return new JsonObject
        {
            ["name"] = this.Name,
            ["path"] = this.Path,
            ["directory"] = this.Directory,
            ["kind"] = this.Kind,
            ["roles"] = Json.Array(this.Roles),
            ["classes"] = classes,
            ["packageReferences"] = Json.Array(this.PackageReferences),
            ["projectReferences"] = Json.Array(this.ProjectReferences),
            ["linkedCompileFiles"] = Json.Array(this.LinkedCompileFiles),
            ["resxGenerators"] = gens,
            ["usesResxSourceGenerator"] = this.UsesResxSourceGenerator,
            ["neutralLanguage"] = this.NeutralLanguage,
            ["langVersion"] = this.LangVersion,
            ["targetFrameworks"] = this.TargetFrameworks,
            // Reported so that a project the scan could only guess at is not read as a project with nothing in it.
            ["evaluationError"] = this.EvaluationError,
        };
    }
}
