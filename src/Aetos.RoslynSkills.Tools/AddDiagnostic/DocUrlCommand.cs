using System.CommandLine;
using System.Text.Json.Nodes;

namespace Aetos.RoslynSkills.Tools.AddDiagnostic;

/// <summary>
/// Resolves the public URL of a rule documentation file for DiagnosticDescriptor.helpLinkUri.
///
/// Template resolution: --template, then `docUrlTemplate` in .claude/roslyn-skills/add-diagnostic.md, then
/// https://github.com/{owner}/{repo}/blob/{branch}/{path} when origin is on github.com.
/// Owner/repo come from the origin remote; branch from origin/HEAD, then `gh repo view`, then the current branch.
/// </summary>
internal static class DocUrlCommand
{
    public static Command Create()
    {
        var doc = new Option<string>("--doc")
        {
            Description = "The documentation file's path within the repository, such as docs/rules/ABC1001.md, because it becomes part of a URL.",
            Required = true,
        };
        var template = new Option<string?>("--template")
        {
            Description = "URL template with {owner}, {repo}, {branch} and {path} placeholders.",
        };
        var path = new Option<string>("--path")
        {
            Description = "Any path inside the repository. Defaults to the working directory.",
            DefaultValueFactory = _ => ".",
        };

        var command = new Command("doc-url", "Resolves a rule documentation page's public URL for helpLinkUri.");
        command.Options.Add(doc);
        command.Options.Add(template);
        command.Options.Add(path);
        command.SetAction(parse => Run(parse.GetValue(doc)!, parse.GetValue(template), parse.GetValue(path)!));
        return command;
    }

    private static int Run(string rawDoc, string? template, string repoPath)
    {
        var root = Repo.GetRoot(repoPath);

        string doc;
        if (Path.IsPathFullyQualified(rawDoc))
        {
            // An absolute path is accepted and made relative to the repository root; one pointing outside it
            // is an error rather than a plausible-looking broken link.
            var rel = Path.GetRelativePath(root, Path.GetFullPath(rawDoc)).Replace('\\', '/');
            if (rel.StartsWith("../", StringComparison.Ordinal) || Path.IsPathFullyQualified(rel))
                return Json.Fail($"--doc '{rawDoc}' is outside the repository at '{root}'.",
                    "Pass the documentation path relative to the repository root, such as docs/rules/ABC1001.md, or point --path at the right repository.");
            doc = rel;
        }
        else
        {
            doc = rawDoc.Replace('\\', '/').TrimStart('/');
        }

        var config = new Config(root);
        if (config.Error is { } configError)
            return Json.Fail(configError, $"Fix the json block in {Config.RelativePath}, or delete the file to fall back to detection.");
        var git = GitInfo.Read(root);

        template ??= config.Get("docUrlTemplate") ?? git.DefaultTemplate;
        if (template is null)
            return Json.Fail($"No docUrlTemplate configured and origin is not on github.com (host: '{git.Host}').",
                $"Add 'docUrlTemplate' to {Config.RelativePath} or pass --template.");

        var missing = new List<string>();
        if (template.Contains("{owner}") && git.Owner is null) missing.Add("owner");
        if (template.Contains("{repo}") && git.RepoName is null) missing.Add("repo");
        if (template.Contains("{branch}") && git.DefaultBranch is null) missing.Add("branch");
        if (missing.Count > 0)
            return Json.Fail("Could not determine: " + string.Join(", ", missing) + ".",
                "Check the git remote, or pass --template without those placeholders.");

        var url = template.Replace("{owner}", git.Owner ?? "").Replace("{repo}", git.RepoName ?? "").Replace("{branch}", git.DefaultBranch ?? "").Replace("{path}", doc);

        Json.Print(new JsonObject
        {
            ["url"] = url,
            ["template"] = template,
            ["owner"] = git.Owner,
            ["repo"] = git.RepoName,
            ["branch"] = git.DefaultBranch,
            ["path"] = doc,
        });
        return 0;
    }
}
