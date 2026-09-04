using System;
using System.Linq;

using Aetos.RoslynSkills.Tools.Internal;

namespace Aetos.RoslynSkills.Tools.Tests;

[TestClass]
public sealed class CSharpSourceTests
{
    // lang=c#
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

        Assert.AreSequenceEqual(["Resources", "Localizable"], classes);
    }

    /// <summary>Guarantees a sibling class the member does not live in is not reported as enclosing it.</summary>
    [TestMethod]
    public void ASiblingClassIsNotReported()
    {
        var classes = CSharpSource.Parse(Nested).ContainingClasses(Nested.IndexOf("\"x\"", StringComparison.Ordinal));

        Assert.AreSequenceEqual(["Elsewhere"], classes);
    }

    /// <summary>Guarantees a position outside every class yields nothing rather than the nearest class.</summary>
    [TestMethod]
    public void APositionOutsideEveryClassYieldsNothing()
    {
        Assert.IsEmpty(CSharpSource.Parse(Nested).ContainingClasses(0));
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

        var (name, baseTypes) = classes.Single();
        Assert.AreEqual("Analyzer", name);
        Assert.AreSequenceEqual(["DiagnosticAnalyzer"], baseTypes);
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
        Assert.AreSequenceEqual(["Resources", "Localizable"], helper.ContainingClasses);

        var member = source.LocalizableStringMembers().Single();
        Assert.AreEqual("DisposableFieldTitle", member.Name);
        Assert.AreEqual("public", member.Accessibility);
        Assert.AreSequenceEqual(["Resources", "Localizable"], member.ContainingClasses);
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

    /// <summary>
    /// Guarantees the IDs class is reported with the accessibility it declares, and that an omitted modifier is
    /// reported as internal rather than as nothing: the value is what a new ID constant is declared with, so it
    /// has to be a keyword that can be written there.
    /// </summary>
    [TestMethod]
    public void TheStaticClassIsReportedWithTheAccessibilityItDeclares()
    {
        Assert.AreEqual("public", CSharpSource.Parse("public static class DiagnosticIds { }").StaticClass().Visibility);

        var (name, visibility) = CSharpSource.Parse("static class DiagnosticIds { }").StaticClass();

        Assert.AreEqual("DiagnosticIds", name);
        Assert.AreEqual("internal", visibility);
    }

    /// <summary>
    /// Guarantees an accessibility a member cannot be reached through is reported as internal, since the value is
    /// pasted into a new declaration: `private static class` would otherwise have the skill write a private
    /// constant that the analyzer referencing it cannot see.
    /// </summary>
    [TestMethod]
    public void AnUnusableAccessibilityIsReportedAsInternal()
    {
        var (name, visibility) = CSharpSource.Parse(
            """
            internal sealed class Analyzer
            {
                private static class DiagnosticIds { }
            }
            """).StaticClass();

        Assert.AreEqual("DiagnosticIds", name);
        Assert.AreEqual("internal", visibility);
    }

    /// <summary>Guarantees a file with no static class is reported as having none rather than as an empty name.</summary>
    [TestMethod]
    public void AFileWithNoStaticClassReportsNoName()
    {
        var (name, visibility) = CSharpSource.Parse("internal sealed class Analyzer { }").StaticClass();

        Assert.IsNull(name);
        Assert.AreEqual("internal", visibility);
    }
}
