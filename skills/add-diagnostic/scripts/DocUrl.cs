#!/usr/bin/env dotnet
#:property PublishAot=false
#:include Common.cs
// DocUrl.cs — resolves the public URL of a rule documentation file for DiagnosticDescriptor.helpLinkUri.
//
// Usage:  dotnet DocUrl.cs -- --doc docs/rules/ABC1001.md [--template <url-template>] [--path <repo>]
//
// Template resolution: --template, then `docUrlTemplate` in .claude/roslyn-skills.md, then
// https://github.com/{owner}/{repo}/blob/{branch}/{path} when origin is on github.com.
// Owner/repo come from the origin remote; branch from origin/HEAD, then `gh repo view`, then the current branch.

using System.Text.Json.Nodes;

var cli = new CliArgs(args);
var doc = cli.Require("doc").Replace('\\', '/').TrimStart('/');
var root = Repo.GetRoot(cli.Get("path") ?? ".");
var config = new Config(root);
var git = GitInfo.Read(root);

var template = cli.Get("template") ?? config.Get("docUrlTemplate") ?? git.DefaultTemplate
    ?? throw new InvalidOperationException($"No docUrlTemplate configured and origin is not on github.com (host: '{git.Host}'). Add 'docUrlTemplate' to {Config.RelativePath} or pass --template.");

var missing = new List<string>();
if (template.Contains("{owner}") && git.Owner is null) missing.Add("owner");
if (template.Contains("{repo}") && git.RepoName is null) missing.Add("repo");
if (template.Contains("{branch}") && git.DefaultBranch is null) missing.Add("branch");
if (missing.Count > 0) throw new InvalidOperationException("Could not determine: " + string.Join(", ", missing) + ". Check the git remote or pass --template without those placeholders.");

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
