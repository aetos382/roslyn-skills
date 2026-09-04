using System.Text.RegularExpressions;

namespace Aetos.RoslynSkills.Tools.Internal;

internal sealed partial record GitInfo(string? Remote, string? Host, string? Owner, string? RepoName, string? DefaultBranch)
{
    [GeneratedRegex(@"^(?:https?://|git@|ssh://git@)(?<host>[^/:]+)[/:](?<owner>[^/]+)/(?<repo>[^/]+?)(?:\.git)?/?$")]
    private static partial Regex RemoteUrl { get; }

    [GeneratedRegex("^origin/")]
    private static partial Regex OriginPrefix { get; }

    public static GitInfo Read(string root)
    {
        var remote = Shell.Run("git", "remote get-url origin", root);
        string? host = null, owner = null, repo = null;

        if (remote is not null)
        {
            var m = RemoteUrl.Match(remote);
            if (m.Success)
            {
                host = m.Groups["host"].Value;
                owner = m.Groups["owner"].Value;
                repo = m.Groups["repo"].Value;
            }
        }

        var branch = Shell.Run("git", "symbolic-ref --short refs/remotes/origin/HEAD", root);
        if (!string.IsNullOrEmpty(branch))
        {
            branch = OriginPrefix.Replace(branch, "");
        }

        if (string.IsNullOrEmpty(branch))
        {
            branch = Shell.Run("gh", "repo view --json defaultBranchRef -q .defaultBranchRef.name", root);
        }

        if (string.IsNullOrEmpty(branch))
        {
            branch = Shell.Run("git", "branch --show-current", root);
        }

        return new GitInfo(remote, host, owner, repo, string.IsNullOrEmpty(branch) ? null : branch);
    }

    public string? DefaultTemplate
    {
        get
        {
            if (this.Host == "github.com")
            {
                return "https://github.com/{owner}/{repo}/blob/{branch}/{path}";
            }

            return null;
        }
    }
}
