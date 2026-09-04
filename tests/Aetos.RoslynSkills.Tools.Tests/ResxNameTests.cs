using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Aetos.RoslynSkills.Tools.Tests;

[TestClass]
public sealed class ResxNameTests
{
    /// <summary>
    /// Guarantees a satellite file is split into the base name its neutral file shares and the culture it
    /// translates, because that base name is what groups the two files together.
    /// </summary>
    [TestMethod]
    public void ASatelliteIsSplitIntoItsBaseNameAndCulture()
    {
        Assert.AreEqual(("Resources", "ja"), ResxName.Split("src/X/Resources.ja.resx"));
        Assert.AreEqual(("Resources", "pt-BR"), ResxName.Split("src/X/Resources.pt-BR.resx"));
        Assert.AreEqual(("Resources", "zh-Hans"), ResxName.Split("src/X/Resources.zh-Hans.resx"));
    }

    /// <summary>
    /// Guarantees a culture carrying a region or script is accepted without being listed, because that shape
    /// is written by nothing but a culture and the list would otherwise have to enumerate every combination.
    /// </summary>
    [TestMethod]
    public void ACultureWithARegionOrScriptNeedsNoList()
    {
        Assert.AreEqual(("Resources", "ja-JP"), ResxName.Split("src/X/Resources.ja-JP.resx"));
        Assert.AreEqual(("Resources", "es-419"), ResxName.Split("src/X/Resources.es-419.resx"));
        Assert.AreEqual(("Resources", "zh-Hant-TW"), ResxName.Split("src/X/Resources.zh-Hant-TW.resx"));
        Assert.AreEqual(("Resources", "kn-IN"), ResxName.Split("src/X/Resources.kn-IN.resx"));
    }

    /// <summary>Guarantees the neutral file reports its whole stem, so it is the group's base and has no culture.</summary>
    [TestMethod]
    public void TheNeutralFileHasNoCulture()
    {
        Assert.AreEqual(("Resources", ""), ResxName.Split("src/X/Resources.resx"));
    }

    /// <summary>
    /// Guarantees a suffix that is not a culture on the list is left as part of the base name, so a dotted
    /// file name is not mistaken for a translation of a file that does not exist.
    /// </summary>
    [TestMethod]
    public void ASuffixThatIsNotAKnownCultureStaysPartOfTheBaseName()
    {
        Assert.AreEqual(("Analyzer.rules", ""), ResxName.Split("src/X/Analyzer.rules.resx"));
        Assert.AreEqual(("Resources.foo", ""), ResxName.Split("src/X/Resources.foo.resx"));
        Assert.AreEqual(("Resources.Designer", ""), ResxName.Split("src/X/Resources.Designer.resx"));
        Assert.AreEqual(("Resources.web-app", ""), ResxName.Split("src/X/Resources.web-app.resx"));
    }
}
