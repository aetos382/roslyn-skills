using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;

using Aetos.RoslynSkills.Tools.Internal;

namespace Aetos.RoslynSkills.Tools.AddDiagnostic;

/// <summary>
/// Adds diagnostic string resources to .resx files in ID order and validates the result.
///
/// Inserts &lt;data name="..." xml:space="preserve"&gt;&lt;value&gt;...&lt;/value&gt;&lt;/data&gt; without touching Designer.cs.
/// New entries are placed so the file stays ordered by ID value (resolved through --ids-file) and within one
/// diagnostic by Title -&gt; Message -&gt; Description (Justification for suppressions). Existing entries are never
/// moved. Every file is re-parsed afterwards; exit code 1 means a file failed validation.
/// </summary>
internal static partial class AddResxEntriesCommand
{
    [GeneratedRegex(@"^(?<base>.+?)(?<suffix>Title|Message|Description|Justification)$")]
    private static partial Regex EntryName { get; }

    public static Command Create()
    {
        var resx = new Option<string[]>("--resx")
        {
            Description = "Absolute path to a .resx file. Repeat the option, or pass a comma-separated list, for several files.",
            Required = true,
            AllowMultipleArgumentsPerToken = true,
        };
        var entries = new Option<string?>("--entries")
        {
            Description = "A JSON array of { name, value, comment? } objects, or the path to a file holding one.",
        };
        var idsFile = new Option<string?>("--ids-file")
        {
            Description = "Absolute path to the IDs file, used to sort new entries by ID value.",
        };
        var force = new Option<bool>("--force")
        {
            Description = "Overwrite the value of an entry that already exists instead of skipping it.",
        };
        var validateOnly = new Option<bool>("--validate-only")
        {
            Description = "Only report on the files, without adding anything.",
        };

        var command = new Command("add-resx-entries", "Adds resx entries in ID order and validates the result.");
        command.Options.Add(resx);
        command.Options.Add(entries);
        command.Options.Add(idsFile);
        command.Options.Add(force);
        command.Options.Add(validateOnly);
        command.SetAction(parse => Run(
            parse.GetValue(resx) ?? [],
            parse.GetValue(entries),
            parse.GetValue(idsFile),
            parse.GetValue(force),
            parse.GetValue(validateOnly)));
        return command;
    }

