using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace Aetos.RoslynSkills.Tools.Internal;

/// <summary>
/// What a child process produced. A caller that only wants the output reads <see cref="Output"/>, and when
/// that is null <see cref="Error"/> says why: the process never started, timed out, or exited non-zero.
/// Those are cases a caller has to be able to tell apart from a process that legitimately printed nothing.
/// </summary>
internal sealed record ProcessResult(int ExitCode, string StdOut, string StdErr, string? Failure)
{
    /// <summary>Trimmed stdout, or null when the process failed to run or exited non-zero.</summary>
    public string? Output =>
        this.Failure is null && this.ExitCode == 0 ? this.StdOut.Trim() : null;

    /// <summary>Why <see cref="Output"/> is null; null when it is not.</summary>
    public string? Error
    {
        get
        {
            if (this.Failure is not null)
            {
                return this.Failure;
            }

            if (this.ExitCode == 0)
            {
                return null;
            }

            var code = this.ExitCode.ToString(CultureInfo.InvariantCulture);
            var stderr = this.StdErr.Trim();
            return stderr.Length == 0 ? $"exited with code {code}" : $"exited with code {code}: {stderr}";
        }
    }
}

internal static class Shell
{
    /// <summary>
    /// Runs a process and returns both streams along with the reason it produced nothing, if it did.
    /// Arguments are passed as a list so nothing has to be quoted for a shell that is never involved.
    /// </summary>
    public static ProcessResult Exec(string file, IEnumerable<string> args, string? workingDirectory = null, int timeoutMs = 120000)
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
            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }

            using var p = Process.Start(psi);
            if (p is null)
            {
                return new ProcessResult(-1, "", "", $"{file} did not start");
            }

            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs))
            {
                var kill = TryKill(p);
                var failure = $"{file} timed out after {timeoutMs} ms";
                return new ProcessResult(-1, "", "", kill is null ? failure : $"{failure} and could not be killed: {kill}");
            }

            return new ProcessResult(p.ExitCode, stdout.Result, stderr.Result, null);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return new ProcessResult(-1, "", "", $"{file} could not be run: {ex.Message}");
        }
    }

    /// <summary>Kills a process tree, returning why it could not be killed rather than pretending it was.</summary>
    private static string? TryKill(Process p)
    {
        try
        {
            p.Kill(true);
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return ex.Message;
        }
    }
}
