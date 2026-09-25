using MediaOrganizer.Orchestration;

namespace MediaOrganizer.Transcoding;

/// <summary>Lifecycle state of the background transcode job.</summary>
public enum TranscodeJobState
{
    Idle,
    Running,
    Completed,
    Failed
}

/// <summary>Snapshot of the transcode job, polled by the companion app's job screen.</summary>
public record TranscodeJobStatus(
    TranscodeJobState State,
    bool IsRunning,
    string? Label,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    int TotalFiles,
    int ProcessedFiles,
    int TranscodedFiles,
    int SkippedFiles,
    int FailedFiles,
    string? CurrentFile,
    string? Error);

/// <summary>
/// Runs transcodes in the background so the HTTP request returns immediately, and keeps the
/// latest status for the app's job screen to poll.
/// </summary>
/// <remarks>
/// The <see cref="JobLock"/> is the single source of truth for "a job is running": it is shared
/// with the organize job, so a transcode can never overlap one. The lock is held for the whole
/// background run and released when it finishes.
/// </remarks>
public sealed class TranscodeJobRunner
{
    private readonly ILogger<TranscodeJobRunner> _logger;
    private readonly TranscodeService _transcodeService;
    private readonly JobLock _jobLock;
    private readonly object _sync = new();

    private TranscodeJobStatus _status = Idle();

    public TranscodeJobRunner(
        ILogger<TranscodeJobRunner> logger,
        TranscodeService transcodeService,
        JobLock jobLock)
    {
        _logger = logger;
        _transcodeService = transcodeService;
        _jobLock = jobLock;
    }

    public TranscodeJobStatus GetStatus()
    {
        lock (_sync)
        {
            return _status;
        }
    }

    /// <summary>
    /// Resolves the target files, claims the shared job lock and starts the work in the
    /// background. Throws <see cref="JobAlreadyRunningException"/> when another job holds the
    /// lock and <see cref="TranscodeNotConfiguredException"/> when transcoding is disabled.
    /// </summary>
    public TranscodeJobStatus Start(string? label, string[]? paths)
    {
        _transcodeService.EnsureEnabled();

        var jobHandle = _jobLock.TryAcquire()
            ?? throw new JobAlreadyRunningException(
                "Another job (organize or transcode) is already running. Wait for it to finish and try again.");

        IReadOnlyList<string> files;
        try
        {
            // Resolve up front so an invalid request fails the HTTP call with a 400 instead of
            // starting a job that immediately errors.
            files = _transcodeService.ResolveTargets(paths);
        }
        catch
        {
            jobHandle.Dispose();
            throw;
        }

        var status = new TranscodeJobStatus(
            TranscodeJobState.Running,
            IsRunning: true,
            Label: string.IsNullOrWhiteSpace(label) ? "Library" : label.Trim(),
            StartedAt: DateTime.UtcNow,
            FinishedAt: null,
            TotalFiles: files.Count,
            ProcessedFiles: 0,
            TranscodedFiles: 0,
            SkippedFiles: 0,
            FailedFiles: 0,
            CurrentFile: null,
            Error: null);

        lock (_sync)
        {
            _status = status;
        }

        _ = Task.Run(() => RunAsync(jobHandle, files, status.Label));

        return status;
    }

    private async Task RunAsync(IDisposable jobHandle, IReadOnlyList<string> files, string? label)
    {
        try
        {
            var progress = new Progress<TranscodeProgress>(Report);
            var summary = await _transcodeService.TranscodeFilesAsync(files, CancellationToken.None, progress);

            lock (_sync)
            {
                _status = _status with
                {
                    State = TranscodeJobState.Completed,
                    IsRunning = false,
                    FinishedAt = DateTime.UtcNow,
                    CurrentFile = null,
                    TotalFiles = summary.TotalFiles,
                    ProcessedFiles = summary.TotalFiles,
                    TranscodedFiles = summary.TranscodedFiles,
                    SkippedFiles = summary.SkippedFiles,
                    FailedFiles = summary.FailedFiles
                };
            }

            _logger.LogInformation(
                "Transcode job '{Label}' finished: {Transcoded} transcoded, {Skipped} skipped, {Failed} failed",
                label,
                summary.TranscodedFiles,
                summary.SkippedFiles,
                summary.FailedFiles);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transcode job failed");
            lock (_sync)
            {
                _status = _status with
                {
                    State = TranscodeJobState.Failed,
                    IsRunning = false,
                    FinishedAt = DateTime.UtcNow,
                    CurrentFile = null,
                    Error = ex.Message
                };
            }
        }
        finally
        {
            jobHandle.Dispose();
        }
    }

    private void Report(TranscodeProgress progress)
    {
        lock (_sync)
        {
            if (!_status.IsRunning)
            {
                return;
            }

            _status = _status with
            {
                TotalFiles = progress.TotalFiles,
                ProcessedFiles = progress.ProcessedFiles,
                TranscodedFiles = progress.TranscodedFiles,
                SkippedFiles = progress.SkippedFiles,
                FailedFiles = progress.FailedFiles,
                CurrentFile = progress.CurrentFile
            };
        }
    }

    private static TranscodeJobStatus Idle() => new(
        TranscodeJobState.Idle,
        IsRunning: false,
        Label: null,
        StartedAt: null,
        FinishedAt: null,
        TotalFiles: 0,
        ProcessedFiles: 0,
        TranscodedFiles: 0,
        SkippedFiles: 0,
        FailedFiles: 0,
        CurrentFile: null,
        Error: null);
}