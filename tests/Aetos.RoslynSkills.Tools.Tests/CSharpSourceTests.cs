using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Aetos.RoslynSkills.Tools.Tests;

[TestClass]
public sealed class CSharpSourceTests
{
    private const string Nested =
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
        var classes = CSharpSource.Parse(Nested)
            .ContainingClasses(Nested.IndexOf("DisposableFieldTitle", StringComparison.Ordinal));

        CollectionAssert.AreEqual(new[] { "Resources", "Localizable" }, classes);
    }

    /// <summary>Guarantees a sibling class the member does not live in is not reported as enclosing it.</summary>
    [TestMethod]
    public void ASiblingClassIsNotReported()
    {
        var classes = CSharpSource.Parse(Nested).ContainingClasses(Nested.IndexOf("\"x\"", StringComparison.Ordinal));

        CollectionAssert.AreEqual(new[] { "Elsewhere" }, classes);
    }

    /// <summary>Guarantees a position outside every class yields nothing rather than the nearest class.</summary>
    [TestMethod]
    public void APositionOutsideEveryClassYieldsNothing()
    {
        Assert.AreEqual(0, CSharpSource.Parse(Nested).ContainingClasses(0).Count);
    }

    /// <summary>
    /// Guarantees a declaration quoted inside a string literal is not read as a declaration, so a file that
    /// carries a code template is not mined for the IDs and classes the template merely illustrates.
    /// </summary>
    [TestMethod]
    public void ADeclarationInsideAStringLiteralIsNotADeclaration()
    {
        var source = CSharpSource.Parse(
            """"
            internal static class Template
            {
                public const string Snippet =
                    """
                    internal static class Fake
                    {
                        public const string Id = "CTS9001";
                    }
                    """;
            }
            """");

        Assert.AreEqual("Template", source.FirstClassName());
        Assert.AreEqual("Snippet", source.ConstStrings().Single().Name);
    }

    /// <summary>
    /// Guarantees a declaration inside a region no ordinary build compiles is not reported, so a debug-only
    /// ID cannot be taken for a released one when the next ID is computed.
    /// </summary>
    [TestMethod]
    public void ADeclarationInADisabledRegionIsNotReported()
    {
        var source = CSharpSource.Parse(
            """
            internal static class DiagnosticIds
            {
                public const string Released = "CTS1001";
            #if DEBUG
                public const string Experimental = "CTS9001";
            #endif
            }
            """);

        Assert.AreEqual("Released", source.ConstStrings().Single().Name);
    }

    /// <summary>
    /// Guarantees a class is reported as playing a role only when it really derives from the role's base type,
    /// so a file that just mentions the type in a comment or a type argument is not mistaken for one.
    /// </summary>
    [TestMethod]
    public void OnlyARealBaseListCountsAsDerivingFromAType()
    {
        var classes = CSharpSource.Parse(
            """
            // A DiagnosticAnalyzer lives here.
            internal sealed class Analyzer : DiagnosticAnalyzer
            {
            }

            internal sealed class Holder
            {
                private List<DiagnosticAnalyzer> analyzers = new();
            }
            """).ClassesWithBaseTypes().ToList();

        var only = classes.Single();
        Assert.AreEqual("Analyzer", only.Name);
        CollectionAssert.AreEqual(new[] { "DiagnosticAnalyzer" }, only.BaseTypes);
    }

    /// <summary>
    /// Guarantees the resource helper and the strings it feeds are both found even when the strings are
    /// fields rather than properties, because either shape is what a new descriptor has to be written to match.
    /// </summary>
    [TestMethod]
    public void LocalizableStringFieldsAreFoundAlongsideTheHelper()
    {
        var source = CSharpSource.Parse(
            """
            internal static class Resources
            {
                internal static class Localizable
                {
                    public static readonly LocalizableResourceString DisposableFieldTitle = Get("DisposableFieldTitle");

                    private static LocalizableResourceString Get(string name) => new(name, ResourceManager, typeof(Resources));
                }
            }
            """);

        var helper = source.LocalizableStringHelper();
        Assert.IsNotNull(helper);
        Assert.AreEqual("Get", helper.Name);
        Assert.AreEqual("private", helper.Accessibility);
        CollectionAssert.AreEqual(new[] { "Resources", "Localizable" }, helper.ContainingClasses);

        var member = source.LocalizableStringMembers().Single();
        Assert.AreEqual("DisposableFieldTitle", member.Name);
        Assert.AreEqual("public", member.Accessibility);
        CollectionAssert.AreEqual(new[] { "Resources", "Localizable" }, member.ContainingClasses);
    }

    /// <summary>
    /// Guarantees an assembly-level attribute is read whether or not the source writes the Attribute suffix,
    /// since the neutral language decides which resx file the new entry belongs in.
    /// </summary>
    [TestMethod]
    public void AnAssemblyAttributeIsReadWithOrWithoutTheSuffix()
    {
        Assert.AreEqual("ja", CSharpSource.Parse("""[assembly: NeutralResourcesLanguage("ja")]""")
            .AssemblyAttributeArgument("NeutralResourcesLanguage"));
        Assert.AreEqual("en", CSharpSource.Parse("""[assembly: System.Resources.NeutralResourcesLanguageAttribute("en")]""")
            .AssemblyAttributeArgument("NeutralResourcesLanguage"));
        Assert.IsNull(CSharpSource.Parse("// [assembly: NeutralResourcesLanguage(\"ja\")]")
            .AssemblyAttributeArgument("NeutralResourcesLanguage"));
    }
}
