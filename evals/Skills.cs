using System;
using System.Collections.Generic;

using Aetos.RoslynSkills.Evals.AddDiagnostic;

namespace Aetos.RoslynSkills.Evals;

/// <summary>
/// What the harness needs to know about one skill: where its fixtures come from, and how to take a structured
/// reading of a repository afterwards. Everything else about an eval — the prompts, what is asserted — lives in
/// that skill's own directory, so adding a skill is a directory plus one row in <see cref="Skills"/>.
/// </summary>
/// <param name="Name">
/// The skill's directory name under both <c>evals/</c> and <c>plugin/skills/</c>. The two have to agree: the
/// prompt points the agent at the skill, and the pinned tool version is read from that skill's SKILL.md.
/// </param>
/// <param name="Fixtures">Fixture name to the function that builds it.</param>
/// <param name="Scan">
/// Arguments passed to the pinned tool, with <c>{repo}</c> standing for the fixture, producing the JSON the
/// <c>json*</c> assertions read. Null for a skill with no such command, whose evals then have to work from files
/// alone.
/// </param>
internal sealed record SkillEvals(
    string Name,
    IReadOnlyDictionary<string, Func<Fixture>> Fixtures,
    IReadOnlyList<string>? Scan);

internal static class Skills
{
    public static IReadOnlyDictionary<string, SkillEvals> All { get; } = new Dictionary<string, SkillEvals>(StringComparer.Ordinal)
    {
        ["add-diagnostic"] = new(
            "add-diagnostic",
            AddDiagnosticFixtures.All,
            ["add-diagnostic", "find-conventions", "--path", "{repo}", "--summary"]),
    };

    public static SkillEvals Get(string name) =>
        All.TryGetValue(name, out var skill)
            ? skill
            : throw new KeyNotFoundException($"no evals for a skill named '{name}'; known: {string.Join(", ", All.Keys)}");
}
