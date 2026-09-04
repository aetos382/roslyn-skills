using System.Text.Json;

using Aetos.RoslynSkills.Tools.Internal;

namespace Aetos.RoslynSkills.Tools.Tests;

/// <summary>
/// Reading the JSON `dotnet msbuild -getProperty -getItem` prints. Every project's data arrives this way, so a
/// misread field turns into a convention the repository does not have.
/// </summary>
[TestClass]
public sealed class EvaluationTests
{
    private const string Output =
        /*lang=json,strict*/
        """
        {
          "Properties": {
            "NeutralLanguage": "  ja  ",
            "LangVersion": "",
            "TargetFrameworks": "netstandard2.0;net10.0",
            "IsTestProject": "True"
          },
          "Items": {
            "PackageReference": [
              { "Identity": "Microsoft.CodeAnalysis.CSharp", "Version": "5.9.0" }
            ],
            "Compile": [
              { "Identity": "A.cs", "FullPath": "/repo/src/A.cs", "Link": "Shared/A.cs" },
              { "Identity": "B.cs", "FullPath": "" },
              "not an item"
            ]
          }
        }
        """;

    /// <summary>
    /// Guarantees a property is read by any casing and comes back trimmed, because the names are spelled as MSBuild
    /// does — case-insensitively — and a value MSBuild pads would end up inside a generated declaration.
    /// </summary>
    [TestMethod]
    public void APropertyIsReadWithoutRegardToCaseAndComesBackTrimmed()
    {
        var evaluation = Evaluation.Parse(Output);

        Assert.AreEqual("ja", evaluation.Property("neutrallanguage"));
        Assert.AreEqual("netstandard2.0;net10.0", evaluation.Property("TargetFrameworks"));
    }

    /// <summary>
    /// Guarantees a property MSBuild reports as empty is reported as absent: an unset property comes back as ""
    /// rather than missing, and "" is not a language or a language version any caller could use.
    /// </summary>
    [TestMethod]
    public void AnEmptyPropertyIsTheSameAsAnAbsentOne()
    {
        var evaluation = Evaluation.Parse(Output);

        Assert.IsNull(evaluation.Property("LangVersion"));
        Assert.IsNull(evaluation.Property("RootNamespace"));
    }

    /// <summary>
    /// Guarantees a boolean property is read the way MSBuild writes it — "True" — rather than only as "true",
    /// which is what decides whether a project is treated as a test project.
    /// </summary>
    [TestMethod]
    public void ABooleanPropertyIsReadWithoutRegardToCase()
    {
        var evaluation = Evaluation.Parse(Output);

        Assert.IsTrue(evaluation.IsTrue("IsTestProject"));
        Assert.IsFalse(evaluation.IsTrue("UsingMSTestSdk"), "an absent property is not true");
    }

    /// <summary>
    /// Guarantees an item keeps its identity, resolved path and remaining metadata, since the metadata is where a
    /// linked Compile item's Link and a PackageReference's Version are read from.
    /// </summary>
    [TestMethod]
    public void AnItemKeepsItsIdentityPathAndMetadata()
    {
        var compile = Evaluation.Parse(Output).Items("compile");

        Assert.AreEqual("A.cs", compile[0].Identity);
        Assert.AreEqual("/repo/src/A.cs", compile[0].FullPath);
        Assert.AreEqual("Shared/A.cs", compile[0].Metadata["link"]);
        Assert.AreEqual("5.9.0", Evaluation.Parse(Output).Items("PackageReference")[0].Metadata["Version"]);
    }

    /// <summary>
    /// Guarantees an item with no resolved path is reported as having none rather than as an empty path, so a
    /// caller cannot combine "" with a directory and get a file that looks real.
    /// </summary>
    [TestMethod]
    public void AnItemWithoutAFullPathReportsNone()
    {
        var compile = Evaluation.Parse(Output).Items("Compile");

        Assert.AreEqual("B.cs", compile[1].Identity);
        Assert.IsNull(compile[1].FullPath);
        Assert.HasCount(2, compile, "an array element that is not an object is not an item");
    }

    /// <summary>Guarantees an item type the project has none of reads as empty rather than throwing.</summary>
    [TestMethod]
    public void AnItemTypeThatIsAbsentIsEmpty()
    {
        Assert.IsEmpty(Evaluation.Parse(Output).Items("EmbeddedResource"));
    }

    /// <summary>
    /// Guarantees output that parses as JSON but is not an object is reported as a failure: MSBuild prints a bare
    /// value when only one property is requested, and treating that as an empty evaluation would describe every
    /// project as having no packages, no references and no resources.
    /// </summary>
    [TestMethod]
    public void OutputThatIsNotAJsonObjectIsAFailure()
    {
        var evaluation = Evaluation.Parse("\"netstandard2.0\"");

        Assert.IsNotNull(evaluation.Error);
        Assert.IsNull(evaluation.Property("TargetFramework"));
    }

    /// <summary>
    /// Guarantees output that is not JSON at all throws, which is the contract the caller relies on: it catches
    /// JsonException and reports the message as that project's evaluationError, MSBuild diagnostics included.
    /// </summary>
    [TestMethod]
    public void OutputThatIsNotJsonThrows()
    {
        Assert.Throws<JsonException>(() => Evaluation.Parse("MSB1009: Project file does not exist."));
    }
}