    private static int Run(string[] resxValues, string? rawEntries, string? idsFilePath, bool force, bool validateOnly)
    {
        var resxPaths = resxValues
            .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToList();
        if (resxPaths.Count == 0)
        {
            return Json.Fail("--resx is required.", "Pass one --resx per file, or a comma-separated list.");
        }

        if (resxPaths.FirstOrDefault(p => !File.Exists(p)) is { } missingResx)
        {
            return Json.Fail($"resx not found: {missingResx}", "Pass an absolute path; the working directory is not the repository.");
        }

        var suffixRank = new Dictionary<string, int> { ["Title"] = 0, ["Message"] = 1, ["Description"] = 2, ["Justification"] = 0 };
        var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
        if (idsFilePath is not null)
        {
            // A missing file would leave every new entry sorted by name instead of by ID, silently producing a
            // different order than the one the option was passed to get.
            if (!File.Exists(idsFilePath))
            {
                return Json.Fail($"IDs file not found: {idsFilePath}",
                    "Pass an absolute path, or omit --ids-file to sort new entries by name.");
            }

            foreach (var id in IdConst.Parse(File.ReadAllText(idsFilePath)))
            {
                idMap[id.Name] = id.Value;
            }
        }

        SortKey KeyOf(string name)
        {
            var m = EntryName.Match(name);
            if (!m.Success)
            {
                return new SortKey(false, name, 0);
            }

            var b = m.Groups["base"].Value;
            return idMap.TryGetValue(b, out var id) ? new SortKey(true, id, suffixRank[m.Groups["suffix"].Value]) : new SortKey(false, b, suffixRank[m.Groups["suffix"].Value]);
        }

        var entries = new List<Entry>();
        if (!validateOnly)
        {
            if (rawEntries is not { } raw)
            {
                return Json.Fail("--entries is required.", "Pass a JSON array or a path to a JSON file, or use --validate-only.");
            }

            var json = File.Exists(raw) ? File.ReadAllText(raw) : raw;
            JsonNode? parsed;
            try
            {
                parsed = JsonNode.Parse(json);
            }
            catch (JsonException ex)
            {
                // The caller wrote this JSON, so it is a bad argument like the two below and not a bug in the tool.
                return Json.Fail($"--entries is not valid JSON: {ex.Message}", "See examples/resx-entries.json.");
            }

            if (parsed is not JsonArray arr)
            {
                return Json.Fail("--entries must be a JSON array.", "See examples/resx-entries.json.");
            }

            foreach (var n in arr)
            {
                if (n is not JsonObject o)
                {
                    return Json.Fail("Each entry must be an object.", "See examples/resx-entries.json.");
                }

                var name = o["name"]?.ToString();
                var value = o["value"]?.ToString();
                if (string.IsNullOrEmpty(name) || value is null)
                {
                    return Json.Fail($"Each entry needs 'name' and 'value': {o.ToJsonString()}", "See examples/resx-entries.json.");
                }

                entries.Add(new Entry(name, value, o["comment"]?.ToString()));
            }
            entries = entries.OrderBy(e => KeyOf(e.Name)).ToList();
        }

        var report = new JsonArray();
        var anyInvalid = false;
        foreach (var file in resxPaths)
        {
            var full = Path.GetFullPath(file);
            var added = new List<string>();
            var skipped = new List<string>();
            var updated = new List<string>();
            var problems = new List<string>();

            // Each file is reported on separately rather than aborting the run: by the time one file turns out to
            // be unreadable, the files before it have already been rewritten, and a report that never got printed
            // would leave the caller with no record of that.
            XmlDocument? doc = null;
            var hasBom = false;
            var newline = "\n";
            try
            {
                (var content, hasBom, newline) = Text.ReadPreserving(full);
                // Assigned only once the document is known to be usable: a half-loaded XmlDocument has no
                // DocumentElement, and the code below reads that one without checking.
                var loaded = new XmlDocument { PreserveWhitespace = true };
                loaded.LoadXml(content);
                if (loaded.DocumentElement?.Name != "root")
                {
                    problems.Add($"not a resx document (root element is '{loaded.DocumentElement?.Name}')");
                }
                else
                {
                    doc = loaded;
                }
            }
            catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
            {
                problems.Add("read failed: " + ex.Message);
            }

            if (doc is not null && !validateOnly)
            {
                var rootEl = doc.DocumentElement!;
                foreach (var e in entries)
                {
                    if (DataElement(rootEl, e.Name) is { } existing)
                    {
                        if (force)
                        {
                            // A <data> with no <value> is malformed but does occur; give it one rather than
                            // failing on a null the validation pass would have reported anyway.
                            var valueElement = ChildElement(existing, "value");
                            if (valueElement is null)
                            {
                                valueElement = doc.CreateElement("value");
                                existing.AppendChild(valueElement);
                            }

                            valueElement.InnerText = e.Value;
                            updated.Add(e.Name);
                        }
                        else
                        {
                            skipped.Add(e.Name);
                        }

                        continue;
                    }

                    var dataNodes = DataElements(rootEl);
                    var key = KeyOf(e.Name);
                    XmlElement? anchor = null;
                    foreach (var n in dataNodes)
                    {
                        if (KeyOf(n.GetAttribute("name")).CompareTo(key) <= 0)
                        {
                            anchor = n;
                        }
                        else
                        {
                            break;
                        }
                    }

                    // Indentation: copy what precedes a neighbouring <data>; with no <data> yet, copy what precedes the last
                    // element child of <root> (typically a <resheader>). Only the last line of that whitespace is used.
                    var sample = anchor ??
                                 (XmlNode?)dataNodes.FirstOrDefault() ??
                                 rootEl.ChildNodes.Cast<XmlNode>().LastOrDefault(n => n is XmlElement);

                    var indent = newline + "  ";
                    if (sample?.PreviousSibling is XmlWhitespace { Value: { } wsValue })
                    {
                        var lastLine = wsValue[(wsValue.LastIndexOf('\n') + 1)..];
                        indent = newline + lastLine;
                    }
                    var unit = indent.EndsWith('\t') ? "\t" : "  ";
                    var innerIndent = indent + unit;

                    var data = doc.CreateElement("data");
                    data.SetAttribute("name", e.Name);
                    data.SetAttribute("xml:space", "preserve");
                    data.AppendChild(doc.CreateWhitespace(innerIndent));
                    var valueEl = doc.CreateElement("value");
                    valueEl.InnerText = e.Value;
                    data.AppendChild(valueEl);
                    if (!string.IsNullOrEmpty(e.Comment))
                    {
                        data.AppendChild(doc.CreateWhitespace(innerIndent));
                        var c = doc.CreateElement("comment");
                        c.InnerText = e.Comment;
                        data.AppendChild(c);
                    }
                    data.AppendChild(doc.CreateWhitespace(indent));

                    var lead = doc.CreateWhitespace(indent);
                    if (anchor is not null)
                    {
                        rootEl.InsertAfter(lead, anchor);
                        rootEl.InsertAfter(data, lead);
                    }
                    else if (dataNodes.Count > 0)
                    {
                        rootEl.InsertBefore(data, dataNodes[0]);
                        rootEl.InsertAfter(lead, data);
                    }
                    else if (rootEl.LastChild is XmlWhitespace trailing)
                    {
                        rootEl.InsertBefore(lead, trailing);
                        rootEl.InsertBefore(data, trailing);
                    }
                    else
                    {
                        rootEl.AppendChild(lead);
                        rootEl.AppendChild(data);
                        rootEl.AppendChild(doc.CreateWhitespace(newline));
                    }
                    added.Add(e.Name);
                }

                if (added.Count > 0 || updated.Count > 0)
                {
                    var settings = new XmlWriterSettings
                    {
                        Encoding = new UTF8Encoding(hasBom),
                        Indent = false,
                        NewLineHandling = NewLineHandling.None,
                        OmitXmlDeclaration = doc.FirstChild is not XmlDeclaration,
                    };
                    using var writer = XmlWriter.Create(full, settings);
                    doc.Save(writer);
                }
            }

            // ---- Validation -----------------------------------------------------
            if (doc is not null)
            {
                try
                {
                    var check = new XmlDocument();
                    check.Load(full);

                    if (check.DocumentElement is not { Name: "root" } checkRoot)
                    {
                        problems.Add($"root element is '{check.DocumentElement?.Name}', expected 'root'");
                    }
                    else
                    {
                        var dataElements = DataElements(checkRoot);
                        var dupes = dataElements.GroupBy(d => d.GetAttribute("name"))
                            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
                        if (dupes.Count > 0)
                        {
                            problems.Add("duplicate data names: " + string.Join(", ", dupes));
                        }

                        foreach (var n in added.Concat(updated))
                        {
                            if (DataElement(checkRoot, n) is not { } written || ChildElement(written, "value") is null)
                            {
                                problems.Add($"entry '{n}' missing after write");
                            }
                        }

                        foreach (var d in dataElements)
                        {
                            if (ChildElement(d, "value") is null)
                            {
                                problems.Add($"data '{d.GetAttribute("name")}' has no <value>");
                            }
                        }
                    }

                    var decls = File.ReadAllText(full).Split("<?xml").Length - 1;
                    if (decls > 1)
                    {
                        problems.Add("multiple XML declarations");
                    }
                }
                catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
                {
                    problems.Add("XML parse failed: " + ex.Message);
                }
            }

            // A satellite has no Designer of its own; the neutral file's is the one that goes stale.
            var designer = Path.Combine(Path.GetDirectoryName(full)!, ResxName.Split(full).Base + ".Designer.cs");
            var designerStale = false;
            if (File.Exists(designer) && added.Count > 0)
            {
                try
                {
                    var dtext = File.ReadAllText(designer);
                    designerStale = added.Any(n => !Regex.IsMatch(dtext, $@"\b{Regex.Escape(n)}\b"));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    problems.Add($"could not read {Path.GetFileName(designer)} to check whether it is stale: {ex.Message}");
                }
            }

            if (problems.Count > 0)
            {
                anyInvalid = true;
            }

            report.Add(new JsonObject
            {
                ["file"] = file,
                ["added"] = Json.Array(added),
                ["updated"] = Json.Array(updated),
                ["skipped"] = Json.Array(skipped),
                ["valid"] = problems.Count == 0,
                ["problems"] = Json.Array(problems),
                ["designerFile"] = File.Exists(designer) ? designer : null,
                ["designerStale"] = designerStale,
            });
        }

        Json.Print(report);
        return anyInvalid ? 1 : 0;
    }

