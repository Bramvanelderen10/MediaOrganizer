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

    public async Task<TranscodeEncodeTest> RunSelfTestAsync(CancellationToken cancellationToken = default)
    {
        var encoder = _options.Encoder;
        var timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 30, 300));

        _logger.LogInformation("Running transcode self-test with encoder {Encoder}", encoder);

        var result = await _processRunner.RunAsync(
            "ffmpeg",
            BuildSelfTestArguments(encoder),
            timeout,
            cancellationToken);

        var succeeded = result.ExitCode == 0;
        var output = Tail(result.StandardError, 1500);
        var driver = DetectDriver(result.StandardError)
            ?? await DetectDriverFromVainfoAsync(cancellationToken);

        if (succeeded)
        {
            _logger.LogInformation(
                "Transcode self-test succeeded with encoder {Encoder} (driver {Driver})",
                encoder,
                driver ?? "unknown");
        }
        else
        {
            _logger.LogWarning(
                "Transcode self-test failed with encoder {Encoder} (exit {Code}): {Error}",
                encoder,
                result.ExitCode,
                output);
        }

        return new TranscodeEncodeTest(
            succeeded,
            !IsSoftwareEncoder(encoder),
            driver,
            output,
            succeeded ? null : BuildHint(output, encoder));
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

    /// <summary>
    /// Builds an ffmpeg command that encodes a 2 second synthetic source and discards the output,
    /// so the configured encoder and driver can be exercised without touching any media file.
    /// </summary>
    private IReadOnlyList<string> BuildSelfTestArguments(string encoder)
    {
        var isVaapi = encoder.Equals(VaapiEncoder, StringComparison.OrdinalIgnoreCase);
        var arguments = new List<string> { "-hide_banner", "-nostdin", "-y" };

        if (isVaapi)
        {
            arguments.Add("-vaapi_device");
            arguments.Add(_options.HardwareDevice);
        }

        arguments.Add("-f");
        arguments.Add("lavfi");
        arguments.Add("-i");
        arguments.Add("testsrc=size=640x360:rate=25");
        arguments.Add("-t");
        arguments.Add("2");

        if (isVaapi)
        {
            arguments.Add("-vf");
            arguments.Add("format=nv12,hwupload");
        }

        arguments.Add("-c:v");
        arguments.Add(encoder);

        if (IsSoftwareEncoder(encoder))
        {
            arguments.Add("-preset");
            arguments.Add("ultrafast");
            arguments.Add("-crf");
            arguments.Add(_options.Quality.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            arguments.Add("-global_quality");
            arguments.Add(_options.Quality.ToString(CultureInfo.InvariantCulture));
        }

        arguments.Add("-f");
        arguments.Add("null");
        arguments.Add("-");

        return arguments;
    }

    /// <summary>
    /// Turns the common ffmpeg/VA-API failures into an actionable hint for the operator.
    /// </summary>
    private static string? BuildHint(string output, string encoder)
    {
        if (output.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
        {
            return "The container user cannot access the GPU device. Set VIDEO_GID and RENDER_GID "
                + "to the host groups that own /dev/dri (run: ls -ln /dev/dri).";
        }

        if (output.Contains("No VA display found", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Device creation failed", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Failed to initialise VAAPI", StringComparison.OrdinalIgnoreCase))
        {
            return "ffmpeg could not open the VA-API device. Check that /dev/dri is passed to the "
                + "container (devices: /dev/dri:/dev/dri) and that VIDEO_GID/RENDER_GID match the "
                + "host. On older Intel GPUs (5th gen / Broadwell and earlier) the default iHD "
                + "driver often cannot encode: add LIBVA_DRIVER_NAME=i965 to the container "
                + "environment. You can confirm the driver with: "
                + "vainfo --display drm --device /dev/dri/renderD128";
        }

        if (output.Contains("Unknown encoder", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Encoder not found", StringComparison.OrdinalIgnoreCase))
        {
            return $"The encoder '{encoder}' is not available in this ffmpeg build.";
        }

        if (output.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase))
        {
            return "The configured HardwareDevice does not exist. Check "
                + "MediaOrganizer__Transcoding__HardwareDevice and that /dev/dri is mapped in.";
        }

        return null;
    }

    /// <summary>Picks out which VA-API driver libva loaded from ffmpeg/vainfo output.</summary>
    private static string? DetectDriver(string output)
    {
        if (output.Contains("iHD_drv_video.so", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Intel iHD driver", StringComparison.OrdinalIgnoreCase))
        {
            return "iHD";
        }

        if (output.Contains("i965_drv_video.so", StringComparison.OrdinalIgnoreCase)
            || output.Contains("i965 driver", StringComparison.OrdinalIgnoreCase))
        {
            return "i965";
        }

        return null;
    }

    /// <summary>
    /// Asks <c>vainfo</c> which driver libva loads. ffmpeg does not print libva's info lines by
    /// default, so this is the reliable way to report the driver. Best effort: vainfo is optional.
    /// </summary>
    private async Task<string?> DetectDriverFromVainfoAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _processRunner.RunAsync(
                "vainfo",
                ["--display", "drm", "--device", _options.HardwareDevice],
                TimeSpan.FromSeconds(30),
                cancellationToken);

            if (result is null)
            {
                return null;
            }

            var text = (result.StandardOutput ?? string.Empty)
                + (result.StandardError ?? string.Empty);

            return DetectDriver(text);
        }
        catch (TranscodeException)
        {
            return null;
        }
    }

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