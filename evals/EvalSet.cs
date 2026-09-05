using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Aetos.RoslynSkills.Evals;

/// <summary>
/// Checks the <c>evals.json</c> files against what the harness can actually run.
///
/// They are otherwise only read when a run starts or is graded, so a typo in one surfaces after an agent run
/// rather than before it. Separated from the command that prints the result so a test can assert the shipped
/// files are clean without going through the CLI.
/// </summary>
internal static class EvalSet
{
    /// <summary>How long a summary may be. Long enough for a sentence, short enough that it is not a paragraph.</summary>
    private const int SummaryLimit = 120;

    /// <summary>One line per problem found, empty when every eval of every skill is well formed.</summary>
    public static IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        foreach (var skillName in Skills.All.Keys)
        {
            var skill = Skills.Get(skillName);
            var ids = new HashSet<string>(StringComparer.Ordinal);

            foreach (var eval in Harness.Evals(skillName))
            {
                var id = eval["id"]?.ToString();
                if (id is null || !ids.Add(id))
                {
                    problems.Add($"{skillName}: an eval has a missing or duplicated id: {id ?? "(none)"}");
                    continue;
                }

                var where = $"{skillName}:{id}";

                if (eval["prompt"] is null || eval["expected_output"] is null)
                {
                    problems.Add($"{where}: prompt and expected_output are both required");
                }

                // The summary is what `list` shows, and what someone reads to decide which eval to run. It has to
                // stay a sentence: allowed to grow into a paragraph it becomes a second expected_output, and the
                // listing goes back to being something nobody reads.
                if (eval["summary"]?.ToString() is not { } summary)
                {
                    problems.Add($"{where}: a summary is required, saying in one sentence what the eval guarantees");
                }
                else if (summary.Length > SummaryLimit || summary.Contains('\n', StringComparison.Ordinal))
                {
                    problems.Add(
                        $"{where}: the summary is {summary.Length} characters over {SummaryLimit} or spans lines; "
                        + "the long form belongs in expected_output");
                }

                var fixture = eval["fixture"]?.ToString();
                if (fixture is null || !skill.Fixtures.ContainsKey(fixture))
                {
                    problems.Add($"{where}: unknown fixture '{fixture}'; known: {string.Join(", ", skill.Fixtures.Keys)}");
                }

                foreach (var assertion in eval["assertions"]?.AsArray() ?? [])
                {
                    var a = assertion!.AsObject();
                    var kind = a["kind"]?.ToString();

                    if (kind is null || !Assertions.Kinds.Contains(kind, StringComparer.Ordinal))
                    {
                        problems.Add($"{where}: unknown assertion kind '{kind}'");
                    }
                    else if (kind.StartsWith("json", StringComparison.Ordinal) && skill.Scan is null)
                    {
                        problems.Add($"{where}: '{kind}' reads a scan, and {skillName} declares no scan command");
                    }

                    if (a["text"] is null)
                    {
                        problems.Add($"{where}: an assertion has no text, so its result would be unreadable");
                    }

                    if (a["pattern"]?.ToString() is { } pattern)
                    {
                        try
                        {
                            _ = new Regex(Assertions.StripPlaceholders(pattern));
                        }
                        catch (ArgumentException ex)
                        {
                            problems.Add($"{where}: /{pattern}/ is not a valid regular expression: {ex.Message}");
                        }
                    }
                }
            }
        }

        return problems;
    }

    /// <summary>How many evals there are in total, for the line <c>check</c> prints when it finds nothing wrong.</summary>
    public static int Count() => Harness.Evals().Count();
}
