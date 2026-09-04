using Aetos.RoslynSkills.Tools.Internal;

namespace Aetos.RoslynSkills.Tools.Tests;

/// <summary>
/// Reading the origin URL. What comes out of here is baked into every new descriptor's helpLinkUri, and a wrong
/// URL is only noticed by whoever clicks it after the release.
/// </summary>
[TestClass]
public sealed class GitInfoTests
{
    /// <summary>
    /// Guarantees the URL shapes git actually stores are all read into the same three parts, since which one a
    /// clone has depends on how it was cloned and not on anything the repository chose.
    /// </summary>
    [TestMethod]
    [DataRow("https://github.com/aetos382/roslyn-skills.git")]
    [DataRow("https://github.com/aetos382/roslyn-skills")]
    [DataRow("git@github.com:aetos382/roslyn-skills.git")]
    [DataRow("ssh://git@github.com/aetos382/roslyn-skills.git")]
    [DataRow("http://github.com/aetos382/roslyn-skills/")]
    public void EveryUrlShapeGitStoresIsRead(string remote)
    {
        var repository = GitInfo.ParseRemote(remote);

        Assert.IsNotNull(repository);
        Assert.AreEqual("github.com", repository.Host);
        Assert.AreEqual("aetos382", repository.Owner);
        Assert.AreEqual("roslyn-skills", repository.Name);
    }

    /// <summary>
    /// Guarantees a host that is not github.com is still read, because the owner and repository name are what a
    /// configured docUrlTemplate fills in and those hosts spell their URLs the same way.
    /// </summary>
    [TestMethod]
    public void AnotherHostIsReadToo()
    {
        var repository = GitInfo.ParseRemote("git@ssh.dev.azure.com:contoso/analyzers.git");

        Assert.IsNotNull(repository);
        Assert.AreEqual("ssh.dev.azure.com", repository.Host);
        Assert.AreEqual("contoso", repository.Owner);
        Assert.AreEqual("analyzers", repository.Name);
    }

    /// <summary>
    /// Guarantees a URL the parser cannot address yields nothing rather than partly filled parts: a local clone or
    /// a file path names no host to build a URL from, and the caller is meant to ask for a template instead.
    /// </summary>
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("/srv/git/analyzers.git")]
    [DataRow(@"C:\repos\analyzers")]
    [DataRow("https://github.com/aetos382")]
    public void AUrlWithNothingToAddressYieldsNothing(string? remote)
    {
        Assert.IsNull(GitInfo.ParseRemote(remote));
    }

    /// <summary>
    /// Guarantees the built-in URL template is offered for github.com only, since the path layout it hard-codes —
    /// /blob/{branch}/ — is GitHub's and would be a broken link anywhere else.
    /// </summary>
    [TestMethod]
    public void TheBuiltInTemplateIsGitHubOnly()
    {
        var github = "https://github.com/aetos382/roslyn-skills.git";

        Assert.AreEqual(
            "https://github.com/{owner}/{repo}/blob/{branch}/{path}",
            new GitInfo(github, GitInfo.ParseRemote(github), "main").DefaultTemplate);

        var other = "git@ssh.dev.azure.com:contoso/analyzers.git";

        Assert.IsNull(new GitInfo(other, GitInfo.ParseRemote(other), "main").DefaultTemplate);
        Assert.IsNull(new GitInfo(null, null, null).DefaultTemplate, "no remote means no template");
    }
}
