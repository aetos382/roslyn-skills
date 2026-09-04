using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Contoso.Analyzers;

/// <summary>
/// Shows where a SuppressionDescriptor goes. The descriptor field carries the same name as the
/// SuppressionIds constant, and the only string resource is {Name}Justification.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TestClassSuppressor : DiagnosticSuppressor
{
    private static readonly SuppressionDescriptor TestClassesMayBePublic = new(
        id: SuppressionIds.TestClassesMayBePublic,
        suppressedDiagnosticId: "CA1515",
        justification: Resources.Localizable.TestClassesMayBePublicJustification);

    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions { get; } =
        ImmutableArray.Create(TestClassesMayBePublic);

    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        // Suppression logic goes here (out of scope for the add-diagnostic skill).
    }
}
