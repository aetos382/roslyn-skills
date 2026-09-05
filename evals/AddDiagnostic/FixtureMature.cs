namespace Aetos.RoslynSkills.Evals.AddDiagnostic;

internal static partial class AddDiagnosticFixtures
{
    /// <summary>
    /// A repository that already does everything: a prefix with band headers, a categories class, resx strings in
    /// two cultures generated at build time, release tracking, rule pages with an index, and a code-fix project
    /// that cannot see the IDs file. Exercises the main path and, through 6g, the ID sharing question.
    /// </summary>
    private static Fixture Mature()
    {
        var files = Common();

        // lang=xml
        files["src/Acme.Analyzers/Acme.Analyzers.csproj"] = """
            <Project Sdk="Microsoft.NET.Sdk">

              <PropertyGroup>
                <TargetFramework>netstandard2.0</TargetFramework>
                <RootNamespace>Acme.Analyzers</RootNamespace>
                <NeutralLanguage>en-US</NeutralLanguage>
                <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="5.9.0" PrivateAssets="all" />
                <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="5.9.0" PrivateAssets="all" />
              </ItemGroup>

              <ItemGroup>
                <EmbeddedResource Update="Resources.resx">
                  <StronglyTypedFileName>$(IntermediateOutputPath)Resources.Designer.cs</StronglyTypedFileName>
                  <StronglyTypedLanguage>CSharp</StronglyTypedLanguage>
                  <StronglyTypedNamespace>Acme.Analyzers</StronglyTypedNamespace>
                  <StronglyTypedClassName>Resources</StronglyTypedClassName>
                </EmbeddedResource>
              </ItemGroup>

            </Project>
            """;

        // lang=c#
        files["src/Acme.Analyzers/DiagnosticIds.cs"] = """
            namespace Acme.Analyzers;

            internal static class DiagnosticIds
            {
                // Design (ACM1xxx)
                public const string AbstractTypeShouldNotHavePublicConstructor = "ACM1001";

                // Usage (ACM2xxx)
                public const string TaskShouldBeAwaited = "ACM2001";
            }
            """;

        // lang=c#
        files["src/Acme.Analyzers/DiagnosticCategories.cs"] = """
            namespace Acme.Analyzers;

            internal static class DiagnosticCategories
            {
                public const string Design = "Design";

                public const string Usage = "Usage";
            }
            """;

        // lang=c#
        files["src/Acme.Analyzers/AcmeAnalyzer.cs"] = """
            using System.Collections.Immutable;

            using Microsoft.CodeAnalysis;
            using Microsoft.CodeAnalysis.Diagnostics;

            namespace Acme.Analyzers;

            [DiagnosticAnalyzer(LanguageNames.CSharp)]
            public sealed class AcmeAnalyzer : DiagnosticAnalyzer
            {
                private static readonly DiagnosticDescriptor AbstractTypeShouldNotHavePublicConstructor = new(
                    DiagnosticIds.AbstractTypeShouldNotHavePublicConstructor,
                    new LocalizableResourceString(nameof(Resources.AbstractTypeShouldNotHavePublicConstructorTitle), Resources.ResourceManager, typeof(Resources)),
                    new LocalizableResourceString(nameof(Resources.AbstractTypeShouldNotHavePublicConstructorMessage), Resources.ResourceManager, typeof(Resources)),
                    DiagnosticCategories.Design,
                    DiagnosticSeverity.Warning,
                    isEnabledByDefault: true,
                    description: new LocalizableResourceString(nameof(Resources.AbstractTypeShouldNotHavePublicConstructorDescription), Resources.ResourceManager, typeof(Resources)),
                    helpLinkUri: "https://github.com/aetos382/acme-analyzers/blob/main/docs/rules/ACM1001.md");

                private static readonly DiagnosticDescriptor TaskShouldBeAwaited = new(
                    DiagnosticIds.TaskShouldBeAwaited,
                    new LocalizableResourceString(nameof(Resources.TaskShouldBeAwaitedTitle), Resources.ResourceManager, typeof(Resources)),
                    new LocalizableResourceString(nameof(Resources.TaskShouldBeAwaitedMessage), Resources.ResourceManager, typeof(Resources)),
                    DiagnosticCategories.Usage,
                    DiagnosticSeverity.Warning,
                    isEnabledByDefault: true,
                    description: new LocalizableResourceString(nameof(Resources.TaskShouldBeAwaitedDescription), Resources.ResourceManager, typeof(Resources)),
                    helpLinkUri: "https://github.com/aetos382/acme-analyzers/blob/main/docs/rules/ACM2001.md");

                public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
                    [AbstractTypeShouldNotHavePublicConstructor, TaskShouldBeAwaited];

                public override void Initialize(AnalysisContext context)
                {
                    context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
                    context.EnableConcurrentExecution();
                }
            }
            """;

        files["src/Acme.Analyzers/Resources.resx"] = ResxHeader + """
              <data name="AbstractTypeShouldNotHavePublicConstructorDescription" xml:space="preserve">
                <value>A public constructor on an abstract type can never be called from outside the type's own hierarchy. Declare it protected instead.</value>
              </data>
              <data name="AbstractTypeShouldNotHavePublicConstructorMessage" xml:space="preserve">
                <value>Abstract type '{0}' should not declare a public constructor</value>
              </data>
              <data name="AbstractTypeShouldNotHavePublicConstructorTitle" xml:space="preserve">
                <value>Abstract type should not have a public constructor</value>
              </data>
              <data name="TaskShouldBeAwaitedDescription" xml:space="preserve">
                <value>A Task that is neither awaited nor observed swallows the exceptions thrown inside it. Await it, or hand it to something that observes it.</value>
              </data>
              <data name="TaskShouldBeAwaitedMessage" xml:space="preserve">
                <value>The Task returned by '{0}' should be awaited</value>
              </data>
              <data name="TaskShouldBeAwaitedTitle" xml:space="preserve">
                <value>Task should be awaited</value>
              </data>
            </root>
            """;

        files["src/Acme.Analyzers/Resources.ja.resx"] = ResxHeader + """
              <data name="AbstractTypeShouldNotHavePublicConstructorDescription" xml:space="preserve">
                <value>抽象型の public コンストラクターは、その型の派生階層の外からは決して呼び出せません。protected として宣言してください。</value>
              </data>
              <data name="AbstractTypeShouldNotHavePublicConstructorMessage" xml:space="preserve">
                <value>抽象型 '{0}' は public コンストラクターを宣言すべきではありません</value>
              </data>
              <data name="AbstractTypeShouldNotHavePublicConstructorTitle" xml:space="preserve">
                <value>抽象型は public コンストラクターを持つべきではない</value>
              </data>
              <data name="TaskShouldBeAwaitedDescription" xml:space="preserve">
                <value>await も監視もされない Task は、その内部で発生した例外を握り潰します。await するか、例外を監視する場所へ渡してください。</value>
              </data>
              <data name="TaskShouldBeAwaitedMessage" xml:space="preserve">
                <value>'{0}' が返す Task は await されるべきです</value>
              </data>
              <data name="TaskShouldBeAwaitedTitle" xml:space="preserve">
                <value>Task は await されるべき</value>
              </data>
            </root>
            """;

        // lang=markdown
        files["src/Acme.Analyzers/AnalyzerReleases.Shipped.md"] = """
            ; Shipped analyzer releases
            ; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md
            """;

        // lang=markdown
        files["src/Acme.Analyzers/AnalyzerReleases.Unshipped.md"] = """
            ; Unshipped analyzer release
            ; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

            ### New Rules

            Rule ID | Category | Severity | Notes
            --------|----------|----------|-------
            ACM1001 | Design   | Warning  | An abstract type should not declare a public constructor.
            ACM2001 | Usage    | Warning  | A Task that is returned should be awaited.
            """;

        // No ProjectReference back to the analyzer project, so find-conventions reports idSharing "none" for this
        // one: what 6g has to notice, and what the Step 4 round has to ask about.

        // lang=xml
        files["src/Acme.Analyzers.CodeFixes/Acme.Analyzers.CodeFixes.csproj"] = """
            <Project Sdk="Microsoft.NET.Sdk">

              <PropertyGroup>
                <TargetFramework>netstandard2.0</TargetFramework>
                <RootNamespace>Acme.Analyzers.CodeFixes</RootNamespace>
                <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="5.9.0" PrivateAssets="all" />
              </ItemGroup>

            </Project>
            """;

        // lang=c#
        files["src/Acme.Analyzers.CodeFixes/TaskShouldBeAwaitedCodeFixProvider.cs"] = """
            using System.Collections.Immutable;
            using System.Threading.Tasks;

            using Microsoft.CodeAnalysis;
            using Microsoft.CodeAnalysis.CodeFixes;

            namespace Acme.Analyzers.CodeFixes;

            [ExportCodeFixProvider(LanguageNames.CSharp)]
            public sealed class TaskShouldBeAwaitedCodeFixProvider : CodeFixProvider
            {
                public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["ACM2001"];

                public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

                public override Task RegisterCodeFixesAsync(CodeFixContext context) => Task.CompletedTask;
            }
            """;

        // lang=markdown
        files["docs/rules/README.md"] = """
            # Rules

            | ID | Category | Severity | Title |
            |----|----------|----------|-------|
            | [ACM1001](ACM1001.md) | Design | Warning | Abstract type should not have a public constructor |
            | [ACM2001](ACM2001.md) | Usage | Warning | Task should be awaited |
            """;

        // lang=markdown
        files["docs/rules/ACM1001.md"] = """
            # ACM1001: Abstract type should not have a public constructor

            | Item | Value |
            |------|-------|
            | Category | Design |
            | Severity | Warning |
            | Enabled by default | Yes |

            ## Cause

            An abstract type declares a public constructor.

            ## Rule description

            A public constructor on an abstract type can never be called from outside the type's own hierarchy.

            ## How to fix violations

            Declare the constructor `protected`.

            ## When to suppress warnings

            Do not suppress this rule.
            """;

        // lang=markdown
        files["docs/rules/ACM2001.md"] = """
            # ACM2001: Task should be awaited

            | Item | Value |
            |------|-------|
            | Category | Usage |
            | Severity | Warning |
            | Enabled by default | Yes |

            ## Cause

            A method returns a `Task` that is neither awaited nor observed.

            ## Rule description

            A Task that is neither awaited nor observed swallows the exceptions thrown inside it.

            ## How to fix violations

            Await the Task, or hand it to something that observes it.

            ## When to suppress warnings

            Suppress this rule when the Task is deliberately fire-and-forget and handles its own exceptions.
            """;

        return new Fixture(
            "mature",
            "https://github.com/aetos382/acme-analyzers.git",
            "src/Acme.Analyzers/Acme.Analyzers.csproj",
            files);
    }
}
