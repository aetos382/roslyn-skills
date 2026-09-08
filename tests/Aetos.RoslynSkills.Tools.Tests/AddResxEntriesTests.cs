using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Aetos.RoslynSkills.Tools.Tests;

/// <summary>
/// Adding resx entries. The file is rewritten in place, so where an entry lands and what the run reports about the
/// result are the whole contract: a wrongly placed or silently dropped entry is a missing message at run time.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class AddResxEntriesTests(TestContext testContext) : IDisposable
{
    private const string Empty =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <root>
          <resheader name="version">
            <value>2.0</value>
          </resheader>
        </root>
        """;

    private readonly TempRepository _repo = new(testContext);

    public void Dispose()
    {
        this._repo.Dispose();
    }

    /// <summary>
    /// Guarantees a new entry is placed by ID rather than appended: one that sorts before every existing entry goes
    /// to the front, one that sorts after an existing entry goes behind it, and the entries of one diagnostic stay in
    /// Title -> Message order. Appending instead would leave the file unordered, which is what the ordering is for.
    /// </summary>
    [TestMethod]
    public void ANewEntryIsPlacedWhereItsIdSorts()
    {
        var ids = this._repo.Write("DiagnosticIds.cs",
            """
            internal static class DiagnosticIds
            {
                public const string ZzzRule = "ABC1001";
                public const string AaaRule = "ABC1002";
                public const string MmmRule = "ABC1003";
            }
            """);
        var file = this._repo.Write("Resources.resx",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <root>
              <resheader name="version">
                <value>2.0</value>
              </resheader>
              <data name="AaaRuleTitle" xml:space="preserve">
                <value>Aaa</value>
              </data>
            </root>
            """);

        var report = Tool.Report(0, "add-diagnostic", "add-resx-entries", "--resx", file, "--ids-file", ids,
            "--entries",
            /*lang=json,strict*/
            """
            [
              { "name": "MmmRuleTitle", "value": "Mmm" },
              { "name": "ZzzRuleMessage", "value": "Zzz message" },
              { "name": "ZzzRuleTitle", "value": "Zzz" }
            ]
            """);

        Assert.HasCount(3, report[0]!["added"]!.AsArray());
        Assert.AreSequenceEqual(
            ["ZzzRuleTitle", "ZzzRuleMessage", "AaaRuleTitle", "MmmRuleTitle"],
            DataNames(file));
    }

    /// <summary>
    /// Guarantees the indentation of a new entry is copied from the file rather than assumed to be two spaces, so a
    /// tab-indented resx does not come back with one entry indented differently from every other.
    /// </summary>
    [TestMethod]
    public void TheIndentationOfTheFileIsCopiedForTheFirstEntry()
    {
        var file = this._repo.Write("Resources.resx", string.Join('\n', [
            """<?xml version="1.0" encoding="utf-8"?>""",
            "<root>",
            "\t<resheader name=\"version\">",
            "\t\t<value>2.0</value>",
            "\t</resheader>",
            "</root>",
        ]));

        Tool.Report(0, "add-diagnostic", "add-resx-entries", "--resx", file,
            "--entries", /*lang=json,strict*/ """[{ "name": "ABC1001Title", "value": "Title" }]""");

        var lines = File.ReadAllLines(file);
        var i = Array.FindIndex(lines, l => l.Contains("<data name=\"ABC1001Title\"", StringComparison.Ordinal));
        Assert.AreEqual("\t", lines[i][..1]);
        Assert.AreEqual("\t\t<value>Title</value>", lines[i + 1]);
    }

    /// <summary>
    /// Guarantees an inserted entry uses the line ending the file already has, in both directions. The answer is
    /// taken from the whitespace the parser kept rather than from the file's bytes, so this is also what says that
    /// whitespace survives the parse: an entry added with the wrong line ending would either show the whole file as
    /// changed on the next commit, or leave one line of it disagreeing with every other.
    /// </summary>
    [TestMethod]
    public void AnInsertedEntryUsesTheLineEndingTheFileAlreadyHas()
    {
        var entries = /*lang=json,strict*/ """[{ "name": "ABC1001Title", "value": "Title" }]""";

        var crlf = this._repo.Write("Crlf.resx", Empty.ReplaceLineEndings("\r\n"));
        Tool.Report(0, "add-diagnostic", "add-resx-entries", "--resx", crlf, "--entries", entries);
        var crlfText = File.ReadAllText(crlf);

        Assert.Contains("ABC1001Title", crlfText, StringComparison.Ordinal);
        // Normalizing every line ending to CRLF changes nothing only when they are all CRLF already.
        Assert.AreEqual(crlfText, crlfText.ReplaceLineEndings("\r\n"));

        var lf = this._repo.Write("Lf.resx", Empty.ReplaceLineEndings("\n"));
        Tool.Report(0, "add-diagnostic", "add-resx-entries", "--resx", lf, "--entries", entries);
        var lfText = File.ReadAllText(lf);

        Assert.Contains("ABC1001Title", lfText, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", lfText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Guarantees an entry can be added to a resx written on one line, where there is no whitespace to insert before:
    /// the entry has to go inside &lt;root&gt; either way, and a file that no longer parses is worse than an ugly one.
    /// </summary>
    [TestMethod]
    public void AResxWrittenOnOneLineStillParsesAfterTheEntryIsAdded()
    {
        var file = this._repo.Write("Resources.resx",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <root><resheader name="version"><value>2.0</value></resheader></root>
            """);

        var report = Tool.Report(0, "add-diagnostic", "add-resx-entries", "--resx", file,
            "--entries", /*lang=json,strict*/ """[{ "name": "ABC1001Title", "value": "Title" }]""");

        Assert.IsTrue(report[0]!["valid"]!.GetValue<bool>());
        Assert.AreSequenceEqual(["ABC1001Title"], DataNames(file));
    }

    /// <summary>
    /// Guarantees an entry that is already there is left alone unless --force is passed, since the existing value may
    /// be a translation: a re-run of the skill must not overwrite it, and --force must not be ignored either.
    /// </summary>
    [TestMethod]
    public void AnExistingEntryIsSkippedUnlessForceIsPassed()
    {
        var file = this._repo.Write("Resources.resx",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <root>
              <data name="ABC1001Title" xml:space="preserve">
                <value>Translated</value>
              </data>
            </root>
            """);
        var entries = /*lang=json,strict*/ """[{ "name": "ABC1001Title", "value": "Title" }]""";

        var skipping = Tool.Report(0, "add-diagnostic", "add-resx-entries", "--resx", file, "--entries", entries);

        Assert.AreSequenceEqual(["ABC1001Title"], skipping[0]!["skipped"]!.AsArray().Select(n => n!.ToString()).ToArray());
        Assert.IsEmpty(skipping[0]!["added"]!.AsArray());
        Assert.Contains("Translated", File.ReadAllText(file));

        var forcing = Tool.Report(0, "add-diagnostic", "add-resx-entries", "--resx", file, "--entries", entries, "--force");

        Assert.AreSequenceEqual(["ABC1001Title"], forcing[0]!["updated"]!.AsArray().Select(n => n!.ToString()).ToArray());
        Assert.DoesNotContain("Translated", File.ReadAllText(file));
    }

    /// <summary>
    /// Guarantees a file that is not a resx is reported as a problem with exit code 1, rather than being written to or
    /// passed over quietly: the skill decides whether to carry on from that exit code.
    /// </summary>
    [TestMethod]
    public void AFileThatIsNotAResxIsReportedAsInvalid()
    {
        var file = this._repo.Write("App.config", "<configuration />");

        var report = Tool.Report(1, "add-diagnostic", "add-resx-entries", "--resx", file, "--validate-only");

        Assert.IsFalse(report[0]!["valid"]!.GetValue<bool>());
        Assert.Contains("configuration", report[0]!["problems"]!.AsArray()[0]!.ToString());
    }

    /// <summary>
    /// Guarantees the validation pass reports the two ways an existing resx is broken in a way the compiler would not
    /// catch — a duplicate name and a &lt;data&gt; with no value — instead of reporting the file as valid.
    /// </summary>
    [TestMethod]
    public void ADuplicateNameAndAValuelessEntryAreBothReported()
    {
        var file = this._repo.Write("Resources.resx",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <root>
              <data name="ABC1001Title" xml:space="preserve">
                <value>Title</value>
              </data>
              <data name="ABC1001Title" xml:space="preserve">
                <value>Title</value>
              </data>
              <data name="ABC1002Title" xml:space="preserve" />
            </root>
            """);

        var problems = Tool.Report(1, "add-diagnostic", "add-resx-entries", "--resx", file, "--validate-only")[0]!["problems"]!
            .AsArray().Select(p => p!.ToString()).ToArray();

        Assert.ContainsSingle(problems.Where(p => p.Contains("duplicate data names: ABC1001Title", StringComparison.Ordinal)));
        Assert.ContainsSingle(problems.Where(p => p.Contains("'ABC1002Title' has no <value>", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Guarantees a broken file among several does not stop the others from being written or reported on: the run is
    /// not transactional, so the report has to say what happened to every file, not only to the one that failed.
    /// </summary>
    [TestMethod]
    public void AFileThatFailsDoesNotHideWhatHappenedToTheOthers()
    {
        var good = this._repo.Write("Resources.resx", Empty);
        var broken = this._repo.Write("Resources.ja.resx", "<root><data name=");

        var report = Tool.Report(1, "add-diagnostic", "add-resx-entries", "--resx", good, "--resx", broken,
            "--entries", /*lang=json,strict*/ """[{ "name": "ABC1001Title", "value": "Title" }]""");

        Assert.HasCount(2, report);
        Assert.IsTrue(report[0]!["valid"]!.GetValue<bool>());
        Assert.Contains("ABC1001Title", File.ReadAllText(good));
        Assert.IsFalse(report[1]!["valid"]!.GetValue<bool>());
        Assert.Contains("read failed", report[1]!["problems"]!.AsArray()[0]!.ToString());
    }

    /// <summary>
    /// Guarantees a resx whose bytes are not text in the encoding it claims is left exactly as it was and reported,
    /// instead of being read through a decoder that replaces what it cannot make sense of and then written back.
    /// The file is committed translated text, the replacement is not reversible, and git would show the damage as
    /// this run's work.
    /// </summary>
    [TestMethod]
    public void AResxWhoseBytesAreNotTextIsReportedAndLeftAlone()
    {
        var file = Path.Combine(this._repo.Root, "Resources.ja.resx");

        // What Shift_JIS would hold for あ: 0x82 0xA0, which is not a UTF-8 sequence, in a file claiming UTF-8.
        byte[] original =
        [
            .. """<?xml version="1.0" encoding="utf-8"?><root><data name="ABC1001Title"><value>"""u8,
            0x82, 0xA0,
            .. "</value></data></root>"u8,
        ];
        File.WriteAllBytes(file, original);

        var report = Tool.Report(1, "add-diagnostic", "add-resx-entries", "--resx", file,
            "--entries", /*lang=json,strict*/ """[{ "name": "ABC1002Title", "value": "Title" }]""");

        Assert.IsFalse(report[0]!["valid"]!.GetValue<bool>());
        Assert.Contains("read failed", report[0]!["problems"]!.AsArray()[0]!.ToString());
        Assert.AreSequenceEqual(original, File.ReadAllBytes(file));
    }

    /// <summary>
    /// Guarantees a resx in an encoding other than UTF-8 is read through what it declares — non-ASCII text intact —
    /// and written back as UTF-8. Nothing detects the encoding by hand: the parser does it, which is also what makes
    /// the case above a failure instead of a lossy read.
    /// </summary>
    [TestMethod]
    public void AUtf16ResxIsReadThroughItsDeclarationAndSavedAsUtf8()
    {
        var file = Path.Combine(this._repo.Root, "Resources.resx");
        File.WriteAllBytes(file,
        [
            .. Encoding.Unicode.GetPreamble(),
            .. Encoding.Unicode.GetBytes("""<?xml version="1.0" encoding="utf-16"?><root><data name="ABC1001Title"><value>あ</value></data></root>"""),
        ]);

        var report = Tool.Report(0, "add-diagnostic", "add-resx-entries", "--resx", file,
            "--entries", /*lang=json,strict*/ """[{ "name": "ABC1002Title", "value": "い" }]""");

        Assert.IsTrue(report[0]!["valid"]!.GetValue<bool>());
        Assert.AreSequenceEqual("<?xml"u8.ToArray(), File.ReadAllBytes(file).Take(5));
        Assert.AreSequenceEqual(
            ["あ", "い"],
            XDocument.Load(file).Root!.Elements("data").Select(d => d.Element("value")!.Value).ToArray());
    }

    /// <summary>
    /// Guarantees a resx declaring an encoding this runtime has no provider for is reported and left alone, rather
    /// than read as something else. .NET carries no code pages beyond the Unicode encodings, ASCII and Latin-1
    /// unless a program registers them, so a `shift_jis` resx lands here even when its bytes are all ASCII.
    /// </summary>
    [TestMethod]
    public void AResxDeclaringAnEncodingTheRuntimeLacksIsReportedAndLeftAlone()
    {
        var file = this._repo.Write("Resources.resx",
            """
            <?xml version="1.0" encoding="shift_jis"?>
            <root>
              <resheader name="version">
                <value>2.0</value>
              </resheader>
            </root>
            """);
        var original = File.ReadAllBytes(file);

        var report = Tool.Report(1, "add-diagnostic", "add-resx-entries", "--resx", file,
            "--entries", /*lang=json,strict*/ """[{ "name": "ABC1001Title", "value": "Title" }]""");

        Assert.IsFalse(report[0]!["valid"]!.GetValue<bool>());
        Assert.Contains("read failed", report[0]!["problems"]!.AsArray()[0]!.ToString());
        Assert.AreSequenceEqual(original, File.ReadAllBytes(file));
    }

    /// <summary>
    /// Guarantees a resx carrying a UTF-8 byte order mark — what Visual Studio writes — is edited normally and
    /// comes back without the mark. Dropping it is deliberate rather than an oversight: the mark is optional in
    /// XML, so the file is worth one line of diff once instead of a code path that reproduces it forever.
    /// </summary>
    [TestMethod]
    public void AByteOrderMarkIsNotWrittenBack()
    {
        var file = Path.Combine(this._repo.Root, "Resources.resx");
        File.WriteAllBytes(file, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(Empty)]);

        var report = Tool.Report(0, "add-diagnostic", "add-resx-entries", "--resx", file,
            "--entries", /*lang=json,strict*/ """[{ "name": "ABC1001Title", "value": "あ" }]""");

        Assert.IsTrue(report[0]!["valid"]!.GetValue<bool>());
        Assert.AreEqual((byte)'<', File.ReadAllBytes(file)[0]);
        Assert.AreEqual("あ", XDocument.Load(file).Root!.Elements("data").Single().Element("value")!.Value);
    }

    /// <summary>
    /// Guarantees a resx whose declaration disagrees with its own bytes is reported and left alone. The declaration
    /// is believed rather than second-guessed, so a file claiming UTF-16 while holding UTF-8 bytes fails to parse,
    /// which is the answer that costs nothing: no reading of those bytes can tell which of the two is the mistake.
    /// </summary>
    [TestMethod]
    public void AResxWhoseDeclarationDisagreesWithItsBytesIsReportedAndLeftAlone()
    {
        var file = this._repo.Write("Resources.resx",
            """
            <?xml version="1.0" encoding="utf-16"?>
            <root>
              <resheader name="version">
                <value>2.0</value>
              </resheader>
            </root>
            """);
        var original = File.ReadAllBytes(file);

        var report = Tool.Report(1, "add-diagnostic", "add-resx-entries", "--resx", file,
            "--entries", /*lang=json,strict*/ """[{ "name": "ABC1001Title", "value": "Title" }]""");

        Assert.IsFalse(report[0]!["valid"]!.GetValue<bool>());
        Assert.Contains("read failed", report[0]!["problems"]!.AsArray()[0]!.ToString());
        Assert.AreSequenceEqual(original, File.ReadAllBytes(file));
    }

    private static string[] DataNames(string resx)
    {
        return XDocument.Load(resx).Root!.Elements("data").Select(d => d.Attribute("name")!.Value).ToArray();
    }
}
