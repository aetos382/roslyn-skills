using System;
using System.IO;
using System.Text;

using Aetos.RoslynSkills.Tools.Internal;

namespace Aetos.RoslynSkills.Tools.Tests;

[TestClass]
public sealed class TextTests(TestContext testContext) : IDisposable
{
    private readonly TempRepository _repo = new(testContext);

    public void Dispose()
    {
        this._repo.Dispose();
    }

    /// <summary>
    /// Guarantees a byte order mark is reported separately and kept out of the content, so an edited resx is
    /// written back with the same preamble and its first element still parses.
    /// </summary>
    [TestMethod]
    public void AByteOrderMarkIsReportedAndStrippedFromTheContent()
    {
        var path = Path.Combine(this._repo.Root, "Resources.resx");
        File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("<root />")]);

        var (content, hasBom, _) = Text.ReadPreserving(path);

        Assert.IsTrue(hasBom);
        Assert.AreEqual("<root />", content);
    }

    /// <summary>Guarantees a file with no byte order mark is not reported as having one.</summary>
    [TestMethod]
    public void AFileWithoutAByteOrderMarkIsReportedAsSuch()
    {
        var path = this._repo.Write("Resources.resx", "<root />");

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
        var crlf = this._repo.Write("Crlf.resx", "<root>\r\n</root>");
        var lf = this._repo.Write("Lf.resx", "<root>\n</root>");

        Assert.AreEqual("\r\n", Text.ReadPreserving(crlf).NewLine);
        Assert.AreEqual("\n", Text.ReadPreserving(lf).NewLine);
    }

    /// <summary>
    /// Guarantees a single-line file still reports a line ending, since an inserted entry needs one and there is
    /// nothing in the file to copy: "\n" is the answer, which is also what a one-line resx written on Windows gets.
    /// </summary>
    [TestMethod]
    public void AFileWithNoLineEndingReportsTheLineFeed()
    {
        var path = this._repo.Write("Resources.resx", "<root />");

        Assert.AreEqual("\n", Text.ReadPreserving(path).NewLine);
    }

    /// <summary>
    /// Guarantees a file whose line endings are mixed is treated as a CRLF file: one CRLF anywhere means the file
    /// is edited on Windows, and inserting CRLF there leaves the LF lines alone rather than converting the file.
    /// </summary>
    [TestMethod]
    public void AMixedFileIsTreatedAsCarriageReturnLineFeed()
    {
        var leadingLf = this._repo.Write("Leading.resx", "<root>\n  <data />\r\n</root>");

        Assert.AreEqual("\r\n", Text.ReadPreserving(leadingLf).NewLine);
    }
}
