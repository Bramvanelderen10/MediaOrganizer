using MediaOrganizer.MoveHistory;
using MediaOrganizer.MovePlan;

using Microsoft.Extensions.Options;

namespace MediaOrganizer;

public class MediaFileOrganizer
{
    private readonly ILogger<MediaFileOrganizer> _logger;
    private readonly MediaOrganizerOptions _options;
    private readonly VideoFileFinder _videoFileFinder;
    private readonly MoveHistoryStore _moveHistoryStore;
    private readonly MediaGrouper _mediaGrouper;
    private readonly MovePlanBuilder _movePlanBuilder;

    public MediaFileOrganizer(
        ILogger<MediaFileOrganizer> logger,
        IOptions<MediaOrganizerOptions> options,
        VideoFileFinder videoFileFinder,
        MoveHistoryStore moveHistoryStore,
        MediaGrouper mediaGrouper,
        MovePlanBuilder movePlanBuilder)
    {
        _logger = logger;
        _options = options.Value;
        _videoFileFinder = videoFileFinder;
        _moveHistoryStore = moveHistoryStore;
        _mediaGrouper = mediaGrouper;
        _movePlanBuilder = movePlanBuilder;
    }

    public Task<MediaOrganizeSummary> OrganizeAsync(string? sourceFolderOverride = null)
    {
        var sourceFolder = sourceFolderOverride ?? _options.SourceFolder;
        if (string.IsNullOrWhiteSpace(sourceFolder))
        {
            throw new InvalidOperationException("Media source folder is not configured. Set MediaOrganizer:SourceFolder in appsettings.");
        }

        if (!Directory.Exists(sourceFolder))
        {
            throw new DirectoryNotFoundException($"Configured source folder does not exist: {sourceFolder}");
        }

        var extensions = _options.VideoExtensions is { Length: > 0 }
            ? _options.VideoExtensions
            : new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".m4v", ".webm", ".ts", ".mpg", ".mpeg" };

        var allVideoFiles = _videoFileFinder.GetVideoFiles(sourceFolder, extensions);

        var mediaGroups = _mediaGrouper.GroupMediaFiles(allVideoFiles);

        // Build the move plan - this checks history and updates database records
        var rootDestinationFolder = _options.DestinationFolder ?? sourceFolder;
        _movePlanBuilder.BuildMovePlan(mediaGroups, rootDestinationFolder);

        // Get entries that need to be moved (those with IsMoved = false)
        var entriesToMove = _moveHistoryStore.GetEntriesNeedingMove();
        var movedCount = ExecuteMovePlan(entriesToMove);

        var skippedCount = allVideoFiles.Count - entriesToMove.Count;

        return Task.FromResult(new MediaOrganizeSummary(allVideoFiles.Count, movedCount, skippedCount));
    }

    public Task<MediaRestoreSummary> RestoreAllAsync()
    {
        var pendingRestores = _moveHistoryStore.GetMovedEntriesForRestore();
        if (pendingRestores.Count == 0)
        {
            return Task.FromResult(new MediaRestoreSummary(0, 0, 0));
        }

        var restoredCount = 0;
        var skippedCount = 0;

        foreach (var item in pendingRestores)
        {
            if (!File.Exists(item.TargetFilePath))
            {
                skippedCount++;
                _logger.LogWarning(
                    "Skipping restore for history item {HistoryId}. Target file no longer exists: {TargetPath}",
                    item.Id,
                    item.TargetFilePath);
                continue;
            }

            if (File.Exists(item.OriginalFilePath))
            {
                skippedCount++;
                _logger.LogWarning(
                    "Skipping restore for history item {HistoryId}. Source path already exists: {SourcePath}",
                    item.Id,
                    item.OriginalFilePath);
                continue;
            }

            var sourceDirectory = Path.GetDirectoryName(item.OriginalFilePath)!;
            Directory.CreateDirectory(sourceDirectory);

            File.Move(item.TargetFilePath, item.OriginalFilePath);
            restoredCount++;
            _moveHistoryStore.UpdateIsMoved(item.Id, false);

            _logger.LogInformation("Restored '{Source}' <- '{Target}'", item.OriginalFilePath, item.TargetFilePath);
        }

        return Task.FromResult(new MediaRestoreSummary(pendingRestores.Count, restoredCount, skippedCount));
    }

    private int ExecuteMovePlan(IReadOnlyCollection<MoveHistoryEntry> movePlan)
    {
        var movedCount = 0;

        foreach (var item in movePlan)
        {
            if (!File.Exists(item.OriginalFilePath))
            {
                _logger.LogWarning("Skipping move for plan item {PlanId}. Source file no longer exists: {SourcePath}", item.Id, item.OriginalFilePath);
                continue;
            }

            var destinationDirectory = Path.GetDirectoryName(item.TargetFilePath)!;
            Directory.CreateDirectory(destinationDirectory);

            var uniqueDestinationPath = EnsureUniquePath(item.TargetFilePath);
            if (!string.Equals(uniqueDestinationPath, item.TargetFilePath, StringComparison.OrdinalIgnoreCase))
            {
                _moveHistoryStore.UpdateTargetPath(item.Id, uniqueDestinationPath);
            }

            File.Move(item.OriginalFilePath, uniqueDestinationPath);
            _moveHistoryStore.UpdateIsMoved(item.Id, true);

            _logger.LogInformation("Moved '{Source}' -> '{Destination}'", item.OriginalFilePath, uniqueDestinationPath);
            movedCount++;
        }

        return movedCount;
    }

    private static string EnsureUniquePath(string fullPath)
    {
        if (!File.Exists(fullPath))
        {
            return fullPath;
        }

        var directory = Path.GetDirectoryName(fullPath)!;
        var extension = Path.GetExtension(fullPath);
        var fileName = Path.GetFileNameWithoutExtension(fullPath);

        var index = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(directory, $"{fileName} ({index}){extension}");
            index++;
        }
        while (File.Exists(candidate));

        return candidate;
    }
}

public record MediaOrganizeSummary(int TotalFiles, int MovedFiles, int SkippedFiles);

public record MediaRestoreSummary(int TotalPendingFiles, int RestoredFiles, int SkippedFiles);
