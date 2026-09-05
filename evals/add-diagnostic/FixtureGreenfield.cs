using System.Collections.Generic;

using Aetos.RoslynSkills.Evals;

namespace Aetos.RoslynSkills.Evals.AddDiagnostic;

internal static partial class AddDiagnosticFixtures
{
    /// <summary>
    /// An analyzer project with nothing in it yet: no IDs file, no categories class, no descriptor to follow, no
    /// resx, no release tracking files, no documentation. Every "when it is missing, create it" branch of the
    /// workflow runs here, and Step 4 has to ask for the prefix and for where the strings live.
    /// </summary>
    private static Fixture Greenfield()
    {
        var files = Common();


        // lang=xml
        files["src/Fabrikam.Analyzers/Fabrikam.Analyzers.csproj"] = """
            <Project Sdk="Microsoft.NET.Sdk">

              <PropertyGroup>
                <TargetFramework>netstandard2.0</TargetFramework>
                <RootNamespace>Fabrikam.Analyzers</RootNamespace>
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
        files["src/Fabrikam.Analyzers/FabrikamAnalyzer.cs"] = """
            using System.Collections.Immutable;

            using Microsoft.CodeAnalysis;
            using Microsoft.CodeAnalysis.Diagnostics;

            namespace Fabrikam.Analyzers;

            [DiagnosticAnalyzer(LanguageNames.CSharp)]
            public sealed class FabrikamAnalyzer : DiagnosticAnalyzer
            {
                public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [];

                public override void Initialize(AnalysisContext context)
                {
                    context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
                    context.EnableConcurrentExecution();
                }
            }
            """;

        // lang=markdown
        files["README.md"] = """
            # Fabrikam.Analyzers

            Roslyn analyzers for Fabrikam's internal libraries. No rules yet.
            """;

        return new Fixture(
            "greenfield",
            "https://github.com/fabrikam/fabrikam-analyzers.git",
            "src/Fabrikam.Analyzers/Fabrikam.Analyzers.csproj",
            files);
    }
}
