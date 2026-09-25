namespace MediaOrganizer.Transcoding;

/// <summary>
/// Result of an external process invocation.
/// </summary>
public record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// Abstraction over launching external processes so that transcoding can be unit-tested
/// without invoking ffmpeg/ffprobe.
/// </summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
