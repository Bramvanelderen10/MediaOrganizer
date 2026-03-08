using MediaOrganizer.Helpers;
using MediaOrganizer.History;

namespace MediaOrganizer.Execution;

public class VideoMover
{
    private readonly ILogger<VideoMover> _logger;
    private readonly IFileSystem _fileSystem;
    private readonly MoveHistoryStore _moveHistoryStore;

    public VideoMover(
        ILogger<VideoMover> logger,
        IFileSystem fileSystem,
        MoveHistoryStore moveHistoryStore)
    {
        _logger = logger;
        _fileSystem = fileSystem;
        _moveHistoryStore = moveHistoryStore;
    }

    public List<MovedFileInfo> ExecuteMovePlan(IReadOnlyCollection<MoveHistoryEntry> movePlan)
    {
        var movedFiles = new List<MovedFileInfo>();

        foreach (var item in movePlan)
        {
            if (!_fileSystem.FileExists(item.OriginalFilePath))
            {
                _logger.LogWarning("Skipping move for plan item {PlanId}. Source file no longer exists: {SourcePath}", item.Id, item.OriginalFilePath);
                continue;
            }

            var destinationDirectory = Path.GetDirectoryName(item.TargetFilePath)!;
            _fileSystem.CreateDirectory(destinationDirectory);

            var uniqueDestinationPath = PathHelpers.EnsureUniquePath(item.TargetFilePath, _fileSystem);
            if (!string.Equals(uniqueDestinationPath, item.TargetFilePath, StringComparison.OrdinalIgnoreCase))
            {
                _moveHistoryStore.UpdateTargetPath(item.Id, uniqueDestinationPath);
            }

            _fileSystem.MoveFile(item.OriginalFilePath, uniqueDestinationPath);
            _moveHistoryStore.UpdateIsMoved(item.Id, true);

            _logger.LogInformation("Moved '{Source}' -> '{Destination}'", item.OriginalFilePath, uniqueDestinationPath);
            movedFiles.Add(new MovedFileInfo(item.OriginalFilePath, uniqueDestinationPath));
        }

        return movedFiles;
    }
}