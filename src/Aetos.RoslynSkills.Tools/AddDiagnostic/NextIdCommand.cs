using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

using Aetos.RoslynSkills.Tools.Internal;

namespace Aetos.RoslynSkills.Tools.AddDiagnostic;

/// <summary>
/// Computes the next free diagnostic (or suppression) ID from an ID constants file.
///
/// Category bands are read from comment headers in the file such as `// Design (ABC1xxx)`. With no band,
/// the next number after the highest existing one is used. Suppression IDs (prefix + 'S' + number) have no bands.
/// </summary>
internal static class NextIdCommand
{
    public static Command Create()
    {
        var idsFile = new Option<string>("--ids-file")
        {
            Description = "Absolute path to DiagnosticIds.cs or SuppressionIds.cs.",
            Required = true,
        };
        var prefix = new Option<string?>("--prefix")
        {
            Description = "ID prefix such as ABC. Inferred from existing IDs, band headers, or the config when omitted.",
        };
        var category = new Option<string?>("--category")
        {
            Description = "Category name, resolved to a band through the file's band headers or the config.",
        };
        var band = new Option<int?>("--band")
        {
            Description = "Band digit to allocate in, when the category has no header yet.",
        };
        var digits = new Option<int?>("--digits")
        {
            Description = "Digit count of the number. Taken from the existing IDs, else 4.",
        };
        var suppression = new Option<bool>("--suppression")
        {
            Description = "Allocate a suppression ID (prefix + 'S' + number), which has no bands.",
        };

        var command = new Command("next-id", "Computes the next free diagnostic or suppression ID.");
        command.Options.Add(idsFile);
        command.Options.Add(prefix);
        command.Options.Add(category);
        command.Options.Add(band);
        command.Options.Add(digits);
        command.Options.Add(suppression);
        command.SetAction(parse => Run(
            parse.GetValue(idsFile)!,
            parse.GetValue(prefix),
            parse.GetValue(category),
            parse.GetValue(band),
            parse.GetValue(digits),
            parse.GetValue(suppression)));
        return command;
    }

    private static int Run(string idsFile, string? prefix, string? category, int? band, int? digits, bool suppression)
    {
        var text = File.Exists(idsFile) ? File.ReadAllText(idsFile) : "";
        var all = IdConst.Parse(text);

        var cfg = new Config(Repo.GetRoot(Path.GetDirectoryName(Path.GetFullPath(idsFile))!));
        if (cfg.Error is { } cfgError)
        {
            return Json.Fail(cfgError, $"Fix the json block in {Config.RelativePath}, or delete the file to fall back to detection.");
        }

        // Prefix: explicit, else config, else inferred from existing IDs, else from band headers (`// Design (ABC1xxx)`).
        prefix ??= cfg.Get("diagnosticPrefix");
        if (prefix is null && all.Count > 0)
        {
            var letters = all.GroupBy(i => i.Letters).OrderByDescending(g => g.Count()).First().Key;
            prefix = suppression && letters.EndsWith('S') ? letters[..^1] : letters;
        }
        prefix ??= IdsFileText.ReadHeaderPrefix(text);
        if (prefix is null)
        {
            return Json.Fail($"No existing IDs or band headers with a prefix in '{idsFile}'.",
                "Pass --prefix <PREFIX>, and --band <n> for a diagnostic. This is the normal case for a repository with no diagnostics yet.");
        }

        var mine = all.Where(i => suppression ? i.IsSuppressionOf(prefix) : i.IsDiagnosticOf(prefix)).ToList();
        var idDigits = digits ?? (mine.Count > 0 ? mine.GroupBy(i => i.Digits).OrderByDescending(g => g.Count()).First().Key : 4);

        var bands = IdsFileText.ReadBands(text);
        if (band is null && category is not null && bands.TryGetValue(category, out var b))
        {
            band = b;
        }

        if (band is null && category is not null && !suppression)
        {
            // Fall back to the config's categories map.
            if (cfg.Get("categories", category) is { } s && int.TryParse(s, out var cb))
            {
                band = cb;
            }
        }
        if (suppression)
        {
            band = null;
        }

        var bandSize = (int)Math.Pow(10, idDigits - 1);
        int next;
        List<IdConst> inBand = new();
        if (band is int bi)
        {
            var low = bi * bandSize;
            var high = low + bandSize - 1;
            inBand = mine.Where(i => i.Number >= low && i.Number <= high).OrderBy(i => i.Number).ToList();
            next = inBand.Count > 0 ? inBand[^1].Number + 1 : low + 1;
            if (next > high)
            {
                return Json.Fail($"Band {bi} ({prefix}{(suppression ? "S" : "")}{low}-{high}) is full.", "Choose another band for this category.");
            }
        }
        else
        {
            next = mine.Count > 0 ? mine.Max(i => i.Number) + 1 : 1;
        }

        var infix = suppression ? "S" : "";
        var value = $"{prefix}{infix}{next.ToString().PadLeft(idDigits, '0')}";
        if (mine.Any(i => i.Number == next))
        {
            return Json.Fail($"Computed ID {value} already exists.", "Re-run after checking the IDs file.");
        }

        var knownBands = new JsonObject();
        foreach (var (k, v) in bands)
        {
            knownBands[k] = v;
        }

        Json.Print(new JsonObject
        {
            ["id"] = value,
            ["prefix"] = prefix,
            ["number"] = next,
            ["digits"] = idDigits,
            ["band"] = band,
            ["category"] = category,
            ["knownBands"] = knownBands,
            ["existingInBand"] = Json.Array(inBand.Select(i => i.Value)),
            ["highestOverall"] = mine.Count > 0 ? mine.Max(i => i.Number) : 0,
            ["unresolvedCategory"] = category is not null && band is null && !suppression,
        });
        return 0;
    }
}
