using MediaOrganizer.Configuration;
using MediaOrganizer.Helpers;
using MediaOrganizer.Orchestration;

using Microsoft.Extensions.Options;

namespace MediaOrganizer.Transcoding;

/// <summary>
/// Decides which files need transcoding by probing their video codec, then delegates the
/// conversion to <see cref="ITranscoder"/>. Failures on a single file are logged and counted
/// so one bad file never aborts the whole run.
/// </summary>
public class TranscodeService
{
    private static readonly string[] DefaultVideoExtensions =
    [
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".m4v", ".webm", ".ts", ".mpg", ".mpeg"
    ];

    private readonly ILogger<TranscodeService> _logger;
    private readonly MediaOrganizerOptions _mediaOptions;
    private readonly TranscodingOptions _options;
    private readonly IFileSystem _fileSystem;
    private readonly IVideoProbe _videoProbe;
    private readonly ITranscoder _transcoder;
    private readonly JobLock _jobLock;

    public TranscodeService(
        ILogger<TranscodeService> logger,
        IOptions<MediaOrganizerOptions> mediaOptions,
        IFileSystem fileSystem,
        IVideoProbe videoProbe,
        ITranscoder transcoder,
        JobLock jobLock)
    {
        _logger = logger;
        _mediaOptions = mediaOptions.Value;
        _options = _mediaOptions.Transcoding;
        _fileSystem = fileSystem;
        _videoProbe = videoProbe;
        _transcoder = transcoder;
        _jobLock = jobLock;
    }

    /// <summary>Whether transcoding is enabled in configuration.</summary>
    public bool IsEnabled => _options.Enabled;

    /// <summary>Reports the configured encoder and whether it is present in the local ffmpeg build.</summary>
    public async Task<TranscodeStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var hardwareAvailable = _options.Enabled
            && await _videoProbe.IsEncoderAvailableAsync(_options.Encoder, cancellationToken);

