using System.Text.Json.Nodes;

namespace Aetos.RoslynSkills.Evals;

/// <summary>Everything one assertion needs to look at.</summary>
/// <param name="Run">The run directory: the fixture, the agent's outputs, and what the run started from.</param>
/// <param name="Repo">The generated repository the agent edited.</param>
/// <param name="Scan">
/// The skill's structured reading of the repository, taken after the agent finished. Empty for a skill that
/// declares no scan command.
/// </param>
/// <param name="Meta">The run's <c>run.json</c>: which eval it is, what to build, and the baseline warnings.</param>
internal sealed record GradingContext(string Run, string Repo, JsonObject Scan, JsonObject Meta);
