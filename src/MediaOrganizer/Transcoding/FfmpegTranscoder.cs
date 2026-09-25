using System.Globalization;

using MediaOrganizer.Configuration;
using MediaOrganizer.Helpers;

using Microsoft.Extensions.Options;

namespace MediaOrganizer.Transcoding;

/// <summary>
/// Runs ffmpeg to re-encode the video stream of a file to the configured target codec.
/// </summary>
/// <remarks>
/// Audio and subtitle streams are copied untouched. The output is written to a temporary file
/// next to the source and only moved into place after ffmpeg exits successfully, so a failed
/// run never destroys the original. When the configured hardware encoder fails and
/// <see cref="TranscodingOptions.AllowSoftwareFallback"/> is set, the file is retried with
/// <c>libx264</c>.
/// </remarks>
public class FfmpegTranscoder : ITranscoder
{
    private const string SoftwareEncoder = "libx264";
    private const string VaapiEncoder = "h264_vaapi";

    private readonly ILogger<FfmpegTranscoder> _logger;
    private readonly IProcessRunner _processRunner;
    private readonly IFileSystem _fileSystem;
    private readonly TranscodingOptions _options;

    public FfmpegTranscoder(
        ILogger<FfmpegTranscoder> logger,
        IProcessRunner processRunner,
        IFileSystem fileSystem,
        IOptions<MediaOrganizerOptions> options)
    {
        _logger = logger;
        _processRunner = processRunner;
        _fileSystem = fileSystem;
        _options = options.Value.Transcoding;
    }

    public async Task<bool> TranscodeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var tempPath = TranscodeTempFiles.BuildTempPath(filePath);
        var timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 60, 86400));

        DeleteIfExists(tempPath);

        try
        {
            var succeeded = await RunEncoderAsync(_options.Encoder, filePath, tempPath, timeout, cancellationToken);

            if (!succeeded && _options.AllowSoftwareFallback && !IsSoftwareEncoder(_options.Encoder))
            {
                _logger.LogWarning(
                    "Hardware encoder '{Encoder}' failed for '{Path}'; retrying with {Fallback}",
                    _options.Encoder,
                    filePath,
                    SoftwareEncoder);

                DeleteIfExists(tempPath);
                succeeded = await RunEncoderAsync(SoftwareEncoder, filePath, tempPath, timeout, cancellationToken);
            }

            if (!succeeded || !_fileSystem.FileExists(tempPath))
            {
                DeleteIfExists(tempPath);
                _logger.LogError("Transcoding produced no output for '{Path}'", filePath);
                return false;
            }

            if (_options.KeepOriginal)
            {
                var outputPath = PathHelpers.EnsureUniquePath(BuildOutputPath(filePath), _fileSystem);
                _fileSystem.MoveFile(tempPath, outputPath);
                _logger.LogInformation("Transcoded '{Source}' -> '{Destination}' (original kept)", filePath, outputPath);
            }
            else
            {
                _fileSystem.DeleteFile(filePath);
                _fileSystem.MoveFile(tempPath, filePath);
                _logger.LogInformation("Transcoded '{Path}' in place", filePath);
            }

            return true;
        }
        catch (TranscodeException)
        {
            DeleteIfExists(tempPath);
            throw;
        }
        catch (Exception ex)
        {
            DeleteIfExists(tempPath);
            _logger.LogError(ex, "Unexpected error while transcoding '{Path}'", filePath);
            return false;
        }
    }

    private async Task<bool> RunEncoderAsync(
        string encoder,
        string inputPath,
        string outputPath,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Transcoding '{Path}' with encoder {Encoder}", inputPath, encoder);

        var result = await _processRunner.RunAsync("ffmpeg", BuildArguments(encoder, inputPath, outputPath), timeout, cancellationToken);
        if (result.ExitCode != 0)
        {
            _logger.LogWarning(
                "ffmpeg exited with code {Code} for '{Path}': {Error}",
                result.ExitCode,
                inputPath,
                Tail(result.StandardError));
            return false;
        }

        return true;
    }

    private IReadOnlyList<string> BuildArguments(string encoder, string inputPath, string outputPath)
    {
        var isVaapi = encoder.Equals(VaapiEncoder, StringComparison.OrdinalIgnoreCase);
        var arguments = new List<string> { "-hide_banner", "-nostdin", "-y" };

        if (isVaapi)
        {
            arguments.Add("-vaapi_device");
            arguments.Add(_options.HardwareDevice);
        }

        arguments.Add("-i");
        arguments.Add(inputPath);
        arguments.Add("-map");
        arguments.Add("0");

        if (isVaapi)
        {
            // Decode in software and upload the frames to the GPU: old Intel GPUs can encode
            // H.264 but cannot hardware-decode every source codec (e.g. HEVC on Broadwell).
            arguments.Add("-vf");
            arguments.Add("format=nv12,hwupload");
        }

        arguments.Add("-c:v");
        arguments.Add(encoder);

        if (IsSoftwareEncoder(encoder))
        {
            arguments.Add("-preset");
            arguments.Add(string.IsNullOrWhiteSpace(_options.Preset) ? "medium" : _options.Preset);
            arguments.Add("-crf");
            arguments.Add(_options.Quality.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            arguments.Add("-global_quality");
            arguments.Add(_options.Quality.ToString(CultureInfo.InvariantCulture));
        }

        arguments.Add("-c:a");
        arguments.Add("copy");
        arguments.Add("-c:s");
        arguments.Add("copy");
        arguments.Add(outputPath);

        return arguments;
    }

    private static bool IsSoftwareEncoder(string encoder)
        => encoder.Equals(SoftwareEncoder, StringComparison.OrdinalIgnoreCase);

    private string BuildOutputPath(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(filePath);
        return Path.Combine(directory, $"{stem}.{_options.TargetCodec}{Path.GetExtension(filePath)}");
    }

    private void DeleteIfExists(string path)
    {
        if (_fileSystem.FileExists(path))
        {
            _fileSystem.DeleteFile(path);
        }
    }

    private static string Tail(string value, int maxLength = 400)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[^maxLength..];
    }
}