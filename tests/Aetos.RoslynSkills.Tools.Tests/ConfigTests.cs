using System;

using Aetos.RoslynSkills.Tools.AddDiagnostic;

namespace Aetos.RoslynSkills.Tools.Tests;

[TestClass]
public sealed class ConfigTests(TestContext testContext) : IDisposable
{
    private readonly TempRepository _repo = new(testContext);

    public void Dispose()
    {
        this._repo.Dispose();
    }

    /// <summary>
    /// Guarantees that the settings are taken from the first fenced json block and that every line outside
    /// it — before and after — becomes the notes the skill is told to read, with the block itself gone.
    /// </summary>
    [TestMethod]
    public void SettingsComeFromTheJsonBlockAndEverythingElseBecomesNotes()
    {
        var config = this._repo.WriteConfig(
            """
            # Settings

            Descriptors go through `DescriptorFactory.Create`.

            ```json
            { "diagnosticPrefix": "CTS" }
            ```

            Trailing note.
            """);

        Assert.IsTrue(config.Exists);
        Assert.IsNull(config.Error);
        Assert.AreEqual("CTS", config.Get("diagnosticPrefix"));
        Assert.Contains("DescriptorFactory.Create", config.Body);
        Assert.Contains("Trailing note.", config.Body);
        Assert.DoesNotContain("diagnosticPrefix", config.Body, "the settings block must not be repeated in the notes");
    }

    /// <summary>
    /// Guarantees that a fenced block tagged with another language is not mistaken for the settings, so the
    /// notes may contain code samples.
    /// </summary>
    [TestMethod]
    public void AFenceTaggedWithAnotherLanguageIsNotTheSettings()
    {
        var config = this._repo.WriteConfig(
            """
            ```csharp
            var x = 1;
            ```

            ```json
            { "diagnosticPrefix": "CTS" }
            ```
            """);

        Assert.IsNull(config.Error);
        Assert.AreEqual("CTS", config.Get("diagnosticPrefix"));
    }

    /// <summary>
    /// Guarantees the CommonMark fence variations a Markdown author may reasonably write are all read: up
    /// to three spaces of indentation, tildes instead of backticks, the `jsonc` tag, and a closing fence
    /// longer than the opening one.
    /// </summary>
    [TestMethod]
    public void IndentedTildeAndJsoncFencesAreAccepted()
    {
        var config = this._repo.WriteConfig(string.Join('\n', [
            "notes",
            "   ~~~JSONC",
            /*lang=json,strict*/
            """   { "diagnosticPrefix": "CTS" }""",
            "   ~~~~",
            "more notes",
        ]));

        Assert.IsNull(config.Error);
        Assert.AreEqual("CTS", config.Get("diagnosticPrefix"));
    }

    /// <summary>
    /// Guarantees comments and trailing commas are accepted, which is what lets the block document its own
    /// keys instead of needing prose elsewhere.
    /// </summary>
    [TestMethod]
    public void CommentsAndTrailingCommasAreAccepted()
    {
        var config = this._repo.WriteConfig(
            """
            ```json
            {
              // line comment
              "diagnosticPrefix": "CTS", /* block comment */
              "idDigits": 4,
            }
            ```
            """);

        Assert.IsNull(config.Error);
        Assert.AreEqual("CTS", config.Get("diagnosticPrefix"));
        Assert.AreEqual("4", config.Get("idDigits"));
    }

    /// <summary>
    /// Guarantees a shorter run of backticks inside the block does not close it, which is the CommonMark rule that
    /// lets a value contain a fence at all — closing early would cut the settings in half and report the remainder
    /// as invalid JSON.
    /// </summary>
    [TestMethod]
    public void AShorterFenceInsideTheBlockDoesNotCloseIt()
    {
        var config = this._repo.WriteConfig(string.Join('\n', [
            "````json",
            /*lang=json,strict*/
            """{ "diagnosticPrefix": "CTS", "docsDir": "``` docs/rules" }""",
            "````",
            "notes",
        ]));

        Assert.IsNull(config.Error);
        Assert.AreEqual("CTS", config.Get("diagnosticPrefix"));
        Assert.AreEqual("``` docs/rules", config.Get("docsDir"));
    }

    /// <summary>
    /// Guarantees a fence is only closed by its own character: CommonMark does not let tildes close backticks, so
    /// the block runs on and the settings do not parse. Reported as an error rather than read as far as the tildes,
    /// because the file does not render as the author intended either.
    /// </summary>
    [TestMethod]
    public void TildesDoNotCloseABacktickFence()
    {
        var config = this._repo.WriteConfig(
            """
            ```json
            { "diagnosticPrefix": "CTS" }
            ~~~
            notes
            """);

        Assert.IsNotNull(config.Error);
        Assert.Contains(Config.RelativePath, config.Error);
    }

