namespace MediaOrganizer;

public class JobExecutor
{
    private readonly ILogger<JobExecutor> _logger;
    private readonly MediaFileOrganizer _mediaFileOrganizer;

    public JobExecutor(ILogger<JobExecutor> logger, MediaFileOrganizer mediaFileOrganizer)
    {
        _logger = logger;
        _mediaFileOrganizer = mediaFileOrganizer;
    }

    public async Task<string> ExecuteJobAsync(string? sourceFolderOverride = null)
    {
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
