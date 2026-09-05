// The repositories the add-diagnostic evals run against.
//
// They are generated rather than committed as real projects: every eval needs a repository in a different state
// (resx or literals, docs or none, an existing prefix or none at all), and the agent edits them destructively.
// Generating means each run starts from a state no earlier run can have touched.

using System;
using System.Collections.Generic;

using Aetos.RoslynSkills.Evals;

namespace Aetos.RoslynSkills.Evals.AddDiagnostic;

internal static partial class AddDiagnosticFixtures
{
    public static IReadOnlyDictionary<string, Func<Fixture>> All { get; } = new Dictionary<string, Func<Fixture>>(StringComparer.Ordinal)
    {
        ["mature"] = Mature,
        ["greenfield"] = Greenfield,
        ["literal"] = Literal,
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

        // lang=xml
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

        // lang=xml
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

        // The SDK a fixture builds under has to be the same one every time, wherever the run directory sits: it
        // decides which analyzers run and what they say, and a baseline that moves with the machine cannot
        // separate the agent's warnings from the toolchain's. This is the pin from the repository's own
        // global.json.
        ["global.json"] = Harness.FixtureGlobalJson,

        // MSBuild walks up from the project until it finds each of these and stops there, so a fixture that
        // declares none of them inherits whatever sits above it. Runs live under this repository's own Temp,
        // where "above it" is this repository: its Directory.Packages.props would turn central package management
        // on and make every Version attribute below an NU1008 error, and its Directory.Build.targets would add
        // packaging metadata to a project that is not being packaged. Each file exists to stop that search, not
        // to say anything.

        // lang=xml
        ["Directory.Build.targets"] = """
            <Project>
            </Project>
            """,

        // lang=xml
        ["Directory.Packages.props"] = """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
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
