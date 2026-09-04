using Aetos.RoslynSkills.Tools.AddDiagnostic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Aetos.RoslynSkills.Tools.Tests;

/// <summary>A throwaway directory standing in for a repository under inspection.</summary>
internal sealed class TempRepository : IDisposable
{
    public TempRepository(TestContext context)
    {
        Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "roslyn-skills-tests", $"{context.TestName}-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string Write(string relativePath, string content)
    {
        var full = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    /// <summary>Writes the add-diagnostic settings file and reads it back.</summary>
    public Config WriteConfig(string content)
    {
        Write(Config.RelativePath, content);
        return new Config(Root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, true);
        }
        catch (IOException)
        {
        }
    }
}
