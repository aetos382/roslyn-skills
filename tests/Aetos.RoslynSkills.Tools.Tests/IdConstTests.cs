using Aetos.RoslynSkills.Tools.AddDiagnostic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Aetos.RoslynSkills.Tools.Tests;

[TestClass]
public sealed class IdConstTests
{
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

        Assert.AreEqual(2, ids.Count);
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

        Assert.AreEqual(0, ids.Count);
    }
}
