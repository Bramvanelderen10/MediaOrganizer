using MediaOrganizer.Configuration;

using Microsoft.Extensions.Options;

namespace MediaOrganizer.Transcoding;

/// <summary>
/// Uses <c>ffprobe</c> to read a file's video codec and <c>ffmpeg</c> to check which encoders
/// are compiled into the installed build.
/// </summary>
public class FfprobeVideoProbe : IVideoProbe
{
    private readonly ILogger<FfprobeVideoProbe> _logger;
    private readonly IProcessRunner _processRunner;
    private readonly TranscodingOptions _options;

    public FfprobeVideoProbe(
        ILogger<FfprobeVideoProbe> logger,
        IProcessRunner processRunner,
        IOptions<MediaOrganizerOptions> options)
    {
        _logger = logger;
        _processRunner = processRunner;
        _options = options.Value.Transcoding;
    }

    public async Task<string?> GetVideoCodecAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var arguments = new List<string>
        {
            "-v", "error",
            "-select_streams", "v:0",
            "-show_entries", "stream=codec_name",
            "-of", "default=noprint_wrappers=1:nokey=1",
            filePath
        };

        var timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 30, 3600));
        var result = await _processRunner.RunAsync("ffprobe", arguments, timeout, cancellationToken);

        if (result.ExitCode != 0)
        {
            _logger.LogWarning(
                "ffprobe could not read '{Path}' (exit {Code}): {Error}",
                filePath,
                result.ExitCode,
                result.StandardError.Trim());
            return null;
        }

        var codec = result.StandardOutput.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(codec) ? null : codec;
    }

    public async Task<bool> IsEncoderAvailableAsync(string encoder, CancellationToken cancellationToken = default)
    {
        var result = await _processRunner.RunAsync(
            "ffmpeg",
            ["-hide_banner", "-encoders"],
            TimeSpan.FromSeconds(60),
            cancellationToken);

        if (result.ExitCode != 0)
        {
            return false;
        }

        return result.StandardOutput.Contains(encoder, StringComparison.OrdinalIgnoreCase);
    }
}
