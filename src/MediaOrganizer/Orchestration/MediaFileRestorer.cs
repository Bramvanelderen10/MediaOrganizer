using MediaOrganizer.Helpers;
using MediaOrganizer.History;

namespace MediaOrganizer.Orchestration;

public class MediaFileRestorer
{
    private readonly ILogger<MediaFileRestorer> _logger;
    private readonly IFileSystem _fileSystem;
    private readonly MoveHistoryStore _moveHistoryStore;

    public MediaFileRestorer(
        ILogger<MediaFileRestorer> logger,
        IFileSystem fileSystem,
        MoveHistoryStore moveHistoryStore)
    {
        _logger = logger;
        _fileSystem = fileSystem;
        _moveHistoryStore = moveHistoryStore;
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
            if (!_fileSystem.FileExists(item.TargetFilePath))
            {
                skippedCount++;
                _logger.LogWarning(
                    "Skipping restore for history item {HistoryId}. Target file no longer exists: {TargetPath}",
                    item.Id,
                    item.TargetFilePath);
                continue;
            }

            if (_fileSystem.FileExists(item.OriginalFilePath))
            {
                skippedCount++;
                _logger.LogWarning(
                    "Skipping restore for history item {HistoryId}. Source path already exists: {SourcePath}",
                    item.Id,
                    item.OriginalFilePath);
                continue;
            }

            var sourceDirectory = Path.GetDirectoryName(item.OriginalFilePath)!;
            _fileSystem.CreateDirectory(sourceDirectory);

            _fileSystem.MoveFile(item.TargetFilePath, item.OriginalFilePath);
            restoredCount++;
            _moveHistoryStore.UpdateIsMoved(item.Id, false);

            _logger.LogInformation("Restored '{Source}' <- '{Target}'", item.OriginalFilePath, item.TargetFilePath);
        }

        return Task.FromResult(new MediaRestoreSummary(pendingRestores.Count, restoredCount, skippedCount));
    }
}
