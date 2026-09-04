using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RoslynSkills.AddDiagnostic.Scripts.Tests;

[TestClass]
public sealed class CliArgsTests
{
    /// <summary>Guarantees both spellings a caller may use for a value are accepted.</summary>
    [TestMethod]
    public void SeparateAndInlineValuesAreBothAccepted()
    {
        var cli = new CliArgs(["--ids-file", "Ids.cs", "--prefix=CTS"]);

        Assert.AreEqual("Ids.cs", cli.Get("ids-file"));
        Assert.AreEqual("CTS", cli.Get("prefix"));
    }

    /// <summary>
    /// Guarantees a repeated option resolves to the last value while the full list stays available in order,
    /// which is what AddResxEntries.cs relies on to take one --input per culture.
    /// </summary>
    [TestMethod]
    public void TheLastValueWinsAndGetAllKeepsEveryValueInOrder()
    {
        var cli = new CliArgs(["--input", "a.json", "--input", "b.json"]);

        Assert.AreEqual("b.json", cli.Get("input"));
        CollectionAssert.AreEqual(new[] { "a.json", "b.json" }, cli.GetAll("input").ToArray());
    }

    /// <summary>Guarantees a declared switch takes no value, leaving the next argument to be parsed on its own.</summary>
    [TestMethod]
    public void ADeclaredSwitchDoesNotConsumeTheNextArgument()
    {
        var cli = new CliArgs(["--summary", "--path", "."], "summary");

        Assert.IsTrue(cli.Has("summary"));
        Assert.AreEqual(".", cli.Get("path"));
    }

    /// <summary>
    /// Guarantees an undeclared switch is taken for an option expecting a value and swallows the argument
    /// after it, which is why every switch has to be named in the constructor. The option's own value is
    /// then left over as a bare argument, so the mistake surfaces as an error rather than a wrong result.
    /// </summary>
    [TestMethod]
    public void AnUndeclaredSwitchSwallowsTheNextArgument()
    {
        var cli = new CliArgs(["--summary", "--path"]);

        Assert.AreEqual("--path", cli.Get("summary"));
        Assert.IsNull(cli.Get("path"));
        Assert.ThrowsExactly<ArgumentException>(() => new CliArgs(["--summary", "--path", "."]));
    }

    /// <summary>Guarantees an option that carries a value also answers Has, so --flag=true works like --flag.</summary>
    [TestMethod]
    public void HasIsTrueForAnOptionThatCarriesAValue()
    {
        var cli = new CliArgs(["--suppression=true"], "suppression");

        Assert.IsTrue(cli.Has("suppression"));
    }

    /// <summary>Guarantees option names are matched without regard to case.</summary>
    [TestMethod]
    public void OptionNamesAreCaseInsensitive()
    {
        var cli = new CliArgs(["--IDS-FILE", "Ids.cs"]);

        Assert.AreEqual("Ids.cs", cli.Get("ids-file"));
    }

    /// <summary>Guarantees GetInt parses the value and reports an absent option as null rather than zero.</summary>
    [TestMethod]
    public void GetIntParsesTheValueAndReturnsNullWhenAbsent()
    {
        var cli = new CliArgs(["--digits", "5"]);

        Assert.AreEqual(5, cli.GetInt("digits"));
        Assert.IsNull(cli.GetInt("band"));
    }

    /// <summary>
    /// Guarantees a mistyped command line fails loudly: a bare argument and a trailing option with no value
    /// both throw, rather than being dropped and producing a result computed from the wrong input.
    /// </summary>
    [TestMethod]
    public void MalformedArgumentsThrow()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CliArgs(["Ids.cs"]));
        Assert.ThrowsExactly<ArgumentException>(() => new CliArgs(["--ids-file"]));
    }

    /// <summary>Guarantees a required option that was not passed throws instead of yielding null downstream.</summary>
    [TestMethod]
    public void RequireThrowsWhenTheOptionIsAbsent()
    {
        var cli = new CliArgs([]);

        Assert.ThrowsExactly<ArgumentException>(() => cli.Require("ids-file"));
    }
}
