using System.Text.RegularExpressions;

using MediaOrganizer.MediaGrouping;
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
    private readonly VideoMover _moveExecutor;
    private readonly SubtitleMover _subtitleMover;
    private readonly DirectoryCleaner _directoryCleaner;

    public MediaFileOrganizer(
        ILogger<MediaFileOrganizer> logger,
        IOptions<MediaOrganizerOptions> options,
        VideoFileFinder videoFileFinder,
        MoveHistoryStore moveHistoryStore,
        MediaGrouper mediaGrouper,
        MovePlanBuilder movePlanBuilder,
        VideoMover moveExecutor,
        SubtitleMover subtitleMover,
        DirectoryCleaner directoryCleaner)
    {
        _logger = logger;
        _options = options.Value;
        _videoFileFinder = videoFileFinder;
        _moveHistoryStore = moveHistoryStore;
        _mediaGrouper = mediaGrouper;
        _movePlanBuilder = movePlanBuilder;
        _moveExecutor = moveExecutor;
        _subtitleMover = subtitleMover;
        _directoryCleaner = directoryCleaner;
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

        var subtitleExtensions = _options.SubtitleExtensions is { Length: > 0 }
            ? _options.SubtitleExtensions
            : [".srt", ".sub", ".ass", ".ssa", ".vtt", ".idx"];

        var allVideoFiles = _videoFileFinder.GetVideoFiles(sourceFolder, extensions);

        var mediaGroups = _mediaGrouper.GroupMediaFiles(allVideoFiles);

        // Build the move plan - this checks history and updates database records
        var rootDestinationFolder = _options.DestinationFolder ?? sourceFolder;
        _movePlanBuilder.BuildMovePlan(mediaGroups, rootDestinationFolder);

        // Get entries that need to be moved (those with IsMoved = false)
        var entriesToMove = _moveHistoryStore.GetEntriesNeedingMove();
        var movedFiles = _moveExecutor.ExecuteMovePlan(entriesToMove);

        // Move companion subtitle files alongside their videos
        var movedSubtitles = _subtitleMover.MoveCompanionSubtitles(movedFiles, subtitleExtensions, sourceFolder);

        // Cleanup: remove any directory subtrees that no longer contain video/subtitle files
        var leftoverFilesRemoved = _directoryCleaner.CleanupDirectoriesWithoutMedia(sourceFolder, extensions, subtitleExtensions);
        var skippedCount = allVideoFiles.Count - entriesToMove.Count;

        return Task.FromResult(new MediaOrganizeSummary(
            allVideoFiles.Count,
            movedFiles.Count,
            skippedCount,
            movedSubtitles.Count,
            leftoverFilesRemoved));
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




}

public record MediaOrganizeSummary(int TotalFiles, int MovedFiles, int SkippedFiles, int SubtitlesMoved, int LeftoverFilesRemoved);

public record MediaRestoreSummary(int TotalPendingFiles, int RestoredFiles, int SkippedFiles);
