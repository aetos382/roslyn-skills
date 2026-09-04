using Aetos.RoslynSkills.Tools.AddDiagnostic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Aetos.RoslynSkills.Tools.Tests;

[TestClass]
public sealed class IdsFileTextTests
{
    /// <summary>
    /// Guarantees the band headers are read in both shapes seen in the wild, because they — not the config
    /// file — are the source of truth for the category-to-band mapping.
    /// </summary>
    [TestMethod]
    public void BandHeadersAreReadInBothShapes()
    {
        var bands = IdsFileText.ReadBands(
            """
            // Design (CTS1xxx)
            // ---- Usage: CTS2xxx ----
            //Performance - CTS3xxx
            """);

        Assert.AreEqual(1, bands["Design"]);
        Assert.AreEqual(2, bands["Usage"]);
        Assert.AreEqual(3, bands["Performance"]);
    }

    /// <summary>
    /// Guarantees a header-shaped line quoted inside a string is not read as a band header, so documentation
    /// text held in a constant cannot invent a band the file does not actually use.
    /// </summary>
    [TestMethod]
    public void AHeaderQuotedInsideAStringIsNotAHeader()
    {
        var bands = IdsFileText.ReadBands(
            """
            internal static class DiagnosticIds
            {
                // Design (CTS1xxx)
                public const string Doc = "// Usage (CTS2xxx)";
            }
            """);

        Assert.AreEqual(1, bands["Design"]);
        Assert.IsFalse(bands.ContainsKey("Usage"));
    }

    /// <summary>Guarantees a category name is matched without regard to case, as next-id passes it through verbatim.</summary>
    [TestMethod]
    public void CategoryNamesAreMatchedCaseInsensitively()
    {
        var bands = IdsFileText.ReadBands("// Design (CTS1xxx)");

        Assert.AreEqual(1, bands["design"]);
    }

    /// <summary>
    /// Guarantees the prefix is taken from the headers when no ID constant exists yet, and that the most
    /// frequent one wins so a single stale header cannot rename every future diagnostic.
    /// </summary>
    [TestMethod]
    public void TheHeaderPrefixIsTheMostFrequentOne()
    {
        var prefix = IdsFileText.ReadHeaderPrefix(
            """
            // Design (CTS1xxx)
            // Usage (CTS2xxx)
            // Legacy (ABC9xxx)
            """);

        Assert.AreEqual("CTS", prefix);
    }

    /// <summary>Guarantees a file whose headers carry no prefix yields null, which is what makes --prefix required.</summary>
    [TestMethod]
    public void HeadersWithoutAPrefixYieldNull()
    {
        Assert.IsNull(IdsFileText.ReadHeaderPrefix("// Design (1xxx)"));
    }

    /// <summary>
    /// Guarantees the IDs class is reported with its declared visibility, and that an omitted modifier is
    /// reported as internal — the C# default the new constant must be written to match.
    /// </summary>
    [TestMethod]
    public void TheIdsClassVisibilityDefaultsToInternal()
    {
        var (implicitName, implicitVisibility) = IdsFileText.ReadClass("static class DiagnosticIds { }");
        var (declaredName, declaredVisibility) = IdsFileText.ReadClass("public static partial class DiagnosticIds { }");

        Assert.AreEqual("DiagnosticIds", implicitName);
        Assert.AreEqual("internal", implicitVisibility);
        Assert.AreEqual("DiagnosticIds", declaredName);
        Assert.AreEqual("public", declaredVisibility);
    }

    /// <summary>Guarantees a file with no static class is reported as such rather than as a nameless class.</summary>
    [TestMethod]
    public void AFileWithNoStaticClassHasNoClassName()
    {
        var (name, visibility) = IdsFileText.ReadClass("// nothing here");

        Assert.IsNull(name);
        Assert.AreEqual("internal", visibility);
    }
}
