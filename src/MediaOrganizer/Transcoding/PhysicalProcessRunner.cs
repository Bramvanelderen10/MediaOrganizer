using System.ComponentModel;
using System.Diagnostics;

namespace MediaOrganizer.Transcoding;

/// <summary>
/// Default <see cref="IProcessRunner"/> that starts real processes and captures their output.
/// </summary>
public class PhysicalProcessRunner : IProcessRunner
{
    private readonly ILogger<PhysicalProcessRunner> _logger;

    public PhysicalProcessRunner(ILogger<PhysicalProcessRunner> logger)
    {
        _logger = logger;
    }

    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            throw new TranscodeException($"Could not start '{fileName}'. Is it installed and on the PATH?", ex);
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new TranscodeException($"'{fileName}' timed out after {timeout.TotalSeconds:0} seconds.");
        }

        return new ProcessResult(process.ExitCode, await standardOutput, await standardError);
    }

    private void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to terminate process {ProcessId}", process.Id);
        }
    }
}
