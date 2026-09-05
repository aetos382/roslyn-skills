using System.Collections.Generic;

namespace Aetos.RoslynSkills.Evals;

/// <summary>
/// One repository shape an eval runs against.
///
/// Fixtures are generated rather than committed as real projects: every eval needs a repository in a different
/// state, and the agent edits it destructively, so each run has to start from somewhere no earlier run can have
/// touched.
/// </summary>
/// <param name="Name">The name <c>evals.json</c> refers to it by.</param>
/// <param name="Remote">
/// The git remote to set on it. A skill that resolves documentation URLs reads this, and a fixture without one
/// would silently exercise the "no remote" branch in every eval.
/// </param>
/// <param name="BuildProject">
/// The project the grader builds, repository-relative, to see whether the agent's edits compile.
/// </param>
/// <param name="Files">Repository-relative path to content, written verbatim.</param>
internal sealed record Fixture(
    string Name,
    string Remote,
    string BuildProject,
    IReadOnlyDictionary<string, string> Files);
