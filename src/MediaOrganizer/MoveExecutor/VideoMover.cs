using MediaOrganizer.MoveHistory;

namespace MediaOrganizer;

public class VideoMover
{
    private readonly ILogger<VideoMover> _logger;
    private readonly MoveHistoryStore _moveHistoryStore;

    public VideoMover(
        ILogger<VideoMover> logger,
        MoveHistoryStore moveHistoryStore)
    {
        _logger = logger;
        _moveHistoryStore = moveHistoryStore;
    }

    public List<MovedFileInfo> ExecuteMovePlan(IReadOnlyCollection<MoveHistoryEntry> movePlan)
    {
        var movedFiles = new List<MovedFileInfo>();

        foreach (var item in movePlan)
        {
            if (!File.Exists(item.OriginalFilePath))
            {
                _logger.LogWarning("Skipping move for plan item {PlanId}. Source file no longer exists: {SourcePath}", item.Id, item.OriginalFilePath);
                continue;
            }

            var destinationDirectory = Path.GetDirectoryName(item.TargetFilePath)!;
            Directory.CreateDirectory(destinationDirectory);

            var uniqueDestinationPath = PathHelpers.EnsureUniquePath(item.TargetFilePath);
            if (!string.Equals(uniqueDestinationPath, item.TargetFilePath, StringComparison.OrdinalIgnoreCase))
            {
                _moveHistoryStore.UpdateTargetPath(item.Id, uniqueDestinationPath);
            }

            File.Move(item.OriginalFilePath, uniqueDestinationPath);
            _moveHistoryStore.UpdateIsMoved(item.Id, true);

            _logger.LogInformation("Moved '{Source}' -> '{Destination}'", item.OriginalFilePath, uniqueDestinationPath);
            movedFiles.Add(new MovedFileInfo(item.OriginalFilePath, uniqueDestinationPath));
        }

        return movedFiles;
    }
}