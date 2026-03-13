using System.Text.RegularExpressions;

using MediaOrganizer.Cleanup;
using MediaOrganizer.Configuration;
using MediaOrganizer.Discovery;
using MediaOrganizer.Execution;
using MediaOrganizer.Helpers;
using MediaOrganizer.History;
using MediaOrganizer.Parsing;
using MediaOrganizer.Planning;

using Microsoft.Extensions.Options;

namespace MediaOrganizer.Orchestration;

public class MediaFileOrganizer
{
    private readonly ILogger<MediaFileOrganizer> _logger;
    private readonly MediaOrganizerOptions _options;
    private readonly IFileSystem _fileSystem;
    private readonly VideoFileFinder _videoFileFinder;
    private readonly SettingStore _moveHistoryStore;
    private readonly MediaGrouper _mediaGrouper;
    private readonly MovePlanBuilder _movePlanBuilder;
    private readonly VideoMover _moveExecutor;
    private readonly SubtitleMover _subtitleMover;
    private readonly DirectoryCleaner _directoryCleaner;

    public MediaFileOrganizer(
        ILogger<MediaFileOrganizer> logger,
        IOptions<MediaOrganizerOptions> options,
        IFileSystem fileSystem,
        VideoFileFinder videoFileFinder,
        SettingStore moveHistoryStore,
        MediaGrouper mediaGrouper,
        MovePlanBuilder movePlanBuilder,
        VideoMover moveExecutor,
        SubtitleMover subtitleMover,
        DirectoryCleaner directoryCleaner)
    {
        _logger = logger;
        _options = options.Value;
        _fileSystem = fileSystem;
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

        if (!_fileSystem.DirectoryExists(sourceFolder))
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
}

public record MediaOrganizeSummary(int TotalFiles, int MovedFiles, int SkippedFiles, int SubtitlesMoved, int LeftoverFilesRemoved);

public record MediaRestoreSummary(int TotalPendingFiles, int RestoredFiles, int SkippedFiles);