        return new TranscodeStatus(
            _options.Enabled,
            _options.Encoder,
            _options.HardwareDevice,
            hardwareAvailable);
    }

    /// <summary>
    /// Handles a transcode request. When <paramref name="paths"/> has entries only those files
    /// are processed (used by the per-item buttons in the companion app); otherwise the whole
    /// media library is scanned.
    /// </summary>
    public async Task<TranscodeSummary> TranscodeRequestAsync(
        string[]? paths,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            throw new TranscodeNotConfiguredException(
                "Transcoding is disabled. Set MediaOrganizer:Transcoding:Enabled=true to enable it.");
        }

        var requestedPaths = paths?.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray() ?? [];

        // Organize and transcode touch the same files, so never let them overlap.
        using var jobLock = _jobLock.TryAcquire()
            ?? throw new JobAlreadyRunningException(
                "Another job (organize or transcode) is already running. Wait for it to finish and try again.");

        if (requestedPaths.Length > 0)
        {
            var root = ResolveRootFolder();
            var files = requestedPaths.Select(path => ResolveWithinRoot(path, root)).ToList();
            return await TranscodeFilesAsync(files, cancellationToken);
        }

        return await TranscodeLibraryAsync(cancellationToken);
    }

    /// <summary>
    /// Scans the destination folder (falling back to the source folder) and transcodes every
    /// video file whose codec is not <c>Transcoding:TargetCodec</c>.
    /// </summary>
    private Task<TranscodeSummary> TranscodeLibraryAsync(CancellationToken cancellationToken)
    {
        var folder = ResolveRootFolder();

        if (!_fileSystem.DirectoryExists(folder))
        {
            throw new TranscodeValidationException($"Folder does not exist: {folder}");
        }

        var extensions = _mediaOptions.VideoExtensions is { Length: > 0 }
            ? _mediaOptions.VideoExtensions
            : DefaultVideoExtensions;

        var files = _fileSystem
            .EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Where(path => !TranscodeTempFiles.IsTempFile(path))
            .ToList();

        return TranscodeFilesAsync(files, cancellationToken);
    }

    /// <summary>Resolves and validates the configured media root folder.</summary>
    private string ResolveRootFolder()
    {
        var root = _mediaOptions.DestinationFolder ?? _mediaOptions.SourceFolder;
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new TranscodeValidationException(
                "No media folder configured. Set MediaOrganizer:SourceFolder or MediaOrganizer:DestinationFolder.");
        }

        return Path.GetFullPath(root);
    }

    /// <summary>
    /// Resolves a request path against the media root and rejects anything outside it, so a
    /// caller can never transcode arbitrary files on the host.
    /// </summary>
    private static string ResolveWithinRoot(string path, string root)
    {
        var trimmed = path.Trim();
        var candidate = Path.IsPathRooted(trimmed) ? trimmed : Path.Combine(root, trimmed);
        var fullPath = Path.GetFullPath(candidate);

        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            throw new TranscodeValidationException($"Path is outside the media folder: {path}");
        }

        return fullPath;
    }

    /// <summary>
    /// Transcodes the given files. Files whose codec already matches the target are skipped, as
    /// are files whose codec is not in <c>Transcoding:OnlyCodecs</c> (when that list is non-empty).
    /// </summary>
    public async Task<TranscodeSummary> TranscodeFilesAsync(
        IEnumerable<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        var files = filePaths.ToList();
        if (!_options.Enabled || files.Count == 0)
        {
            return new TranscodeSummary(files.Count, 0, files.Count, 0);
        }

        var allowedCodecs = _options.OnlyCodecs
            .Where(codec => !string.IsNullOrWhiteSpace(codec))
            .Select(NormalizeCodec)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targetCodec = NormalizeCodec(_options.TargetCodec);

        var transcoded = 0;
        var skipped = 0;
        var failed = 0;

        _logger.LogInformation(
            "Transcoding {Count} candidate file(s) with encoder {Encoder} (target {Target})",
            files.Count,
            _options.Encoder,
            targetCodec);

        foreach (var filePath in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Never treat an in-progress transcode output as a source file.
            if (TranscodeTempFiles.IsTempFile(filePath))
            {
                skipped++;
                continue;
            }

            if (!_fileSystem.FileExists(filePath))
            {
                skipped++;
                continue;
            }

            string? codec;
            try
            {
                codec = await _videoProbe.GetVideoCodecAsync(filePath, cancellationToken);
            }
            catch (TranscodeException ex)
            {
                _logger.LogError(ex, "Could not probe '{Path}'", filePath);
                failed++;
                continue;
            }

            if (codec is null || codec == targetCodec)
            {
                skipped++;
                continue;
            }

            // OnlyCodecs is an optional allowlist; an empty list means "anything not the target".
            if (allowedCodecs.Count > 0 && !allowedCodecs.Contains(codec))
            {
                skipped++;
                continue;
            }

            try
            {
                if (await _transcoder.TranscodeAsync(filePath, cancellationToken))
                {
                    transcoded++;
                }
                else
                {
                    failed++;
                }
            }
            catch (TranscodeException ex)
            {
                _logger.LogError(ex, "Transcoding '{Path}' failed", filePath);
                failed++;
            }
        }

        _logger.LogInformation(
            "Transcoding finished: {Transcoded} transcoded, {Skipped} skipped, {Failed} failed",
            transcoded,
            skipped,
            failed);

        return new TranscodeSummary(files.Count, transcoded, skipped, failed);
    }

    private static string NormalizeCodec(string codec)
        => codec.Trim().TrimStart('.').ToLowerInvariant();
}

/// <summary>Outcome of a transcode run.</summary>
public record TranscodeSummary(int TotalFiles, int TranscodedFiles, int SkippedFiles, int FailedFiles);

/// <summary>Current transcoding configuration and hardware availability.</summary>
public record TranscodeStatus(
    bool Enabled,
    string Encoder,
    string HardwareDevice,
    bool HardwareAvailable);