    /// <summary>
    /// The direct &lt;data&gt; children of &lt;root&gt;, in document order.
    /// </summary>
    private static List<XmlElement> DataElements(XmlElement root)
    {
        return root.ChildNodes.OfType<XmlElement>().Where(e => e.Name == "data").ToList();
    }

    /// <summary>
    /// The &lt;data&gt; child carrying this name attribute. Scanned rather than selected with an XPath predicate:
    /// an entry name arrives from JSON the agent wrote, and a quote or bracket in it would change which nodes an
    /// interpolated `data[@name='...']` matches.
    /// </summary>
    private static XmlElement? DataElement(XmlElement root, string name)
    {
        return DataElements(root).FirstOrDefault(e => e.GetAttribute("name") == name);
    }

    private static XmlElement? ChildElement(XmlElement parent, string name)
    {
        return parent.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.Name == name);
    }

    private sealed record Entry(string Name, string Value, string? Comment);

    /// <summary>
    /// Where an entry sorts: ID-mapped entries by ID value and first, unmapped ones by base name, and within one
    /// diagnostic by Title -&gt; Message -&gt; Description.
    /// </summary>
    private sealed record SortKey(bool Known, string Primary, int Rank) : IComparable<SortKey>
    {
        public int CompareTo(SortKey? other)
        {
            if (other is null)
            {
                return 1;
            }

            if (this.Known != other.Known)
            {
                return this.Known ? -1 : 1;
            }

            var c = string.CompareOrdinal(this.Primary, other.Primary);
            return c != 0 ? c : this.Rank.CompareTo(other.Rank);
        }
    }
}
