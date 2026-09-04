using System.Text.RegularExpressions;

namespace Aetos.RoslynSkills.Tools;

internal sealed record GitInfo(string? Remote, string? Host, string? Owner, string? RepoName, string? DefaultBranch)
{
    public static GitInfo Read(string root)
    {
        string? remote = Shell.Run("git", "remote get-url origin", root);
        string? host = null, owner = null, repo = null;
        if (remote is not null)
        {
            var m = Regex.Match(remote, @"^(?:https?://|git@|ssh://git@)(?<host>[^/:]+)[/:](?<owner>[^/]+)/(?<repo>[^/]+?)(?:\.git)?/?$");
            if (m.Success) { host = m.Groups["host"].Value; owner = m.Groups["owner"].Value; repo = m.Groups["repo"].Value; }
        }
        var branch = Shell.Run("git", "symbolic-ref --short refs/remotes/origin/HEAD", root);
        if (!string.IsNullOrEmpty(branch)) branch = Regex.Replace(branch, "^origin/", "");
        if (string.IsNullOrEmpty(branch)) branch = Shell.Run("gh", "repo view --json defaultBranchRef -q .defaultBranchRef.name", root);
        if (string.IsNullOrEmpty(branch)) branch = Shell.Run("git", "branch --show-current", root);
        return new GitInfo(remote, host, owner, repo, string.IsNullOrEmpty(branch) ? null : branch);
    }

    public string? DefaultTemplate => Host == "github.com" ? "https://github.com/{owner}/{repo}/blob/{branch}/{path}" : null;
}
