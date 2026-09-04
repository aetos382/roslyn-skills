using System.Text.RegularExpressions;

namespace Aetos.RoslynSkills.Tools.Internal;

/// <summary>
/// The repository an origin URL names. The three parts only ever make sense together — a host without an owner
/// cannot address anything — so they travel as one value rather than as three independently nullable fields.
/// </summary>
internal sealed record RemoteRepository(string Host, string Owner, string Name);

internal sealed partial record GitInfo(string? Remote, RemoteRepository? Repository, string? DefaultBranch)
{
    [GeneratedRegex(@"^(?:https?://|git@|ssh://git@)(?<host>[^/:]+)[/:](?<owner>[^/]+)/(?<repo>[^/]+?)(?:\.git)?/?$")]
    private static partial Regex RemoteUrl { get; }

    [GeneratedRegex("^origin/")]
    private static partial Regex OriginPrefix { get; }

    /// <summary>The repository an origin URL points at, or null when the URL is not one this can address.</summary>
    public static RemoteRepository? ParseRemote(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return null;
        }

        var m = RemoteUrl.Match(url);
        return m.Success
            ? new RemoteRepository(m.Groups["host"].Value, m.Groups["owner"].Value, m.Groups["repo"].Value)
            : null;
    }

    public static GitInfo Read(string root)
    {
        var remote = Shell.Exec("git", ["remote", "get-url", "origin"], root, 15000).Output;

        // The default branch, and only the default branch. The currently checked out branch is deliberately
        // not a fallback: a help link built from it dies with the branch, and a URL that 404s later is worse
        // than a descriptor with no help link at all.
        var branch = Shell.Exec("git", ["symbolic-ref", "--short", "refs/remotes/origin/HEAD"], root, 15000).Output;
        if (!string.IsNullOrEmpty(branch))
        {
            branch = OriginPrefix.Replace(branch, "");
        }

        if (string.IsNullOrEmpty(branch))
        {
            branch = Shell.Exec("gh", ["repo", "view", "--json", "defaultBranchRef", "-q", ".defaultBranchRef.name"], root, 15000).Output;
        }

        return new GitInfo(remote, ParseRemote(remote), string.IsNullOrEmpty(branch) ? null : branch);
    }

    public string? DefaultTemplate
    {
        get
        {
            if (this.Repository?.Host == "github.com")
            {
                return "https://github.com/{owner}/{repo}/blob/{branch}/{path}";
            }

            return null;
        }
    }
}
