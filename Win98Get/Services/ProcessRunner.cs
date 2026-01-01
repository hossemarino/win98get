using System.Diagnostics;
using System.Text;

namespace Win98Get.Services;

public sealed class CommandResult
{
    public required int ExitCode { get; init; }
    public required string StdOut { get; init; }
    public required string StdErr { get; init; }
}

public static class ProcessRunner
{
    public static async Task<CommandResult> RunCaptureAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = new Process { StartInfo = psi };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new CommandResult
            {
                ExitCode = -1,
                StdOut = string.Empty,
                StdErr = ex.Message
            };
        }

        using var killReg = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Ignore kill failures.
            }
        });

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Ignore kill failures.
            }
            throw;
        }
        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;

        return new CommandResult
        {
            ExitCode = process.ExitCode,
            StdOut = stdOut,
            StdErr = stdErr
        };
    }

    public static async Task<int> RunInteractiveAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = true,
            CreateNoWindow = false,
            WindowStyle = ProcessWindowStyle.Normal,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }

    public static async Task<int> RunStreamingAsync(
        string fileName,
        string arguments,
        Action<string> onOutputLine,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                onOutputLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                onOutputLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var killReg = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    onOutputLine("(cancelling…)");
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Ignore kill failures.
            }
        });

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Ignore kill failures.
            }
            throw;
        }
        return process.ExitCode;
    }
}
