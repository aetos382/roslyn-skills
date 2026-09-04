using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RoslynSkills.AddDiagnostic.Scripts.Tests;

[TestClass]
public sealed class TextTests(TestContext testContext) : IDisposable
{
    private readonly TempRepository _repo = new(testContext);

    public void Dispose() => _repo.Dispose();

    /// <summary>
    /// Guarantees a byte order mark is reported separately and kept out of the content, so an edited resx is
    /// written back with the same preamble and its first element still parses.
    /// </summary>
    [TestMethod]
    public void AByteOrderMarkIsReportedAndStrippedFromTheContent()
    {
        var path = Path.Combine(_repo.Root, "Resources.resx");
        File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("<root />")]);

        var (content, hasBom, _) = Text.ReadPreserving(path);

        Assert.IsTrue(hasBom);
        Assert.AreEqual("<root />", content);
    }

    /// <summary>Guarantees a file with no byte order mark is not reported as having one.</summary>
    [TestMethod]
    public void AFileWithoutAByteOrderMarkIsReportedAsSuch()
    {
        var path = _repo.Write("Resources.resx", "<root />");

        var (content, hasBom, _) = Text.ReadPreserving(path);

        Assert.IsFalse(hasBom);
        Assert.AreEqual("<root />", content);
    }

    /// <summary>
    /// Guarantees the file's own line ending is reported, because an inserted resx entry has to use it or the
    /// whole file shows up as changed.
    /// </summary>
    [TestMethod]
    public void TheLineEndingAlreadyInTheFileIsReported()
    {
        var crlf = _repo.Write("Crlf.resx", "<root>\r\n</root>");
        var lf = _repo.Write("Lf.resx", "<root>\n</root>");

        Assert.AreEqual("\r\n", Text.ReadPreserving(crlf).NewLine);
        Assert.AreEqual("\n", Text.ReadPreserving(lf).NewLine);
    }
}
