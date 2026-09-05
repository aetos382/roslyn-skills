namespace Aetos.RoslynSkills.Evals.AddDiagnostic;

internal static partial class AddDiagnosticFixtures
{
    /// <summary>
    /// A repository whose descriptors pass literal strings and that has no resx anywhere, no documentation
    /// directory, and no suppressions yet. The literal route is the one the neighbouring descriptors decide, so
    /// 6d has nothing to do; the band mapping is deliberately not the conventional one, so a workflow that
    /// assumes "Usage is band 2" allocates the wrong number.
    /// </summary>
    private static Fixture Literal()
    {
        var files = Common();

        // lang=xml
        files["src/Northwind.Analyzers/Northwind.Analyzers.csproj"] = """
            <Project Sdk="Microsoft.NET.Sdk">

              <PropertyGroup>
                <TargetFramework>netstandard2.0</TargetFramework>
                <RootNamespace>Northwind.Analyzers</RootNamespace>
                <NeutralLanguage>en-US</NeutralLanguage>
                <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="5.9.0" PrivateAssets="all" />
                <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="5.9.0" PrivateAssets="all" />
              </ItemGroup>

            </Project>
            """;

        // lang=c#
        files["src/Northwind.Analyzers/DiagnosticIds.cs"] = """
            namespace Northwind.Analyzers;

            internal static class DiagnosticIds
            {
                // Usage (NWD1xxx)
                public const string DisposableFieldShouldBeDisposed = "NWD1001";
                public const string CancellationTokenShouldBeForwarded = "NWD1002";
            }
            """;

        // lang=c#
        files["src/Northwind.Analyzers/DiagnosticCategories.cs"] = """
            namespace Northwind.Analyzers;

            internal static class DiagnosticCategories
            {
                public const string Usage = "Usage";
            }
            """;

        // lang=c#
        files["src/Northwind.Analyzers/NorthwindAnalyzer.cs"] = """
            using System.Collections.Immutable;

            using Microsoft.CodeAnalysis;
            using Microsoft.CodeAnalysis.Diagnostics;

            namespace Northwind.Analyzers;

            [DiagnosticAnalyzer(LanguageNames.CSharp)]
            public sealed class NorthwindAnalyzer : DiagnosticAnalyzer
            {
                private static readonly DiagnosticDescriptor DisposableFieldShouldBeDisposed = new(
                    DiagnosticIds.DisposableFieldShouldBeDisposed,
                    "Disposable field should be disposed",
                    "Field '{0}' holds a disposable value that is never disposed",
                    DiagnosticCategories.Usage,
                    DiagnosticSeverity.Warning,
                    isEnabledByDefault: true,
                    description: "A field holding an IDisposable value keeps that value alive for the lifetime of its owner. Dispose it from the owner's own Dispose method.");

                private static readonly DiagnosticDescriptor CancellationTokenShouldBeForwarded = new(
                    DiagnosticIds.CancellationTokenShouldBeForwarded,
                    "CancellationToken should be forwarded",
                    "Method '{0}' accepts a CancellationToken but does not pass it to '{1}'",
                    DiagnosticCategories.Usage,
                    DiagnosticSeverity.Warning,
                    isEnabledByDefault: true,
                    description: "A method that accepts a CancellationToken and does not forward it to the calls it makes cannot be cancelled.");

                public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
                    [DisposableFieldShouldBeDisposed, CancellationTokenShouldBeForwarded];

                public override void Initialize(AnalysisContext context)
                {
                    context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
                    context.EnableConcurrentExecution();
                }
            }
            """;

        // lang=markdown
        files["src/Northwind.Analyzers/AnalyzerReleases.Shipped.md"] = """
            ; Shipped analyzer releases
            ; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md
            """;

        // lang=markdown
        files["src/Northwind.Analyzers/AnalyzerReleases.Unshipped.md"] = """
            ; Unshipped analyzer release
            ; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

            ### New Rules

            Rule ID | Category | Severity | Notes
            --------|----------|----------|-------
            NWD1001 | Usage    | Warning  | A disposable field should be disposed by its owner.
            NWD1002 | Usage    | Warning  | A CancellationToken parameter should be forwarded to the calls the method makes.
            """;

        return new Fixture(
            "literal",
            "https://github.com/aetos382/northwind-analyzers.git",
            "src/Northwind.Analyzers/Northwind.Analyzers.csproj",
            files);
    }
}
