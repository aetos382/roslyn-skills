using Aetos.RoslynSkills.Tools.AddDiagnostic;

namespace Aetos.RoslynSkills.Tools.Tests;

/// <summary>
/// Recognising an AnalyzerReleases file that an interrupted run left behind. Such a file exists, carries every
/// heading, and declares nothing, so the scan has to report it as a leftover rather than as release tracking that
/// is already in place.
/// </summary>
[TestClass]
public sealed class LeftoverTests
{
    private const string Header =
        """
        ; Unshipped analyzer release
        ; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

        ### New Rules

        Rule ID | Category | Severity | Notes
        --------|----------|----------|-------
        """;

    /// <summary>
    /// Guarantees the comment lines, the heading, and the table header and rule are not mistaken for rules: they are
    /// exactly what a file created but never filled in contains.
    /// </summary>
    [TestMethod]
    public void AFileWithHeadingsAndNoRowsListsNoRule()
    {
        Assert.IsTrue(FindConventionsCommand.ListsNoRule(Header));
        Assert.IsTrue(FindConventionsCommand.ListsNoRule("; Unshipped analyzer release\n"));
        Assert.IsTrue(FindConventionsCommand.ListsNoRule(""));
    }

    /// <summary>
    /// Guarantees a single rule row is enough to make the file a real one, whichever section it sits under, so a
    /// repository that tracks releases is never reported as having a leftover.
    /// </summary>
    [TestMethod]
    public void ASingleRuleRowIsARule()
    {
        Assert.IsFalse(FindConventionsCommand.ListsNoRule($"{Header}\nCTS1001 | Design | Warning | Dispose fields"));
        Assert.IsFalse(FindConventionsCommand.ListsNoRule("## Release 1.0\n\n### Removed Rules\n\nABCS001 | Usage | Info | Gone"));
    }
}
