using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Contoso.Analyzers;

/// <summary>
/// Shows where the DiagnosticDescriptor for a new diagnostic goes and how it is wired into
/// SupportedDiagnostics. The descriptor field carries the same name as the ID constant.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DisposableFieldAnalyzer : DiagnosticAnalyzer
{
    // One descriptor per diagnostic, named after the ID constant.
    // Title/Message/Description come from Resources.resx ({Name}Title, {Name}Message, {Name}Description)
    // through the nested Resources.Localizable class (see examples/Resources.Roslyn.cs). Without that partial,
    // use new LocalizableResourceString(nameof(Resources.XxxTitle), Resources.ResourceManager, typeof(Resources)).
    private static readonly DiagnosticDescriptor DisposableFieldShouldBeDisposed = new(
        id: DiagnosticIds.DisposableFieldShouldBeDisposed,
        title: Resources.Localizable.DisposableFieldShouldBeDisposedTitle,
        messageFormat: Resources.Localizable.DisposableFieldShouldBeDisposedMessage,
        category: DiagnosticCategories.Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Resources.Localizable.DisposableFieldShouldBeDisposedDescription,
        helpLinkUri: "https://github.com/contoso/analyzers/blob/main/docs/rules/CTS1001.md");

    // A diagnostic reported from a compilation-end action must carry the CompilationEnd tag (RS1037).
    // Omit customTags entirely when nothing is required.
    private static readonly DiagnosticDescriptor AbstractTypeShouldNotHavePublicConstructor = new(
        id: DiagnosticIds.AbstractTypeShouldNotHavePublicConstructor,
        title: Resources.Localizable.AbstractTypeShouldNotHavePublicConstructorTitle,
        messageFormat: Resources.Localizable.AbstractTypeShouldNotHavePublicConstructorMessage,
        category: DiagnosticCategories.Design,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: Resources.Localizable.AbstractTypeShouldNotHavePublicConstructorDescription,
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    // Collection expression (C# 12+): avoids IDE0303 under EnforceCodeStyleInBuild. Use ImmutableArray.Create(...)
    // only when LangVersion is below 12.
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [
            DisposableFieldShouldBeDisposed,
            AbstractTypeShouldNotHavePublicConstructor,
        ];

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        // Analysis registration goes here (out of scope for the add-diagnostic skill).
    }
}
