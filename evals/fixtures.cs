// Fixture repositories the evals run against.
//
// They are generated rather than committed as real projects: every eval needs a repository in a different state
// (resx or literals, docs or none, an existing prefix or none at all), the agent edits them destructively, and a
// nested .csproj inside this repository would join its build. Generating means each run starts from a state no
// earlier run can have touched.

using System.Collections.Generic;

/// <summary>One repository shape: its files, the git remote <c>doc-url</c> resolves against, and the project the
/// grader builds to see whether the agent's edits compile.</summary>
internal sealed record Fixture(string Name, string Remote, string BuildProject, IReadOnlyDictionary<string, string> Files);

internal static partial class Fixtures
{
    public static IReadOnlyList<string> Names { get; } = ["mature", "greenfield", "literal"];

    public static Fixture Get(string name) => name switch
    {
        "mature" => Mature(),
        "greenfield" => Greenfield(),
        "literal" => Literal(),
        _ => throw new KeyNotFoundException($"unknown fixture '{name}'; known: {string.Join(", ", Names)}"),
    };

    // Shared by all three: a .gitignore so the agent's builds stay out of git status, a NuGet.config that ignores
    // whatever the machine has configured, and a Directory.Build.props that both cuts off inheritance from any
    // parent directory and turns on the analyzer settings Step 7 of the skill expects to face.
    private static Dictionary<string, string> Common() => new()
    {
        [".gitignore"] = """
            bin/
            obj/
            """,
        ["NuGet.config"] = """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
              </packageSources>
              <fallbackPackageFolders>
                <clear />
              </fallbackPackageFolders>
            </configuration>
            """,
        // The fixtures start with no warnings at all, so that a warning after the agent's edits is unambiguously
        // one the agent introduced. That means settling the preferences the fixture code already follows, and
        // turning off the two rules an analyzer's own entry points always trip.
        [".editorconfig"] = """
            root = true

            [*.cs]
            csharp_style_namespace_declarations = file_scoped
            dotnet_diagnostic.CA1062.severity = none
            dotnet_diagnostic.CA1725.severity = none
            """,
        ["Directory.Build.props"] = """
            <Project>
              <PropertyGroup>
                <LangVersion>latest</LangVersion>
                <Nullable>enable</Nullable>
                <ImplicitUsings>disable</ImplicitUsings>
                <AnalysisLevel>latest-all</AnalysisLevel>
                <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
                <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
                <IsPackable>false</IsPackable>
              </PropertyGroup>
            </Project>
            """,
    };

    private const string ResxHeader = """
        <?xml version="1.0" encoding="utf-8"?>
        <root>
          <resheader name="resmimetype">
            <value>text/microsoft-resx</value>
          </resheader>
          <resheader name="version">
            <value>2.0</value>
          </resheader>
          <resheader name="reader">
            <value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
          </resheader>
          <resheader name="writer">
            <value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
          </resheader>

        """;
}
