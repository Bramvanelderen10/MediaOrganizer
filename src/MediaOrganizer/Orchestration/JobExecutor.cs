namespace MediaOrganizer.Orchestration;

public class JobExecutor
{
    private readonly ILogger<JobExecutor> _logger;
    private readonly MediaFileOrganizer _mediaFileOrganizer;
    private readonly JobLock _jobLock;

    public JobExecutor(ILogger<JobExecutor> logger, MediaFileOrganizer mediaFileOrganizer, JobLock jobLock)
    {
        _logger = logger;
        _mediaFileOrganizer = mediaFileOrganizer;
        _jobLock = jobLock;
    }

    public async Task<string> ExecuteJobAsync(string? sourceFolderOverride = null)
    {
        // The organize job and the transcode job move/rewrite the same files, so never allow
        // them to run at the same time.
        using var jobLock = _jobLock.TryAcquire()
            ?? throw new JobAlreadyRunningException(
                "Another job (organize or transcode) is already running. Wait for it to finish and try again.");

        _logger.LogInformation("=== Job execution started at {Time} ===", DateTime.Now);

        try
        {
            var summary = await _mediaFileOrganizer.OrganizeAsync(sourceFolderOverride);
            var result = $"Processed {summary.TotalFiles} video files. Moved {summary.MovedFiles}, skipped {summary.SkippedFiles}. Subtitles moved: {summary.SubtitlesMoved}. Leftover files removed: {summary.LeftoverFilesRemoved}.";
            _logger.LogInformation("=== Job execution completed at {Time} ===", DateTime.Now);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job execution failed");
            throw;
        }
    }
}
