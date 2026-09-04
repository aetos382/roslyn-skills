using System;
using System.IO;
using System.Linq;
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

    private static string[] DataNames(string resx)
    {
        return XDocument.Load(resx).Root!.Elements("data").Select(d => d.Attribute("name")!.Value).ToArray();
    }
}
