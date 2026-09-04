using System.Diagnostics;

namespace Aetos.RoslynSkills.Tools;

internal static class Shell
{
    /// <summary>Runs a process and returns its exit code with both streams; exit code -1 means it never ran.</summary>
    public static (int ExitCode, string StdOut, string StdErr) Exec(string file, IEnumerable<string> args, string? workingDirectory = null, int timeoutMs = 120000)
    {
        try
        {
            var psi = new ProcessStartInfo(file)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return (-1, "", "process did not start");
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return (-1, "", $"timed out after {timeoutMs} ms"); }
            return (p.ExitCode, stdout.Result, stderr.Result);
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }

    /// <summary>Runs a process and returns stdout, or null when it fails or is missing.</summary>
    public static string? Run(string file, string args, string? workingDirectory = null, int timeoutMs = 15000)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return null; }
            return p.ExitCode == 0 ? stdout.Result.Trim() : null;
        }
        catch
        {
            return null;
        }
    }
}
