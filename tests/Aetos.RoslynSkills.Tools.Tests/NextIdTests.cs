using System;
using System.IO;

namespace Aetos.RoslynSkills.Tools.Tests;

/// <summary>
/// Allocating the next ID. An ID cannot be corrected after a release — the old one has to stay as a documented
/// alias — so every branch of the arithmetic is checked here rather than trusted.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class NextIdTests(TestContext testContext) : IDisposable
{
    private const string Ids =
        // lang=c#
        """
        internal static class DiagnosticIds
        {
            // Design (CTS1xxx)
            public const string DisposableField = "CTS1001";
            public const string SealedType = "CTS1002";

            // Usage (CTS2xxx)
        }
        """;

    private readonly TempRepository _repo = new(testContext);

    public void Dispose()
    {
        this._repo.Dispose();
    }

    /// <summary>
    /// Guarantees the next ID of a category continues that category's band rather than the file's highest number,
    /// which is the whole point of bands: CTS2xxx exists, and a new Design rule still gets CTS1003.
    /// </summary>
    [TestMethod]
    public void ACategoryContinuesItsOwnBand()
    {
        var file = this._repo.Write("DiagnosticIds.cs", Ids);

        var json = Tool.Json(0, "add-diagnostic", "next-id", "--ids-file", file, "--category", "Design");

        Assert.AreEqual("CTS1003", json["id"]!.ToString());
        Assert.AreEqual(1, json["band"]!.GetValue<int>());
        Assert.AreEqual("CTS", json["prefix"]!.ToString());
        Assert.AreEqual(4, json["digits"]!.GetValue<int>());
        Assert.IsTrue(json["idsFileExists"]!.GetValue<bool>());
    }

    /// <summary>
    /// Guarantees a band with no IDs in it yet starts at its first number, so the first Usage rule is CTS2001 and
    /// not CTS2000 — a number the band header would not describe.
    /// </summary>
    [TestMethod]
    public void AnEmptyBandStartsAtItsFirstNumber()
    {
        var file = this._repo.Write("DiagnosticIds.cs", Ids);

        var json = Tool.Json(0, "add-diagnostic", "next-id", "--ids-file", file, "--category", "usage");

        Assert.AreEqual("CTS2001", json["id"]!.ToString());
        Assert.IsEmpty(json["existingInBand"]!.AsArray());
    }

    /// <summary>
    /// Guarantees a full band is reported as a failure instead of spilling into the next one, since CTS2000 would
    /// then belong to two categories at once.
    /// </summary>
    [TestMethod]
    public void AFullBandIsReportedInsteadOfSpillingIntoTheNext()
    {
        var file = this._repo.Write("DiagnosticIds.cs",
            """
            internal static class DiagnosticIds
            {
                // Design (CTS1xxx)
                public const string Last = "CTS1999";
            }
            """);

        var json = Tool.Json(1, "add-diagnostic", "next-id", "--ids-file", file, "--category", "Design");

        Assert.Contains("full", json["error"]!.ToString());
        Assert.Contains("CTS1000-1999", json["error"]!.ToString());
        Assert.IsNotNull(json["hint"]);
    }

    /// <summary>
    /// Guarantees a suppression ID is numbered in its own sequence with no band, and that the prefix reported is the
    /// diagnostic prefix rather than the suppression letters: the extra S belongs to the ID, not to the repository.
    /// </summary>
    [TestMethod]
    public void ASuppressionIdContinuesItsOwnSequenceWithoutABand()
    {
        var file = this._repo.Write("SuppressionIds.cs",
            """
            internal static class SuppressionIds
            {
                public const string SuppressDisposableField = "CTSS0001";
            }
            """);

        var json = Tool.Json(0, "add-diagnostic", "next-id", "--ids-file", file, "--suppression");

        Assert.AreEqual("CTSS0002", json["id"]!.ToString());
        Assert.AreEqual("CTS", json["prefix"]!.ToString());
        Assert.IsNull(json["band"]);
    }

    /// <summary>
    /// Guarantees a suppression allocated from a file holding both kinds does not restart at 1 behind the
    /// diagnostics: the diagnostic IDs are not part of the suppression sequence, and vice versa.
    /// </summary>
    [TestMethod]
    public void DiagnosticsAndSuppressionsAreNumberedSeparately()
    {
        var file = this._repo.Write("Ids.cs",
            """
            internal static class Ids
            {
                public const string DisposableField = "CTS1001";
                public const string SuppressDisposableField = "CTSS0007";
            }
            """);

        Assert.AreEqual("CTSS0008", Tool.Json(0, "add-diagnostic", "next-id", "--ids-file", file, "--suppression")["id"]!.ToString());
        Assert.AreEqual("CTS1002", Tool.Json(0, "add-diagnostic", "next-id", "--ids-file", file)["id"]!.ToString());
    }

    /// <summary>
    /// Guarantees a prefix that itself ends in S is not read as a suppression group: a repository writing RS1001
    /// gets RSS0001 for its first suppression, not RS0001 in the middle of its own diagnostics.
    /// </summary>
    [TestMethod]
    public void APrefixEndingInSIsNotMistakenForASuppressionGroup()
    {
        var file = this._repo.Write("DiagnosticIds.cs",
            """
            internal static class DiagnosticIds
            {
                public const string DisposableField = "RS1001";
            }
            """);

        var json = Tool.Json(0, "add-diagnostic", "next-id", "--ids-file", file, "--suppression");

        Assert.AreEqual("RSS0001", json["id"]!.ToString());
        Assert.AreEqual("RS", json["prefix"]!.ToString());
    }

    /// <summary>
    /// Guarantees the digit count comes from the IDs already in the file, so a repository writing three digits keeps
    /// writing three and its band arithmetic scales to match.
    /// </summary>
    [TestMethod]
    public void TheDigitCountComesFromTheExistingIds()
    {
        var file = this._repo.Write("DiagnosticIds.cs",
            """
            internal static class DiagnosticIds
            {
                // Design (CTS1xx)
                public const string DisposableField = "CTS101";
            }
            """);

        var json = Tool.Json(0, "add-diagnostic", "next-id", "--ids-file", file, "--category", "Design");

        Assert.AreEqual("CTS102", json["id"]!.ToString());
        Assert.AreEqual(3, json["digits"]!.GetValue<int>());
    }

    /// <summary>
    /// Guarantees a category the file has no header for is reported as unresolved rather than guessed at: the ID is
    /// still allocated after the highest one, and the flag is what tells the skill to ask for a band.
    /// </summary>
    [TestMethod]
    public void ACategoryWithNoBandIsReportedAsUnresolved()
    {
        var file = this._repo.Write("DiagnosticIds.cs", Ids);

        var json = Tool.Json(0, "add-diagnostic", "next-id", "--ids-file", file, "--category", "Performance");

        Assert.IsTrue(json["unresolvedCategory"]!.GetValue<bool>());
        Assert.IsNull(json["band"]);
        Assert.AreEqual("CTS1003", json["id"]!.ToString(), "with no band the number follows the highest overall");
    }

    /// <summary>
    /// Guarantees the settings file resolves a category the IDs file has no header for yet, which is how a
    /// repository declares its bands up front instead of one comment at a time.
    /// </summary>
    [TestMethod]
    public void TheSettingsFileResolvesACategoryWithNoHeader()
    {
        Directory.CreateDirectory(Path.Combine(this._repo.Root, ".git"));
        this._repo.WriteConfig(
            """
            ```json
            { "categories": { "Performance": 3 } }
            ```
            """);
        var file = this._repo.Write("src/A/DiagnosticIds.cs", Ids);

        var json = Tool.Json(0, "add-diagnostic", "next-id", "--ids-file", file, "--category", "Performance");

        Assert.AreEqual("CTS3001", json["id"]!.ToString());
        Assert.AreEqual(3, json["band"]!.GetValue<int>());
        Assert.IsFalse(json["unresolvedCategory"]!.GetValue<bool>());
    }

    /// <summary>
    /// Guarantees a path that does not exist is reported instead of being read as an empty file, because a mistyped
    /// path would otherwise restart the numbering and hand out an ID the repository has already shipped.
    /// </summary>
    [TestMethod]
    public void AMistypedPathIsReportedRatherThanTreatedAsEmpty()
    {
        var missing = Path.Combine(this._repo.Root, "src", "Analyzers", "DiagnosticIds.cs");

        var json = Tool.Json(1, "add-diagnostic", "next-id", "--ids-file", missing, "--category", "Design");

        Assert.Contains(missing, json["error"]!.ToString());
        Assert.Contains("--prefix", json["hint"]!.ToString());
    }

    /// <summary>
    /// Guarantees the first ID of a repository can still be allocated before the IDs file exists, since the skill
    /// creates that file only after it knows the ID. The flag says the file is not there yet.
    /// </summary>
    [TestMethod]
    public void TheFirstIdOfAFileThatDoesNotExistYetIsAllocated()
    {
        var missing = Path.Combine(this._repo.Root, "src", "Analyzers", "DiagnosticIds.cs");

        var json = Tool.Json(0, "add-diagnostic", "next-id", "--ids-file", missing, "--prefix", "ABC", "--band", "1");

        Assert.AreEqual("ABC1001", json["id"]!.ToString());
        Assert.IsFalse(json["idsFileExists"]!.GetValue<bool>());
    }

    /// <summary>
    /// Guarantees a repository with no IDs and no band headers is told to pass a prefix rather than being given an
    /// invented one, since the prefix goes into every ID the repository will ever have.
    /// </summary>
    [TestMethod]
    public void AFileWithNothingToInferFromAsksForAPrefix()
    {
        var file = this._repo.Write("DiagnosticIds.cs", "internal static class DiagnosticIds { }");

        var json = Tool.Json(1, "add-diagnostic", "next-id", "--ids-file", file);

        Assert.Contains("--prefix", json["hint"]!.ToString());
    }

    /// <summary>
    /// Guarantees the band headers the file carries are reported alongside the ID, so the skill can name the
    /// category a band belongs to without parsing the file itself.
    /// </summary>
    [TestMethod]
    public void TheBandHeadersInTheFileAreReported()
    {
        var file = this._repo.Write("DiagnosticIds.cs", Ids);

        var bands = Tool.Json(0, "add-diagnostic", "next-id", "--ids-file", file, "--category", "Design")["knownBands"]!.AsObject();

        Assert.AreEqual(1, bands["Design"]!.GetValue<int>());
        Assert.AreEqual(2, bands["Usage"]!.GetValue<int>());
    }
}
