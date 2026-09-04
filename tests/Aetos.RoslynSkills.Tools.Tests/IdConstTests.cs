using System.Linq;

using Aetos.RoslynSkills.Tools.AddDiagnostic;

namespace Aetos.RoslynSkills.Tools.Tests;

[TestClass]
public sealed class IdConstTests
{
    // lang=c#
    private const string IdsFile =
        """
        internal static class DiagnosticIds
        {
            // Design (CTS1xxx)
            public const string DisposableField = "CTS1001";

            // Usage (CTS2xxx)
            internal const string BadUsage = "CTS2001";
        }
        """;

    /// <summary>
    /// Guarantees each ID constant is reported with the pieces the ID arithmetic needs — letters, number,
    /// digit count — and with the line it sits on, which is where a new constant gets inserted.
    /// </summary>
    [TestMethod]
    public void EveryPieceOfAnIdConstantIsReportedIncludingItsLine()
    {
        var ids = IdConst.Parse(IdsFile);

        Assert.HasCount(2, ids);
        Assert.AreEqual("DisposableField", ids[0].Name);
        Assert.AreEqual("CTS1001", ids[0].Value);
        Assert.AreEqual("CTS", ids[0].Letters);
        Assert.AreEqual(1001, ids[0].Number);
        Assert.AreEqual(4, ids[0].Digits);
        Assert.AreEqual(4, ids[0].Line);
        Assert.AreEqual(7, ids[1].Line);
    }

    /// <summary>
    /// Guarantees a suppression ID is told apart from a diagnostic ID by the extra S in front of the number,
    /// so the two sets never share a numbering sequence.
    /// </summary>
    [TestMethod]
    public void ASuppressionIdIsDistinguishedByTheTrailingS()
    {
        var ids = IdConst.Parse(
            """
            internal static class SuppressionIds
            {
                public const string SuppressBadUsage = "CTSS0001";
            }
            """);

        var id = ids.Single();
        Assert.AreEqual("CTSS", id.Letters);
        Assert.AreEqual(1, id.Number);
        Assert.IsTrue(id.IsSuppressionOf("CTS"));
        Assert.IsFalse(id.IsDiagnosticOf("CTS"));
    }

    /// <summary>
    /// Guarantees the prefix is inferred from the letters most of the IDs share, so a repository that has vendored or
    /// copied a stray ID of another prefix is not renamed after it.
    /// </summary>
    [TestMethod]
    public void ThePrefixIsTheLettersMostIdsShare()
    {
        var ids = IdConst.Parse(
            """
            internal static class DiagnosticIds
            {
                public const string First = "CTS1001";
                public const string Second = "CTS1002";
                public const string Borrowed = "RS1001";
            }
            """);

        Assert.AreEqual("CTS", IdConst.InferPrefix(ids));
    }

    /// <summary>
    /// Guarantees the suppression letters are not inferred as the prefix when the diagnostics they suppress are there
    /// too: CTSS is CTS plus the suppression S, and inferring it would make every later ID a CTSS one.
    /// </summary>
    [TestMethod]
    public void TheSuppressionLettersAreNotTakenForThePrefix()
    {
        var ids = IdConst.Parse(
            """
            internal static class Ids
            {
                public const string DisposableField = "CTS1001";
                public const string SuppressA = "CTSS0001";
                public const string SuppressB = "CTSS0002";
                public const string SuppressC = "CTSS0003";
            }
            """);

        Assert.AreEqual("CTS", IdConst.InferPrefix(ids), "the suppressions outnumber the diagnostics and still lose");
    }

    /// <summary>
    /// Guarantees a tie is broken by the alphabet rather than by the order the files happened to be scanned in, so
    /// two runs over the same repository infer the same prefix.
    /// </summary>
    [TestMethod]
    public void ATieIsBrokenByTheAlphabetSoTheAnswerIsStable()
    {
        var ids = IdConst.Parse(
            """
            internal static class DiagnosticIds
            {
                public const string Zzz = "ZZZ1001";
                public const string Abc = "ABC1001";
            }
            """);

        Assert.AreEqual("ABC", IdConst.InferPrefix(ids));
    }

    /// <summary>
    /// Guarantees nothing is inferred from no IDs, since the caller has to ask for a prefix rather than be handed an
    /// invented one that then appears in every ID the repository ships.
    /// </summary>
    [TestMethod]
    public void NoIdsMeansNoPrefix()
    {
        Assert.IsNull(IdConst.InferPrefix([]));
    }

    /// <summary>
    /// Guarantees constants that merely look similar are left alone, so a category name or a version string
    /// is never counted as a diagnostic ID and the next ID is not computed from it.
    /// </summary>
    [TestMethod]
    public void ConstantsThatAreNotIdsAreIgnored()
    {
        var ids = IdConst.Parse(
            """
            internal static class Other
            {
                public const string Design = "Design";
                public const string LowerCase = "cts1001";
                public const int Number = 1001;
                public static readonly string NotConst = "CTS1001";
            }
            """);

        Assert.IsEmpty(ids);
    }
}
