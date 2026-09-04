using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RoslynSkills.AddDiagnostic.Scripts.Tests;

[TestClass]
public sealed class SourceScanTests
{
    private const string Source =
        """
        internal static partial class Resources
        {
            internal static partial class Localizable
            {
                public const string Title = "DisposableFieldTitle";
            }
        }

        internal static class Elsewhere
        {
            public const string Other = "x";
        }
        """;

    /// <summary>
    /// Guarantees the enclosing classes of a member are reported outermost first, because that order is the
    /// nesting a new resource property has to be written into.
    /// </summary>
    [TestMethod]
    public void EnclosingClassesAreReportedOutermostFirst()
    {
        var classes = SourceScan.ContainingClasses(Source, Source.IndexOf("DisposableFieldTitle", StringComparison.Ordinal));

        CollectionAssert.AreEqual(new[] { "Resources", "Localizable" }, classes);
    }

    /// <summary>Guarantees a sibling class the member does not live in is not reported as enclosing it.</summary>
    [TestMethod]
    public void ASiblingClassIsNotReported()
    {
        var classes = SourceScan.ContainingClasses(Source, Source.IndexOf("\"x\"", StringComparison.Ordinal));

        CollectionAssert.AreEqual(new[] { "Elsewhere" }, classes);
    }

    /// <summary>Guarantees a position outside every class yields nothing rather than the nearest class.</summary>
    [TestMethod]
    public void APositionOutsideEveryClassYieldsNothing()
    {
        Assert.AreEqual(0, SourceScan.ContainingClasses(Source, 0).Count);
    }
}