    /// <summary>
    /// Guarantees an unclosed fence is read to the end of the document rather than reported as an error,
    /// which is how CommonMark treats it — the file renders that way, so it must parse that way.
    /// </summary>
    [TestMethod]
    public void AnUnclosedFenceRunsToTheEndOfTheDocument()
    {
        var config = this._repo.WriteConfig(
            """
            notes
            ```json
            { "diagnosticPrefix": "CTS" }
            """);

        Assert.IsNull(config.Error);
        Assert.AreEqual("CTS", config.Get("diagnosticPrefix"));
    }

    /// <summary>
    /// Guarantees a file with no json block is treated as notes only — a valid way to use the file — with
    /// the whole document kept as notes and detection left to fill in every setting.
    /// </summary>
    [TestMethod]
    public void AFileWithNoJsonBlockIsNotesOnly()
    {
        var config = this._repo.WriteConfig(
            """
            # Notes

            Only prose here.
            """);

        Assert.IsTrue(config.Exists);
        Assert.IsNull(config.Error);
        Assert.IsNull(config.Get("diagnosticPrefix"));
        Assert.Contains("Only prose here.", config.Body);
    }

    /// <summary>
    /// Guarantees a malformed block is reported with its position instead of being silently ignored: a key
    /// lost to a lenient parser looks exactly like a key never written, and the tool would then describe
    /// conventions the repository does not follow.
    /// </summary>
    [TestMethod]
    public void MalformedJsonIsReportedInsteadOfIgnored()
    {
        var config = this._repo.WriteConfig(
            """
            ```json
            { "diagnosticPrefix": "CTS" "idDigits": 4 }
            ```
            """);

        Assert.IsNotNull(config.Error);
        Assert.Contains(Config.RelativePath, config.Error);
        Assert.Contains("BytePositionInLine", config.Error);
        Assert.IsNull(config.Get("diagnosticPrefix"));
    }

    /// <summary>Guarantees valid JSON that is not an object is reported rather than accepted as no settings.</summary>
    [TestMethod]
    public void ABlockThatIsNotAnObjectIsReported()
    {
        var config = this._repo.WriteConfig(
            """
            ```json
            ["CTS"]
            ```
            """);

        Assert.IsNotNull(config.Error);
    }

    /// <summary>Guarantees an empty block means no settings rather than an error.</summary>
    [TestMethod]
    public void AnEmptyBlockLeavesNoSettingsAndNoError()
    {
        var config = this._repo.WriteConfig(
            """
            ```json
            ```
            """);

        Assert.IsNull(config.Error);
        Assert.IsNull(config.Get("diagnosticPrefix"));
    }

    /// <summary>
    /// Guarantees keys are matched without regard to case, so a user writing `docsdir` gets the setting they
    /// meant rather than a silently ignored one.
    /// </summary>
    [TestMethod]
    public void KeyLookupIgnoresCase()
    {
        var config = this._repo.WriteConfig(
            """
            ```json
            { "DOCSDIR": "docs/rules" }
            ```
            """);

        Assert.AreEqual("docs/rules", config.Get("docsDir"));
    }

    /// <summary>
    /// Guarantees the nested lookup reads the categories map, which is how the next-id command resolves a
    /// category to a band when the IDs file has no header for it yet.
    /// </summary>
    [TestMethod]
    public void NestedLookupReadsTheCategoriesMap()
    {
        var config = this._repo.WriteConfig(
            """
            ```json
            { "categories": { "Design": 1, "Usage": 2 } }
            ```
            """);

        Assert.AreEqual("2", config.Get("categories", "usage"));
        Assert.IsNull(config.Get("categories", "Performance"));
        Assert.IsNull(config.Get("diagnosticPrefix", "Design"), "a scalar has nothing nested under it");
    }

    /// <summary>
    /// Guarantees numbers and booleans come back in their JSON spelling, because every caller parses the
    /// value from a string.
    /// </summary>
    [TestMethod]
    public void NonStringValuesKeepTheirJsonSpelling()
    {
        var config = this._repo.WriteConfig(
            """
            ```json
            { "idDigits": 4, "enabled": true }
            ```
            """);

        Assert.AreEqual("4", config.Get("idDigits"));
        Assert.AreEqual("true", config.Get("enabled"));
    }

    /// <summary>
    /// Guarantees an absent file is the ordinary case, not a failure: the repository is then described by
    /// detection alone.
    /// </summary>
    [TestMethod]
    public void AMissingFileIsNotAnError()
    {
        var config = new Config(this._repo.Root);

        Assert.IsFalse(config.Exists);
        Assert.IsNull(config.Error);
        Assert.AreEqual("", config.Body);
        Assert.IsNull(config.Get("diagnosticPrefix"));
    }

    /// <summary>
    /// Guarantees the JSON handed to callers is a copy, so a script that edits its own output cannot change
    /// what a later lookup returns.
    /// </summary>
    [TestMethod]
    public void ToJsonIsAnIndependentCopy()
    {
        var config = this._repo.WriteConfig(
            """
            ```json
            { "diagnosticPrefix": "CTS" }
            ```
            """);

        var json = config.ToJson();
        json["diagnosticPrefix"] = "ZZZ";

        Assert.AreEqual("CTS", config.Get("diagnosticPrefix"));
    }
}
